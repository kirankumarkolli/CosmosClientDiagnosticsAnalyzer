/**
 * Analyzer Module
 * Analyzes parsed diagnostics data and computes metrics
 */

class Analyzer {
    /**
     * Main analysis entry point
     * @param {Array} diagnostics - Parsed diagnostics objects
     * @param {number} threshold - Latency threshold in ms
     * @param {function} progressCallback - Progress callback
     * @returns {Object} Analysis result
     */
    analyze(diagnostics, threshold = 600, progressCallback = null) {
        const result = {
            totalEntries: diagnostics.length,
            threshold: threshold,
            highLatencyEntries: 0,
            operationBuckets: [],
            networkInteractions: [],
            resourceTypeGroups: [],
            statusCodeGroups: [],
            transportEventGroups: [],
            allHighLatencyDiagnostics: []
        };

        if (progressCallback) progressCallback('Filtering high latency entries...', 45);

        // Filter high latency entries
        const highLatency = diagnostics.filter(d => (d.duration || 0) > threshold);
        result.highLatencyEntries = highLatency.length;

        if (progressCallback) progressCallback('Building operation buckets...', 50);

        // Store all high latency diagnostics
        result.allHighLatencyDiagnostics = highLatency
            .map(d => ({
                name: d.name || 'Unknown',
                startTime: d.startTime || '',
                duration: d.duration || 0,
                directCalls: this.countCalls(d.Summary?.DirectCalls),
                gatewayCalls: this.countCalls(d.Summary?.GatewayCalls),
                rawJson: d._rawJson
            }))
            .sort((a, b) => b.duration - a.duration);

        // Group by operation name
        const bucketMap = new Map();
        for (const d of highLatency) {
            const name = d.name || 'Unknown';
            if (!bucketMap.has(name)) {
                bucketMap.set(name, []);
            }
            bucketMap.get(name).push(d);
        }

        // Compute bucket statistics
        result.operationBuckets = Array.from(bucketMap.entries())
            .map(([name, items]) => {
                const durations = items.map(x => x.duration || 0).sort((a, b) => a - b);
                const nwCounts = items.map(x => this.countCalls(x.Summary?.DirectCalls));
                
                return {
                    name,
                    count: items.length,
                    min: Math.min(...durations),
                    max: Math.max(...durations),
                    p50: this.percentile(durations, 50),
                    p75: this.percentile(durations, 75),
                    p90: this.percentile(durations, 90),
                    p95: this.percentile(durations, 95),
                    p99: this.percentile(durations, 99),
                    minNwCount: Math.min(...nwCounts),
                    maxNwCount: Math.max(...nwCounts)
                };
            })
            .sort((a, b) => b.count - a.count);

        if (progressCallback) progressCallback('Extracting network interactions...', 60);

        // Get the highest count operation for detailed analysis
        const targetOp = result.operationBuckets[0]?.name;
        if (targetOp) {
            const targetDiags = diagnostics.filter(d => d.name === targetOp);
            result.networkInteractions = this.extractNetworkInteractions(targetDiags);

            const highLatencyNw = result.networkInteractions
                .filter(n => n.durationInMs > threshold)
                .sort((a, b) => b.durationInMs - a.durationInMs);

            if (progressCallback) progressCallback('Computing grouped analysis...', 75);

            // Group by ResourceType -> OperationType
            result.resourceTypeGroups = this.groupBy(
                highLatencyNw,
                n => `${n.resourceType || 'Unknown'} → ${n.operationType || 'Unknown'}`
            );

            // Group by StatusCode -> SubStatusCode
            result.statusCodeGroups = this.groupBy(
                highLatencyNw,
                n => `${n.statusCode || 'Unknown'} → ${n.subStatusCode || 'Unknown'}`
            );

            if (progressCallback) progressCallback('Analyzing transport events...', 85);

            // Group by transport event
            result.transportEventGroups = this.computeTransportEventGroups(highLatencyNw);
        }

        if (progressCallback) progressCallback('Complete!', 100);

        return result;
    }

    /**
     * Count calls from summary object
     */
    countCalls(callsObj) {
        if (!callsObj) return 0;
        return Object.values(callsObj).reduce((sum, val) => sum + (val || 0), 0);
    }

    /**
     * Calculate percentile value
     */
    percentile(sortedArr, p) {
        if (sortedArr.length === 0) return 0;
        const index = Math.ceil((p / 100) * sortedArr.length) - 1;
        return sortedArr[Math.max(0, Math.min(index, sortedArr.length - 1))];
    }

