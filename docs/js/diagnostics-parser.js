/**
 * Cosmos Diagnostics Parser
 * Parses and repairs truncated JSON from Cosmos DB diagnostics logs
 */

class DiagnosticsParser {
    constructor() {
        this.repairedCount = 0;
    }

    /**
     * Main entry point - analyze diagnostics content
     */
    analyzeDiagnostics(fileContent, latencyThreshold = 600, progressCallback = null) {
        const result = {
            totalEntries: 0,
            parsedEntries: 0,
            repairedEntries: 0,
            highLatencyEntries: 0,
            operationBuckets: [],
            highLatencyNetworkInteractions: [],
            resourceTypeGroups: [],
            statusCodeGroups: [],
            transportEventGroups: [],
            allHighLatencyDiagnostics: []
        };

        const lines = fileContent.split('\n').filter(line => line.trim());
        result.totalEntries = lines.length;

        // Parse each line
        const diagnosticsList = [];
        this.repairedCount = 0;

        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            if (progressCallback && i % 100 === 0) {
                progressCallback(`Parsing line ${i + 1} of ${lines.length}...`, (i / lines.length) * 50);
            }

            const parsed = this.tryDeserializePartial(line);
            if (parsed) {
                parsed._rawJson = line.trim();
                diagnosticsList.push(parsed);
            }
        }

        result.parsedEntries = diagnosticsList.length;
        result.repairedEntries = this.repairedCount;

        // High latency diagnostics
        const highLatencyDiags = diagnosticsList.filter(e => e.duration > latencyThreshold);
        result.highLatencyEntries = highLatencyDiags.length;

        if (progressCallback) {
            progressCallback('Building operation buckets...', 55);
        }

        // Store all high latency diagnostics for drill-down
        result.allHighLatencyDiagnostics = highLatencyDiags
            .map(e => ({
                name: e.name,
                startTime: e.startTime,
                duration: e.duration,
                directCallCount: this.getDirectCallCount(e.Summary),
                gatewayCallCount: this.getGatewayCallCount(e.Summary),
                totalCallCount: this.getTotalCallCount(e.Summary),
                rawJson: e._rawJson
            }))
            .sort((a, b) => b.duration - a.duration);

        // Operation buckets
        const bucketMap = new Map();
        highLatencyDiags.forEach(e => {
            if (!e.name) return;
            if (!bucketMap.has(e.name)) {
                bucketMap.set(e.name, []);
            }
            bucketMap.get(e.name).push(e);
        });

        result.operationBuckets = Array.from(bucketMap.entries())
            .map(([name, items]) => ({
                bucket: name,
                min: Math.min(...items.map(x => x.duration)),
                max: Math.max(...items.map(x => x.duration)),
                minNWCount: Math.min(...items.map(x => this.getDirectCallCount(x.Summary))),
                maxNWCount: Math.max(...items.map(x => this.getDirectCallCount(x.Summary))),
                count: items.length
            }))
            .sort((a, b) => b.count - a.count);

        if (progressCallback) {
            progressCallback('Analyzing network interactions...', 65);
        }

        // Get high count operation name
        const highCountOpName = result.operationBuckets[0]?.bucket;
        if (!highCountOpName) {
            return result;
        }

        // Filter to target operation and extract network interactions
        const targetDiags = diagnosticsList.filter(e => e.name === highCountOpName);
        const nwInteractions = this.extractNetworkInteractions(targetDiags);

        const highLatencyNW = nwInteractions
            .filter(e => e.durationInMs > latencyThreshold)
            .sort((a, b) => b.durationInMs - a.durationInMs);

        result.highLatencyNetworkInteractions = highLatencyNW.slice(0, 100);

        if (progressCallback) {
            progressCallback('Building grouped analysis...', 80);
        }

        // Group by Resource Type -> Operation Type
        result.resourceTypeGroups = this.groupBy(highLatencyNW, 
            e => `${e.resourceType} -> ${e.operationType}`,
            e => ({
                durationInMs: e.durationInMs,
                statusCode: e.statusCode,
                subStatusCode: e.subStatusCode,
                resourceType: e.resourceType,
                operationType: e.operationType,
                rawJson: e.rawJson
            })
        );

        // Group by Status Code -> Sub Status Code
        result.statusCodeGroups = this.groupBy(highLatencyNW,
            e => `${e.statusCode} -> ${e.subStatusCode}`,
            e => ({
                durationInMs: e.durationInMs,
                statusCode: e.statusCode,
                subStatusCode: e.subStatusCode,
                resourceType: e.resourceType,
                operationType: e.operationType,
                rawJson: e.rawJson
            })
        );

        if (progressCallback) {
            progressCallback('Building transport event analysis...', 90);
        }

        // Group by Transport Event
        result.transportEventGroups = this.groupByTransportEvent(highLatencyNW);

        if (progressCallback) {
            progressCallback('Complete!', 100);
        }

