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
     * @param {boolean} skipLatencyFilter - Skip latency filtering (for single entry)
     * @returns {Object} Analysis result
     */
    analyze(diagnostics, threshold = 600, progressCallback = null, skipLatencyFilter = false) {
        const result = {
            totalEntries: diagnostics.length,
            threshold: threshold,
            highLatencyEntries: 0,
            operationBuckets: [],
            networkInteractions: [],
            resourceTypeGroups: [],
            statusCodeGroups: [],
            transportEventGroups: [],
            transportExceptionGroups: [],
            allHighLatencyDiagnostics: [],
            systemMetrics: null,
            clientConfig: null
        };

        if (progressCallback) progressCallback('Extracting system metrics...', 40);
        
        // Extract system metrics and client config from ALL entries
        result.systemMetrics = this.extractSystemMetrics(diagnostics);
        result.clientConfig = this.extractClientConfig(diagnostics);

        if (progressCallback) progressCallback('Filtering high latency entries...', 45);

        // Filter high latency entries (skip filter for single entry mode)
        const highLatency = skipLatencyFilter 
            ? diagnostics 
            : diagnostics.filter(d => (d.duration || 0) > threshold);
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
                rawJson: d._rawJson,
                wasRepaired: d._wasRepaired || false
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
            // Use highLatency (filtered entries) for multi-entry mode, all diagnostics for single entry
            const targetDiags = highLatency.filter(d => d.name === targetOp);
            result.networkInteractions = this.extractNetworkInteractions(targetDiags);

            // For single entry mode, include all network interactions; for multi-entry, no additional filter needed
            // since targetDiags already comes from highLatency entries
            const highLatencyNw = result.networkInteractions;
            highLatencyNw.sort((a, b) => b.durationInMs - a.durationInMs);

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
            
            // Group by transport exception (truncate at "(Time:" to group similar exceptions)
            result.transportExceptionGroups = this.groupBy(
                highLatencyNw.filter(n => n.transportException),
                n => {
                    const exception = n.transportException || 'None';
                    const timeIndex = exception.indexOf('(Time:');
                    return timeIndex !== -1 ? exception.substring(0, timeIndex).trim() : exception;
                }
            );
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
                    const transportException = storeResult.transportException || storeResult.TransportException;
                    
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
                        transportException: transportException ? (transportException.message || transportException.Message || JSON.stringify(transportException)) : null,
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
                    p75: this.percentile(durations, 75),
                    p90: this.percentile(durations, 90),
                    p95: this.percentile(durations, 95),
                    p99: this.percentile(durations, 99),
                    entries: entries
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
                            entries: phaseItems
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
                    p75: this.percentile(durations, 75),
                    p90: this.percentile(durations, 90),
                    p95: this.percentile(durations, 95),
                    p99: this.percentile(durations, 99),
                    phaseDetails,
                    entries: items
                };
            })
            .sort((a, b) => b.count - a.count);
    }

    /**
     * Extract system metrics from all diagnostics entries
     * @param {Array} diagnostics - Parsed diagnostics objects
     * @returns {Object} System metrics with snapshots and statistics
     */
    extractSystemMetrics(diagnostics) {
        const snapshots = [];
        
        for (const diag of diagnostics) {
            const children = this.getRecursiveChildren(diag);
            const diagTime = diag.startTime || diag['start datetime'];
            
            for (const child of children) {
                // Try multiple paths where System Info might exist
                const systemInfoSources = [
                    child.data?.['System Info'],
                    child.data?.systemInfo,
                    child.data?.SystemInfo,
                    child.data?.clientSideRequestStats?.SystemInfo,
                    child.data?.clientSideRequestStats?.systemInfo,
                    child.data?.['Client Side Request Stats']?.SystemInfo,
                    child.data?.['Client Side Request Stats']?.systemInfo,
                    child.data?.['Client Side Request Stats']?.['System Info']
                ];
                
                for (const systemInfo of systemInfoSources) {
                    if (!systemInfo?.systemHistory && !systemInfo?.SystemHistory) continue;
                    
                    const history = systemInfo.systemHistory || systemInfo.SystemHistory || [];
                    
                    for (const entry of history) {
                        const snapshot = {
                            timestamp: entry.DateUtc || entry.dateUtc || diagTime,
                            cpu: entry.Cpu ?? entry.cpu ?? 0,
                            memoryBytes: entry.Memory ?? entry.memory ?? 0,
                            memoryMB: (entry.Memory ?? entry.memory ?? 0) / (1024 * 1024),
                            threadWaitMs: entry.ThreadInfo?.ThreadWaitIntervalInMs ?? 
                                          entry.threadInfo?.threadWaitIntervalInMs ?? 0,
                            tcpConnections: entry.NumberOfOpenTcpConnection ?? 
                                            entry.numberOfOpenTcpConnection ?? 0,
                            availableThreads: entry.ThreadInfo?.AvailableThreads ?? 
                                              entry.threadInfo?.availableThreads ?? 0,
                            isThreadStarving: entry.ThreadInfo?.IsThreadStarving ?? 
                                              entry.threadInfo?.isThreadStarving ?? 'N/A'
                        };
                        snapshots.push(snapshot);
                    }
                }
            }
        }
        
        // Sort by timestamp and deduplicate by timestamp
        snapshots.sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp));
        
        // Remove duplicates based on timestamp
        const uniqueSnapshots = [];
        const seenTimestamps = new Set();
        for (const s of snapshots) {
            const key = `${s.timestamp}-${s.cpu}-${s.memoryBytes}`;
            if (!seenTimestamps.has(key)) {
                seenTimestamps.add(key);
                uniqueSnapshots.push(s);
            }
        }
        
        // Calculate statistics for each metric
        const cpuValues = uniqueSnapshots.map(s => s.cpu).filter(v => v != null && v > 0).sort((a, b) => a - b);
        const memoryValues = uniqueSnapshots.map(s => s.memoryMB).filter(v => v != null && v > 0).sort((a, b) => a - b);
        const threadWaitValues = uniqueSnapshots.map(s => s.threadWaitMs).filter(v => v != null).sort((a, b) => a - b);
        const tcpValues = uniqueSnapshots.map(s => s.tcpConnections).filter(v => v != null).sort((a, b) => a - b);
        
        return {
            snapshots: uniqueSnapshots.slice(0, 500), // Limit for performance
            totalSnapshots: uniqueSnapshots.length,
            stats: {
                cpu: this.computeMetricStats(cpuValues),
                memory: this.computeMetricStats(memoryValues),
                threadWait: this.computeMetricStats(threadWaitValues),
                tcpConnections: this.computeMetricStats(tcpValues)
            }
        };
    }

    /**
     * Extract client configuration metrics from all diagnostics entries
     * @param {Array} diagnostics - Parsed diagnostics objects
     * @returns {Object} Client config with heatmap data and raw snapshots for filtering
     */
    extractClientConfig(diagnostics) {
        const snapshots = [];
        
        for (const diag of diagnostics) {
            const config = diag.data?.['Client Configuration'] || 
                           diag.data?.clientConfiguration ||
                           diag.data?.ClientConfiguration;
            
            const machineId = config?.MachineId ?? config?.machineId ?? 'Unknown';
            const duration = diag.duration ?? diag['duration in milliseconds'] ?? 0;
            const timestamp = diag.startTime ?? diag['start datetime'];
            
            if (!timestamp) continue;
            
            const snapshot = {
                timestamp: timestamp,
                duration: duration,
                machineId: machineId,
                connectionMode: config?.ConnectionMode ?? config?.connectionMode ?? '',
                rawJson: diag._rawJson
            };
            snapshots.push(snapshot);
        }
        
        // Sort by timestamp
        snapshots.sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp));
        
        // Compute heatmap buckets
        const heatmapData = this.computeHeatmapBuckets(snapshots);
        
        // Get unique machines and modes
        const uniqueMachines = [...new Set(snapshots.map(s => s.machineId).filter(Boolean))];
        const connectionModes = [...new Set(snapshots.map(s => s.connectionMode).filter(Boolean))];
        
        return {
            snapshots: snapshots, // Keep all for filtering
            totalSnapshots: snapshots.length,
            heatmapData: heatmapData,
            uniqueMachines: uniqueMachines,
            connectionModes: connectionModes
        };
    }

    /**
     * Compute heatmap buckets from snapshots
     * @param {Array} snapshots - Array of {timestamp, duration, machineId}
     * @returns {Object} Heatmap data with time buckets, latency buckets, and counts
     */
    computeHeatmapBuckets(snapshots) {
        if (snapshots.length === 0) {
            return { timeBuckets: [], latencyBuckets: [], data: [], maxCount: 0 };
        }

        // Define latency buckets
        const latencyBuckets = [
            { min: 0, max: 100, label: '0-100ms' },
            { min: 100, max: 500, label: '100-500ms' },
            { min: 500, max: 1000, label: '500ms-1s' },
            { min: 1000, max: 2000, label: '1-2s' },
            { min: 2000, max: 5000, label: '2-5s' },
            { min: 5000, max: Infinity, label: '5s+' }
        ];

        // Determine time range and bucket size
        const timestamps = snapshots.map(s => new Date(s.timestamp).getTime());
        const minTime = Math.min(...timestamps);
        const maxTime = Math.max(...timestamps);
        const timeRange = maxTime - minTime;
        
        // Aim for ~30-60 time buckets
        let bucketSizeMs;
        if (timeRange <= 60 * 60 * 1000) { // <= 1 hour
            bucketSizeMs = 60 * 1000; // 1 minute
        } else if (timeRange <= 24 * 60 * 60 * 1000) { // <= 1 day
            bucketSizeMs = 5 * 60 * 1000; // 5 minutes
        } else {
            bucketSizeMs = 60 * 60 * 1000; // 1 hour
        }

        // Create time buckets
        const timeBuckets = [];
        const timeBucketMap = new Map(); // timestamp -> index
        let currentTime = Math.floor(minTime / bucketSizeMs) * bucketSizeMs;
        let idx = 0;
        while (currentTime <= maxTime) {
            const label = new Date(currentTime).toISOString().replace('T', ' ').substring(11, 19);
            timeBuckets.push({ time: currentTime, label: label });
            timeBucketMap.set(currentTime, idx);
            currentTime += bucketSizeMs;
            idx++;
        }

        // Count entries per bucket
        const countMap = new Map(); // "timeIdx,latencyIdx" -> count
        
        for (const s of snapshots) {
            const time = new Date(s.timestamp).getTime();
            const timeBucket = Math.floor(time / bucketSizeMs) * bucketSizeMs;
            const timeIdx = timeBucketMap.get(timeBucket);
            
            // Find latency bucket
            let latencyIdx = latencyBuckets.length - 1; // default to last bucket
            for (let i = 0; i < latencyBuckets.length; i++) {
                if (s.duration >= latencyBuckets[i].min && s.duration < latencyBuckets[i].max) {
                    latencyIdx = i;
                    break;
                }
            }
            
            const key = `${timeIdx},${latencyIdx}`;
            countMap.set(key, (countMap.get(key) || 0) + 1);
        }

        // Convert to heatmap data array [[timeIdx, latencyIdx, count], ...]
        const data = [];
        let maxCount = 0;
        for (const [key, count] of countMap) {
            const [timeIdx, latencyIdx] = key.split(',').map(Number);
            data.push([timeIdx, latencyIdx, count]);
            maxCount = Math.max(maxCount, count);
        }

        return {
            timeBuckets: timeBuckets.map(t => t.label),
            timeBucketsRaw: timeBuckets, // Keep raw for filtering
            latencyBuckets: latencyBuckets.map(l => l.label),
            latencyBucketsRaw: latencyBuckets, // Keep raw for filtering
            data: data,
            maxCount: maxCount,
            bucketSizeMs: bucketSizeMs
        };
    }

    /**
     * Compute statistics for a metric array
     * @param {Array} sortedValues - Sorted array of numeric values
     * @returns {Object} Statistics object
     */
    computeMetricStats(sortedValues) {
        if (sortedValues.length === 0) {
            return { min: 0, max: 0, avg: 0, p50: 0, p75: 0, p90: 0, p95: 0, p99: 0, count: 0 };
        }
        
        const sum = sortedValues.reduce((a, b) => a + b, 0);
        return {
            min: sortedValues[0],
            max: sortedValues[sortedValues.length - 1],
            avg: sum / sortedValues.length,
            p50: this.percentile(sortedValues, 50),
            p75: this.percentile(sortedValues, 75),
            p90: this.percentile(sortedValues, 90),
            p95: this.percentile(sortedValues, 95),
            p99: this.percentile(sortedValues, 99),
            count: sortedValues.length
        };
    }
}

// Export for browser
window.Analyzer = Analyzer;