    /**
     * Extract network interactions from diagnostics
     */
    extractNetworkInteractions(diagnostics) {
        const interactions = [];

        for (const diag of diagnostics) {
            const children = this.getRecursiveChildren(diag);
            
            for (const child of children) {
                const stats = child.data?.clientSideRequestStats?.storeResponseStatistics ||
                              child.data?.['Client Side Request Stats']?.StoreResponseStatistics;
                
                if (!stats) continue;

                for (const stat of stats) {
                    const storeResult = stat.storeResult || stat.StoreResult;
                    if (!storeResult) continue;

                    const physicalAddress = storeResult.storePhysicalAddress || 
                                           storeResult.StorePhysicalAddress;
                    if (!physicalAddress) continue;

                    const timeline = storeResult.transportRequestTimeline;
                    
                    interactions.push({
                        resourceType: stat.resourceType || stat.ResourceType,
                        operationType: stat.operationType || stat.OperationType,
                        statusCode: storeResult.statusCode || storeResult.StatusCode,
                        subStatusCode: storeResult.subStatusCode || storeResult.SubStatusCode,
                        durationInMs: stat.durationInMs || stat.DurationInMs || 0,
                        beLatencyInMs: storeResult.beLatencyInMs || storeResult.BELatencyInMs,
                        storePhysicalAddress: physicalAddress,
                        lastEvent: this.getLastEvent(timeline),
                        bottleneckEvent: this.getBottleneckEvent(timeline),
                        timelineEvents: this.extractTimelineEvents(timeline),
                        rawJson: diag._rawJson
                    });
                }
            }
        }

        return interactions;
    }

    /**
     * Recursively get all children nodes
     */
    getRecursiveChildren(obj, results = []) {
        if (!obj) return results;
        results.push(obj);
        if (obj.children && Array.isArray(obj.children)) {
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
        const events = timeline?.requestTimeline;
        if (!events) return null;
        const event = events.find(e => e.event === eventName);
        return event?.durationInMs ?? null;
    }

    /**
     * Get last completed event name
     */
    getLastEvent(timeline) {
        const events = timeline?.requestTimeline;
        if (!events || events.length === 0) return 'Unknown';
        
        const order = ['Completed', 'Received', 'Transit Time', 'Pipelined', 'ChannelAcquisitionStarted', 'Created'];
        for (const name of order) {
            if (events.find(e => e.event === name)) {
                return name;
            }
        }
        return events[events.length - 1]?.event || 'Unknown';
    }

    /**
     * Get bottleneck event (highest duration)
     */
    getBottleneckEvent(timeline) {
        const events = timeline?.requestTimeline;
        if (!events || events.length === 0) return null;
        
        return events.reduce((max, e) => 
            (e.durationInMs > (max?.durationInMs ?? 0)) ? e : max, null);
    }

    /**
     * Extract timeline events as object
     */
    extractTimelineEvents(timeline) {
        const events = timeline?.requestTimeline;
        if (!events) return {};
        
        const result = {};
        for (const e of events) {
            result[e.event] = e.durationInMs;
        }
        return result;
    }

    /**
     * Generic grouping function
     */
    groupBy(items, keyFn) {
        const groups = new Map();
        
        for (const item of items) {
            const key = keyFn(item);
            if (!groups.has(key)) {
                groups.set(key, []);
            }
            groups.get(key).push(item);
        }

        return Array.from(groups.entries())
            .map(([key, entries]) => {
                const durations = entries.map(e => e.durationInMs).sort((a, b) => a - b);
                return {
                    key,
                    count: entries.length,
                    min: Math.min(...durations),
                    max: Math.max(...durations),
                    p50: this.percentile(durations, 50),
                    p90: this.percentile(durations, 90),
                    p99: this.percentile(durations, 99),
                    entries: entries.slice(0, 50)
                };
            })
            .sort((a, b) => b.count - a.count);
    }

    /**
     * Compute transport event groupings with phase details
     */
    computeTransportEventGroups(interactions) {
        const groups = new Map();

        for (const item of interactions) {
            const key = item.lastEvent || 'Unknown';
            if (!groups.has(key)) {
                groups.set(key, []);
            }
            groups.get(key).push(item);
        }

        return Array.from(groups.entries())
            .map(([status, items]) => {
                // Group by bottleneck phase
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
                                    endpoints.set(url.host, (endpoints.get(url.host) || 0) + 1);
                                } catch (e) {
                                    endpoints.set(item.storePhysicalAddress, 
                                        (endpoints.get(item.storePhysicalAddress) || 0) + 1);
                                }
                            }
                        }

                        const durations = phaseItems.map(x => x.durationInMs).sort((a, b) => a - b);

                        return {
                            phase,
                            count: phaseItems.length,
                            min: Math.min(...durations),
                            max: Math.max(...durations),
                            p50: this.percentile(durations, 50),
                            p75: this.percentile(durations, 75),
                            p90: this.percentile(durations, 90),
                            p95: this.percentile(durations, 95),
                            p99: this.percentile(durations, 99),
                            endpointCount: endpoints.size,
                            topEndpoints: Array.from(endpoints.entries())
                                .map(([endpoint, count]) => ({ endpoint, count }))
                                .sort((a, b) => b.count - a.count)
                                .slice(0, 10),
                            entries: phaseItems.slice(0, 50)
                        };
                    })
                    .sort((a, b) => b.count - a.count);

                const durations = items.map(x => x.durationInMs).sort((a, b) => a - b);

                return {
                    status,
                    count: items.length,
                    min: Math.min(...durations),
                    max: Math.max(...durations),
                    p50: this.percentile(durations, 50),
                    p90: this.percentile(durations, 90),
                    phaseDetails,
                    entries: items.slice(0, 50)
                };
            })
            .sort((a, b) => b.count - a.count);
    }
}

// Export for browser
window.Analyzer = Analyzer;