        return result;
    }

    /**
     * Try to deserialize potentially truncated JSON
     */
    tryDeserializePartial(json) {
        if (!json || !json.trim()) return null;

        json = json.trim();

        // First try direct parse
        try {
            const parsed = JSON.parse(json);
            return this.normalizeKeys(parsed);
        } catch (e) {
            // Try to repair
        }

        // Try to repair truncated JSON
        let repaired = this.repairJson(json);
        try {
            const parsed = JSON.parse(repaired);
            this.repairedCount++;
            return this.normalizeKeys(parsed);
        } catch (e) {
            // Iterative truncation repair
            for (let i = 0; i < 10; i++) {
                repaired = this.repairJson(repaired);
                try {
                    const parsed = JSON.parse(repaired);
                    this.repairedCount++;
                    return this.normalizeKeys(parsed);
                } catch (e2) {
                    // Continue trying
                }
            }
        }

        return null;
    }

    /**
     * Repair truncated JSON
     */
    repairJson(json) {
        if (!json) return json;

        // Remove trailing truncation markers
        json = json.replace(/,?\s*\.{3,}\s*$/, '');
        json = json.replace(/,\s*$/, '');

        // Track open brackets
        const stack = [];
        let inString = false;
        let escapeNext = false;

        for (let i = 0; i < json.length; i++) {
            const c = json[i];

            if (escapeNext) {
                escapeNext = false;
                continue;
            }

            if (c === '\\' && inString) {
                escapeNext = true;
                continue;
            }

            if (c === '"' && !escapeNext) {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c === '{' || c === '[') {
                stack.push(c);
            } else if (c === '}') {
                if (stack.length > 0 && stack[stack.length - 1] === '{') {
                    stack.pop();
                }
            } else if (c === ']') {
                if (stack.length > 0 && stack[stack.length - 1] === '[') {
                    stack.pop();
                }
            }
        }

        // Close unclosed string
        if (inString) {
            json += '"';
        }

        // Remove trailing incomplete property
        json = json.replace(/,\s*"[^"]*"\s*:\s*$/, '');
        json = json.replace(/,\s*"[^"]*$/, '');

        // Close brackets in reverse order (LIFO)
        while (stack.length > 0) {
            const open = stack.pop();
            json += (open === '{') ? '}' : ']';
        }

        return json;
    }

    /**
     * Normalize JSON keys to camelCase for consistent access
     */
    normalizeKeys(obj) {
        if (!obj || typeof obj !== 'object') return obj;

        if (Array.isArray(obj)) {
            return obj.map(item => this.normalizeKeys(item));
        }

        const normalized = {};
        for (const key of Object.keys(obj)) {
            // Convert "duration in milliseconds" to "duration", etc.
            let newKey = key;
            if (key === 'duration in milliseconds') newKey = 'duration';
            else if (key === 'start time') newKey = 'startTime';
            else if (key === 'Client Side Request Stats') newKey = 'clientSideRequestStats';
            else if (key === 'StoreResponseStatistics') newKey = 'storeResponseStatistics';
            else if (key === 'AddressResolutionStatistics') newKey = 'addressResolutionStatistics';
            else if (key === 'transportRequestTimeline') newKey = 'transportRequestTimeline';
            else if (key === 'requestTimeline') newKey = 'requestTimeline';

            normalized[newKey] = this.normalizeKeys(obj[key]);
        }
        return normalized;
    }

    /**
     * Extract network interactions from diagnostics
     */
    extractNetworkInteractions(diagnostics) {
        const interactions = [];

        for (const diag of diagnostics) {
            const children = this.getRecursiveChildren(diag);
            for (const child of children) {
                const stats = child.data?.clientSideRequestStats?.storeResponseStatistics;
                if (!stats) continue;

                for (const stat of stats) {
                    if (!stat.StoreResult?.StorePhysicalAddress) continue;

                    const timeline = stat.StoreResult.transportRequestTimeline;
                    interactions.push({
                        resourceType: stat.ResourceType,
                        operationType: stat.OperationType,
                        statusCode: stat.StoreResult.StatusCode,
                        subStatusCode: stat.StoreResult.SubStatusCode,
                        durationInMs: stat.DurationInMs,
                        created: this.getTimelineEvent(timeline, 'Created'),
                        channelAcquisitionStarted: this.getTimelineEvent(timeline, 'ChannelAcquisitionStarted'),
                        pipelined: this.getTimelineEvent(timeline, 'Pipelined'),
                        transitTime: this.getTimelineEvent(timeline, 'Transit Time'),
                        received: this.getTimelineEvent(timeline, 'Received'),
                        completed: this.getTimelineEvent(timeline, 'Completed'),
                        beLatencyInMs: stat.StoreResult.BELatencyInMs,
                        lastEvent: this.getLastEvent(timeline),
                        bottleneckEvent: this.getBottleneckEvent(timeline),
                        storePhysicalAddress: stat.StoreResult.StorePhysicalAddress,
                        rawJson: diag._rawJson
                    });
                }
            }
        }

        return interactions;
    }

    /**
     * Get recursive children from diagnostics
     */
    getRecursiveChildren(obj, results = []) {
        if (!obj) return results;
        results.push(obj);
        if (obj.children) {
            for (const child of obj.children) {
                this.getRecursiveChildren(child, results);
            }
        }
        return results;
    }

    /**
     * Get timeline event duration
     */
    getTimelineEvent(timeline, eventName) {
        if (!timeline?.requestTimeline) return null;
        const event = timeline.requestTimeline.find(e => e.event === eventName);
        return event?.durationInMs ?? null;
    }

    /**
     * Get last completed event
     */
    getLastEvent(timeline) {
        if (!timeline?.requestTimeline) return 'Unknown';
        const events = ['Completed', 'Received', 'Transit Time', 'Pipelined', 'ChannelAcquisitionStarted', 'Created'];
        for (const evt of events) {
            if (timeline.requestTimeline.find(e => e.event === evt)) {
                return evt;
            }
        }
        return 'Unknown';
    }

    /**
     * Get bottleneck event (highest duration)
     */
    getBottleneckEvent(timeline) {
        if (!timeline?.requestTimeline || timeline.requestTimeline.length === 0) return null;
        return timeline.requestTimeline.reduce((max, e) => 
            (e.durationInMs > (max?.durationInMs ?? 0)) ? e : max, null);
    }

    /**
     * Get direct call count from summary
     */
    getDirectCallCount(summary) {
        if (!summary?.DirectCalls) return 0;
        return Object.values(summary.DirectCalls).reduce((sum, val) => sum + (val || 0), 0);
    }

    /**
     * Get gateway call count from summary
     */
    getGatewayCallCount(summary) {
        if (!summary?.GatewayCalls) return 0;
        return Object.values(summary.GatewayCalls).reduce((sum, val) => sum + (val || 0), 0);
    }

    /**
     * Get total call count
     */
    getTotalCallCount(summary) {
        return this.getDirectCallCount(summary) + this.getGatewayCallCount(summary);
    }

    /**
     * Group items by key and return with entries
     */
    groupBy(items, keyFn, entryMapper) {
        const groups = new Map();
        
        for (const item of items) {
            const key = keyFn(item);
            if (!groups.has(key)) {
                groups.set(key, []);
            }
            groups.get(key).push(item);
        }

        return Array.from(groups.entries())
            .map(([key, items]) => ({
                key,
                count: items.length,
                entries: items
                    .sort((a, b) => b.durationInMs - a.durationInMs)
                    .slice(0, 50)
                    .map(entryMapper)
            }))
            .sort((a, b) => b.count - a.count);
    }

    /**
     * Group by transport event with phase details
     */
    groupByTransportEvent(items) {
        const groups = new Map();

        for (const item of items) {
            const key = item.lastEvent || 'Unknown';
            if (!groups.has(key)) {
                groups.set(key, []);
            }
            groups.get(key).push(item);
        }

        return Array.from(groups.entries())
            .map(([status, items]) => {
                // Group by bottleneck event (phase)
                const phaseMap = new Map();
                for (const item of items) {
                    const phase = item.bottleneckEvent?.event || 'Unknown';
                    if (!phaseMap.has(phase)) {
                        phaseMap.set(phase, []);
                    }
                    phaseMap.get(phase).push(item);
                }

                const phaseDetails = Array.from(phaseMap.entries())
                    .map(([phase, phaseItems]) => {
                        // Count unique endpoints
                        const endpoints = new Map();
                        for (const item of phaseItems) {
                            if (item.storePhysicalAddress) {
                                try {
                                    const url = new URL(item.storePhysicalAddress);
                                    const endpoint = url.host;
                                    endpoints.set(endpoint, (endpoints.get(endpoint) || 0) + 1);
                                } catch (e) {}
                            }
                        }

                        return {
                            phase,
                            count: phaseItems.length,
                            minDuration: Math.min(...phaseItems.map(x => x.durationInMs)),
                            maxDuration: Math.max(...phaseItems.map(x => x.durationInMs)),
                            endpointCount: endpoints.size,
                            top10Endpoints: Array.from(endpoints.entries())
                                .map(([endpoint, count]) => ({ endpoint, count }))
                                .sort((a, b) => b.count - a.count)
                                .slice(0, 10),
                            entries: phaseItems
                                .sort((a, b) => b.durationInMs - a.durationInMs)
                                .slice(0, 50)
                                .map(e => ({
                                    durationInMs: e.durationInMs,
                                    statusCode: e.statusCode,
                                    subStatusCode: e.subStatusCode,
                                    resourceType: e.resourceType,
                                    operationType: e.operationType,
                                    rawJson: e.rawJson
                                }))
                        };
                    });

                return {
                    status,
                    count: items.length,
                    phaseDetails,
                    entries: items
                        .sort((a, b) => b.durationInMs - a.durationInMs)
                        .slice(0, 50)
                        .map(e => ({
                            durationInMs: e.durationInMs,
                            statusCode: e.statusCode,
                            subStatusCode: e.subStatusCode,
                            resourceType: e.resourceType,
                            operationType: e.operationType,
                            rawJson: e.rawJson
                        }))
                };
            })
            .sort((a, b) => b.count - a.count);
    }
}

// Export for use
window.DiagnosticsParser = DiagnosticsParser;
