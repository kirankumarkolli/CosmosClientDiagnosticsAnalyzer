/**
 * Report Generator Module
 * Generates HTML report from analysis results
 */

class ReportGenerator {
    constructor() {
        this.jsonIdCounter = 0;
    }

    /**
     * Generate complete HTML report
     * @param {Object} result - Analysis result from Analyzer
     * @returns {string} HTML string
     */
    generate(result) {
        let html = '';

        // Summary section
        html += this.generateSummary(result);

        // System Metrics Time Plot (new)
        if (result.systemMetrics && result.systemMetrics.snapshots.length > 0) {
            html += this.generateSystemMetricsSection(result.systemMetrics);
        }

        // Client Configuration Time Plot (new)
        if (result.clientConfig && result.clientConfig.snapshots.length > 0) {
            html += this.generateClientConfigSection(result.clientConfig);
        }

        // Operation buckets
        if (result.operationBuckets.length > 0) {
            html += this.generateOperationBuckets(result);
        }

        // Network interactions (collapsible)
        if (result.networkInteractions.length > 0) {
            html += this.generateNetworkInteractions(result);
        }

        // Resource type groups
        if (result.resourceTypeGroups.length > 0) {
            html += this.generateGroupSection(
                '📁 GroupBy {ResourceType → OperationType}',
                result.resourceTypeGroups,
                'resourceType'
            );
        }

        // Status code groups
        if (result.statusCodeGroups.length > 0) {
            html += this.generateGroupSection(
                '🔢 GroupBy {StatusCode → SubStatusCode}',
                result.statusCodeGroups,
                'statusCode'
            );
        }

        // Transport event groups
        if (result.transportEventGroups.length > 0) {
            html += this.generateTransportEventSection(result.transportEventGroups);
        }

        return html;
    }

    /**
     * Generate summary section
     */
    generateSummary(result) {
        const repairedEntries = result.repairedEntries || 0;
        const failedEntries = result.failedEntries || 0;
        const parsedEntries = result.parsedEntries || result.totalEntries;
        
        return `
            <div class="section">
                <h2>📊 Summary</h2>
                <div class="table-container">
                    <div class="table-header">Parsing Statistics</div>
                    <table class="data-table">
                        <thead>
                            <tr>
                                <th class="row-num">#</th>
                                <th>Metric</th>
                                <th>Value</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr>
                                <td class="row-num">1</td>
                                <td>Total Lines</td>
                                <td><span class="num">${result.totalEntries.toLocaleString()}</span></td>
                            </tr>
                            <tr>
                                <td class="row-num">2</td>
                                <td>Successfully Parsed</td>
                                <td><span class="num">${parsedEntries.toLocaleString()}</span></td>
                            </tr>
                            <tr>
                                <td class="row-num">3</td>
                                <td>Repaired (Truncated JSON Fixed)</td>
                                <td><span class="num ${repairedEntries > 0 ? 'success' : ''}">${repairedEntries.toLocaleString()}</span></td>
                            </tr>
                            <tr>
                                <td class="row-num">4</td>
                                <td>Failed to Parse</td>
                                <td><span class="num ${failedEntries > 0 ? 'error' : ''}">${failedEntries.toLocaleString()}</span></td>
                            </tr>
                            <tr>
                                <td class="row-num">5</td>
                                <td>Latency Threshold</td>
                                <td><span class="num">${result.threshold.toLocaleString()} ms</span></td>
                            </tr>
                            <tr>
                                <td class="row-num">6</td>
                                <td>High Latency Entries</td>
                                <td><span class="num ${result.highLatencyEntries > 0 ? 'warning' : ''}">${result.highLatencyEntries.toLocaleString()}</span></td>
                            </tr>
                            <tr>
                                <td class="row-num">7</td>
                                <td>High Latency Rate</td>
                                <td><span class="num">${((result.highLatencyEntries / parsedEntries) * 100).toFixed(2)}%</span></td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </div>
        `;
    }

    /**
     * Generate operation buckets section
     */
    generateOperationBuckets(result) {
        let html = `
            <div class="section">
                <h2>📦 Operation Buckets</h2>
                <p class="note">Click on an operation name to see detailed entries</p>
                <div class="table-container">
                    <div class="table-header">Operations by Frequency (Threshold: ${result.threshold}ms)</div>
                    <table class="data-table" id="buckets-table">
                        <thead>
                            <tr>
                                <th class="row-num">#</th>
                                <th class="sortable" data-col="1">Operation<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="2">Count<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="3">Min (ms)<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="4">P50<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="5">P75<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="6">P90<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="7">P95<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="8">P99<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="9">Max (ms)<span class="sort-icon">⇅</span></th>
                                <th>NW Calls</th>
                            </tr>
                        </thead>
                        <tbody>
        `;

        result.operationBuckets.forEach((bucket, i) => {
            const bucketId = this.safeId(bucket.name);
            html += `
                <tr class="clickable-row" onclick="app.showBucket('${bucketId}')">
                    <td class="row-num">${i + 1}</td>
                    <td data-sort="${this.escapeAttr(bucket.name)}"><span class="link">${this.escape(bucket.name)}</span></td>
                    <td data-sort="${bucket.count}"><span class="num">${bucket.count.toLocaleString()}</span></td>
                    <td data-sort="${bucket.min}"><span class="num">${bucket.min.toFixed(2)}</span></td>
                    <td data-sort="${bucket.p50}"><span class="num">${bucket.p50.toFixed(2)}</span></td>
                    <td data-sort="${bucket.p75}"><span class="num">${bucket.p75.toFixed(2)}</span></td>
                    <td data-sort="${bucket.p90}"><span class="num">${bucket.p90.toFixed(2)}</span></td>
                    <td data-sort="${bucket.p95}"><span class="num">${bucket.p95.toFixed(2)}</span></td>
                    <td data-sort="${bucket.p99}"><span class="num">${bucket.p99.toFixed(2)}</span></td>
                    <td data-sort="${bucket.max}"><span class="num">${bucket.max.toFixed(2)}</span></td>
                    <td><span class="num">${bucket.minNwCount}-${bucket.maxNwCount}</span></td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';

        // Generate detail sections for each bucket with percentile breakdown
        for (const bucket of result.operationBuckets) {
            const bucketId = this.safeId(bucket.name);
            const allEntries = result.allHighLatencyDiagnostics
                .filter(e => e.name === bucket.name)
                .sort((a, b) => a.duration - b.duration);

            // Group entries by percentile ranges per SPEC:
            // P50: ≤ P50, P75: (P50, P75], P90: (P75, P90], P95: (P90, P95], P99: (P90, P99]
            const p50Entries = allEntries.filter(e => e.duration <= bucket.p50);
            const p75Entries = allEntries.filter(e => e.duration > bucket.p50 && e.duration <= bucket.p75);
            const p90Entries = allEntries.filter(e => e.duration > bucket.p75 && e.duration <= bucket.p90);
            const p95Entries = allEntries.filter(e => e.duration > bucket.p90 && e.duration <= bucket.p95);
            const p99Entries = allEntries.filter(e => e.duration > bucket.p95 && e.duration <= bucket.p99);
            const above99Entries = allEntries.filter(e => e.duration > bucket.p99);

            html += `
                <div id="bucket-${bucketId}" class="detail-section">
                    <button class="btn-close" onclick="app.closeBucket('${bucketId}')">&times;</button>
                    <h3>📋 ${this.escape(bucket.name)}</h3>
                    <p class="note">Total: ${bucket.count} entries grouped by percentile ranges</p>
                    
                    <div class="percentile-groups">
                        ${this.generatePercentileGroup('≤ P50', `≤ ${bucket.p50.toFixed(2)}ms`, p50Entries, `${bucketId}-p50`)}
                        ${this.generatePercentileGroup('P50 - P75', `${bucket.p50.toFixed(2)} - ${bucket.p75.toFixed(2)}ms`, p75Entries, `${bucketId}-p75`)}
                        ${this.generatePercentileGroup('P75 - P90', `${bucket.p75.toFixed(2)} - ${bucket.p90.toFixed(2)}ms`, p90Entries, `${bucketId}-p90`)}
                        ${this.generatePercentileGroup('P90 - P95', `${bucket.p90.toFixed(2)} - ${bucket.p95.toFixed(2)}ms`, p95Entries, `${bucketId}-p95`)}
                        ${this.generatePercentileGroup('P95 - P99', `${bucket.p95.toFixed(2)} - ${bucket.p99.toFixed(2)}ms`, p99Entries, `${bucketId}-p99`)}
                        ${this.generatePercentileGroup('> P99', `> ${bucket.p99.toFixed(2)}ms`, above99Entries, `${bucketId}-above99`)}
                    </div>
                </div>
            `;
        }

        html += '</div>';
        return html;
    }

    /**
     * Generate a collapsible percentile group
     */
    generatePercentileGroup(label, range, entries, groupId) {
        if (entries.length === 0) return '';
        
        return `
            <div class="subsection">
                <div class="collapsible-header" onclick="app.toggleSection('${groupId}')">
                    <h4>${label} (${entries.length} entries) - Range: ${range}</h4>
                    <span class="collapse-icon" id="${groupId}-icon">▶</span>
                </div>
                <div id="${groupId}" class="collapsible-content">
                    ${this.generateEntriesTable(entries, `table-${groupId}`)}
                </div>
            </div>
        `;
    }

    /**
     * Generate entries table with JSON viewer
     */
    generateEntriesTable(entries, tableId) {
        let html = `
            <div class="table-container">
                <table class="data-table" id="${tableId}">
                    <thead>
                        <tr>
                            <th class="row-num">#</th>
                            <th class="sortable" data-col="1">Start Time<span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="2">Duration (ms)<span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="3">Direct Calls<span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="4">Gateway Calls<span class="sort-icon">⇅</span></th>
                            <th>JSON</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        entries.forEach((entry, i) => {
            const jsonId = `json-${++this.jsonIdCounter}`;
            const jsonLen = entry.rawJson?.length || 0;
            const repairStatus = entry.wasRepaired ? '🔧 Repaired' : '✓ Valid';
            const repairClass = entry.wasRepaired ? 'warning' : 'success';
            // Store raw JSON without HTML escaping - script tags don't render HTML
            const safeJson = (entry.rawJson || '').replace(/<\/script>/gi, '<\\/script>');
            html += `
                <tr>
                    <td class="row-num">${i + 1}</td>
                    <td data-sort="${this.escapeAttr(entry.startTime)}"><span class="str">${this.escape(entry.startTime)}</span></td>
                    <td data-sort="${entry.duration}"><span class="num">${entry.duration.toFixed(2)}</span></td>
                    <td data-sort="${entry.directCalls}"><span class="num">${entry.directCalls}</span></td>
                    <td data-sort="${entry.gatewayCalls}"><span class="num">${entry.gatewayCalls}</span></td>
                    <td>
                        <span class="${repairClass}" style="font-size:11px;margin-right:6px;">${repairStatus}</span>
                        <button class="btn-view" onclick="app.showJson('${jsonId}')">📄 View (${this.formatSize(jsonLen)})</button>
                        <script type="application/json" id="${jsonId}">${safeJson}</script>
                    </td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';
        return html;
    }

    /**
     * Generate network interactions section (collapsible)
     */
    generateNetworkInteractions(result) {
        const highLatency = result.networkInteractions
            .filter(n => n.durationInMs > result.threshold)
            .slice(0, 100);

        if (highLatency.length === 0) return '';

        return `
            <div class="section">
                <div class="collapsible-header" onclick="app.toggleSection('nw-interactions')">
                    <h3>🌐 High Latency Network Interactions (${highLatency.length})</h3>
                    <span class="collapse-icon" id="nw-interactions-icon">▶</span>
                </div>
                <div id="nw-interactions" class="collapsible-content">
                    <div class="table-container">
                        <table class="data-table" id="nw-table">
                            <thead>
                                <tr>
                                    <th class="row-num">#</th>
                                    <th class="sortable" data-col="1">Resource<span class="sort-icon">⇅</span></th>
                                    <th class="sortable" data-col="2">Operation<span class="sort-icon">⇅</span></th>
                                    <th class="sortable" data-col="3">Status<span class="sort-icon">⇅</span></th>
                                    <th class="sortable" data-col="4">Duration (ms)<span class="sort-icon">⇅</span></th>
                                    <th class="sortable" data-col="5">BE Latency<span class="sort-icon">⇅</span></th>
                                    <th>Last Event</th>
                                </tr>
                            </thead>
                            <tbody>
                            ${highLatency.map((n, i) => `
                                <tr>
                                    <td class="row-num">${i + 1}</td>
                                    <td data-sort="${this.escapeAttr(n.resourceType)}"><span class="str">${this.escape(n.resourceType)}</span></td>
                                    <td data-sort="${this.escapeAttr(n.operationType)}"><span class="str">${this.escape(n.operationType)}</span></td>
                                    <td data-sort="${n.statusCode}"><span class="num">${n.statusCode}/${n.subStatusCode}</span></td>
                                    <td data-sort="${n.durationInMs}"><span class="num">${n.durationInMs.toFixed(2)}</span></td>
                                    <td data-sort="${parseFloat(n.beLatencyInMs) || 0}"><span class="num">${n.beLatencyInMs || '-'}</span></td>
                                    <td><span class="str">${this.escape(n.lastEvent)}</span></td>
                                </tr>
                            `).join('')}
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * Generate grouped section (resource type or status code)
     */
    generateGroupSection(title, groups, prefix) {
        let html = `
            <div class="section">
                <h2>${title}</h2>
                <p class="note">Click on a row to see detailed entries</p>
                <div class="table-container">
                    <table class="data-table" id="${prefix}-table">
                        <thead>
                            <tr>
                                <th class="row-num">#</th>
                                <th class="sortable" data-col="1">Key<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="2">Count<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="3">Min (ms)<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="4">P50<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="5">P75<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="6">P90<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="7">P95<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="8">P99<span class="sort-icon">⇅</span></th>
                                <th class="sortable" data-col="9">Max (ms)<span class="sort-icon">⇅</span></th>
                                <th>Action</th>
                            </tr>
                        </thead>
                        <tbody>
        `;

        groups.forEach((group, i) => {
            const groupId = this.safeId(`${prefix}-${group.key}`);
            html += `
                <tr class="clickable-row" onclick="app.showGroup('${groupId}')">
                    <td class="row-num">${i + 1}</td>
                    <td data-sort="${this.escapeAttr(group.key)}"><span class="link">${this.escape(group.key)}</span></td>
                    <td data-sort="${group.count}"><span class="num">${group.count.toLocaleString()}</span></td>
                    <td data-sort="${group.min}"><span class="num">${group.min.toFixed(2)}</span></td>
                    <td data-sort="${group.p50}"><span class="num">${group.p50.toFixed(2)}</span></td>
                    <td data-sort="${group.p75}"><span class="num">${group.p75.toFixed(2)}</span></td>
                    <td data-sort="${group.p90}"><span class="num">${group.p90.toFixed(2)}</span></td>
                    <td data-sort="${group.p95}"><span class="num">${group.p95.toFixed(2)}</span></td>
                    <td data-sort="${group.p99}"><span class="num">${group.p99.toFixed(2)}</span></td>
                    <td data-sort="${group.max}"><span class="num">${group.max.toFixed(2)}</span></td>
                    <td><button class="btn-view" onclick="event.stopPropagation(); app.showGroup('${groupId}')">📄 View</button></td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';

        // Generate detail sections
        for (const group of groups) {
            const groupId = this.safeId(`${prefix}-${group.key}`);
            html += `
                <div id="group-${groupId}" class="detail-section">
                    <button class="btn-close" onclick="app.closeGroup('${groupId}')">&times;</button>
                    <h3>📋 ${this.escape(group.key)}</h3>
                    <p class="note">Showing ${group.entries.length} of ${group.count} entries</p>
                    ${this.generateNetworkEntriesTable(group.entries, `group-${groupId}-entries`)}
                </div>
            `;
        }

        html += '</div>';
        return html;
    }

    /**
     * Generate network entries table
     */
    generateNetworkEntriesTable(entries, tableId) {
        let html = `
            <div class="table-container">
                <table class="data-table" id="${tableId}">
                    <thead>
                        <tr>
                            <th class="row-num">#</th>
                            <th class="sortable" data-col="1">Duration (ms)<span class="sort-icon">⇅</span></th>
                            <th>Status</th>
                            <th>Resource</th>
                            <th>Operation</th>
                            <th>JSON</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        entries.forEach((entry, i) => {
            const jsonId = `json-${++this.jsonIdCounter}`;
            // Store raw JSON without HTML escaping - script tags don't render HTML
            const safeJson = (entry.rawJson || '').replace(/<\/script>/gi, '<\\/script>');
            html += `
                <tr>
                    <td class="row-num">${i + 1}</td>
                    <td data-sort="${entry.durationInMs}"><span class="num">${entry.durationInMs.toFixed(2)}</span></td>
                    <td><span class="num">${entry.statusCode}/${entry.subStatusCode}</span></td>
                    <td><span class="str">${this.escape(entry.resourceType)}</span></td>
                    <td><span class="str">${this.escape(entry.operationType)}</span></td>
                    <td>
                        <button class="btn-view" onclick="app.showJson('${jsonId}')">📄 View</button>
                        <script type="application/json" id="${jsonId}">${safeJson}</script>
                    </td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';
        return html;
    }

    /**
     * Generate transport event section
     */
    generateTransportEventSection(groups) {
        let html = `
            <div class="section">
                <h2>🚀 GroupBy LastTransportEvent</h2>
                <p class="note">Click on an event to see phase breakdown and entries</p>
        `;

        for (const group of groups) {
            const groupId = this.safeId(`transport-${group.status}`);
            html += `
                <div class="subsection">
                    <div class="collapsible-header" onclick="app.toggleSection('${groupId}')">
                        <h4>${this.escape(group.status)} (${group.count} items) - P50: ${group.p50.toFixed(2)}ms, P75: ${group.p75.toFixed(2)}ms, P90: ${group.p90.toFixed(2)}ms, P95: ${group.p95.toFixed(2)}ms, P99: ${group.p99.toFixed(2)}ms</h4>
                        <span class="collapse-icon" id="${groupId}-icon">▶</span>
                    </div>
                    <div id="${groupId}" class="collapsible-content">
                        ${this.generatePhaseTable(group.phaseDetails, group.status)}
                    </div>
                </div>
            `;
        }

        html += '</div>';
        return html;
    }

    /**
     * Generate phase details table
     */
    generatePhaseTable(phases, transportEvent) {
        if (!phases || phases.length === 0) return '<p class="note">No phase data available</p>';

        let html = `
            <div class="table-container">
                <div class="table-header">Phase Breakdown</div>
                <p class="note">Click percentile values to view entries in that range</p>
                <table class="data-table">
                    <thead>
                        <tr>
                            <th class="row-num">#</th>
                            <th>Bottleneck Phase</th>
                            <th>Count</th>
                            <th>P50 (ms)</th>
                            <th>P75 (ms)</th>
                            <th>P90 (ms)</th>
                            <th>P95 (ms)</th>
                            <th>P99 (ms)</th>
                            <th>Endpoints</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        phases.forEach((phase, i) => {
            const phaseId = this.safeId(`phase-${transportEvent}-${phase.phase}`);
            html += `
                <tr>
                    <td class="row-num">${i + 1}</td>
                    <td><span class="str">${this.escape(phase.phase)}</span></td>
                    <td><span class="num">${phase.count.toLocaleString()}</span></td>
                    <td><a href="#${phaseId}-p50" class="percentile-link" onclick="app.toggleSection('${phaseId}-entries'); return false;"><span class="num">${phase.p50.toFixed(2)}</span></a></td>
                    <td><a href="#${phaseId}-p75" class="percentile-link" onclick="app.toggleSection('${phaseId}-entries'); return false;"><span class="num">${phase.p75.toFixed(2)}</span></a></td>
                    <td><a href="#${phaseId}-p90" class="percentile-link" onclick="app.toggleSection('${phaseId}-entries'); return false;"><span class="num">${phase.p90.toFixed(2)}</span></a></td>
                    <td><a href="#${phaseId}-p95" class="percentile-link" onclick="app.toggleSection('${phaseId}-entries'); return false;"><span class="num">${phase.p95.toFixed(2)}</span></a></td>
                    <td><a href="#${phaseId}-p99" class="percentile-link" onclick="app.toggleSection('${phaseId}-entries'); return false;"><span class="num">${phase.p99.toFixed(2)}</span></a></td>
                    <td><span class="num">${phase.endpointCount}</span></td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';
        
        // Add collapsible entries section for each phase
        for (const phase of phases) {
            const phaseId = this.safeId(`phase-${transportEvent}-${phase.phase}`);
            html += this.generatePhaseEntriesSection(phase, phaseId);
        }

        // Top endpoints
        for (const phase of phases.filter(p => p.topEndpoints.length > 0)) {
            html += `
                <details style="margin: 10px 0;">
                    <summary style="cursor: pointer; color: var(--accent-color);">
                        Top Endpoints for ${this.escape(phase.phase)} (${phase.topEndpoints.length})
                    </summary>
                    <div class="table-container" style="margin-top: 10px;">
                        <table class="data-table">
                            <thead>
                                <tr>
                                    <th class="row-num">#</th>
                                    <th>Endpoint</th>
                                    <th>Count</th>
                                </tr>
                            </thead>
                            <tbody>
                            ${phase.topEndpoints.map((ep, i) => `
                                <tr>
                                    <td class="row-num">${i + 1}</td>
                                    <td><span class="str">${this.escape(ep.endpoint)}</span></td>
                                    <td><span class="num">${ep.count}</span></td>
                                </tr>
                            `).join('')}
                            </tbody>
                        </table>
                    </div>
                </details>
            `;
        }

        return html;
    }

    /**
     * Generate phase entries section with percentile-based grouping
     */
    generatePhaseEntriesSection(phase, phaseId) {
        if (!phase.entries || phase.entries.length === 0) return '';

        // Group entries by percentile range
        const groups = {
            p50: [],
            p75: [],
            p90: [],
            p95: [],
            p99: [],
            above: []
        };

        for (const entry of phase.entries) {
            const dur = entry.durationInMs;
            if (dur <= phase.p50) {
                groups.p50.push(entry);
            } else if (dur <= phase.p75) {
                groups.p75.push(entry);
            } else if (dur <= phase.p90) {
                groups.p90.push(entry);
            } else if (dur <= phase.p95) {
                groups.p95.push(entry);
            } else if (dur <= phase.p99) {
                groups.p99.push(entry);
            } else {
                groups.above.push(entry);
            }
        }

        let html = `
            <div class="collapsible-header" onclick="app.toggleSection('${phaseId}-entries')" style="margin-top: 10px;">
                <h5>📊 ${this.escape(phase.phase)} Entries (${phase.entries.length})</h5>
                <span class="collapse-icon" id="${phaseId}-entries-icon">▶</span>
            </div>
            <div id="${phaseId}-entries" class="collapsible-content">
        `;

        // Generate group for each percentile range
        const groupDefs = [
            { key: 'p50', label: `≤P50 (≤${phase.p50.toFixed(2)}ms)` },
            { key: 'p75', label: `P50-P75 (${phase.p50.toFixed(2)}-${phase.p75.toFixed(2)}ms]` },
            { key: 'p90', label: `P75-P90 (${phase.p75.toFixed(2)}-${phase.p90.toFixed(2)}ms]` },
            { key: 'p95', label: `P90-P95 (${phase.p90.toFixed(2)}-${phase.p95.toFixed(2)}ms]` },
            { key: 'p99', label: `P95-P99 (${phase.p95.toFixed(2)}-${phase.p99.toFixed(2)}ms]` },
            { key: 'above', label: `>P99 (>${phase.p99.toFixed(2)}ms)` }
        ];

        for (const { key, label } of groupDefs) {
            if (groups[key].length > 0) {
                const groupId = `${phaseId}-${key}`;
                html += `
                    <div class="percentile-group">
                        <div class="collapsible-header" onclick="app.toggleSection('${groupId}')">
                            <span class="percentile-badge ${key}">${label} (${groups[key].length})</span>
                            <span class="collapse-icon" id="${groupId}-icon">▶</span>
                        </div>
                        <div id="${groupId}" class="collapsible-content">
                            ${this.generatePhaseEntryTable(groups[key])}
                        </div>
                    </div>
                `;
            }
        }

        html += '</div>';
        return html;
    }

    /**
     * Generate table for phase entries
     */
    generatePhaseEntryTable(entries) {
        let html = `
            <div class="table-container">
                <table class="data-table sortable-table">
                    <thead>
                        <tr>
                            <th class="row-num">#</th>
                            <th class="sortable" data-col="1">Duration (ms)<span class="sort-icon">⇅</span></th>
                            <th>Status</th>
                            <th>Resource</th>
                            <th>Operation</th>
                            <th>Endpoint</th>
                            <th>JSON</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        entries.forEach((entry, i) => {
            const jsonId = `json-${++this.jsonIdCounter}`;
            const safeJson = (entry.rawJson || '').replace(/<\/script>/gi, '<\\/script>');
            let endpoint = '';
            if (entry.storePhysicalAddress) {
                try {
                    const url = new URL(entry.storePhysicalAddress);
                    endpoint = url.host;
                } catch (e) {
                    endpoint = entry.storePhysicalAddress;
                }
            }
            html += `
                <tr>
                    <td class="row-num">${i + 1}</td>
                    <td data-sort="${entry.durationInMs}"><span class="num">${entry.durationInMs.toFixed(2)}</span></td>
                    <td><span class="num">${entry.statusCode || '-'}/${entry.subStatusCode || '-'}</span></td>
                    <td><span class="str">${this.escape(entry.resourceType || '-')}</span></td>
                    <td><span class="str">${this.escape(entry.operationType || '-')}</span></td>
                    <td><span class="str">${this.escape(endpoint || '-')}</span></td>
                    <td>
                        <button class="btn-view" onclick="app.showJson('${jsonId}')">📄 View</button>
                        <script type="application/json" id="${jsonId}">${safeJson}</script>
                    </td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';
        return html;
    }

    /**
     * Generate System Metrics Time Plot section
     */
    generateSystemMetricsSection(metrics) {
        const chartId = 'systemMetricsChart';
        const chartData = JSON.stringify({
            labels: metrics.snapshots.map(s => s.timestamp),
            datasets: [
                {
                    label: 'CPU (%)',
                    data: metrics.snapshots.map(s => s.cpu),
                    borderColor: '#4fc3f7',
                    backgroundColor: 'rgba(79, 195, 247, 0.1)',
                    yAxisID: 'y',
                    tension: 0.1
                },
                {
                    label: 'Memory (MB)',
                    data: metrics.snapshots.map(s => s.memoryMB),
                    borderColor: '#81c784',
                    backgroundColor: 'rgba(129, 199, 132, 0.1)',
                    yAxisID: 'y1',
                    tension: 0.1
                },
                {
                    label: 'Thread Wait (ms)',
                    data: metrics.snapshots.map(s => s.threadWaitMs),
                    borderColor: '#ffb74d',
                    backgroundColor: 'rgba(255, 183, 77, 0.1)',
                    yAxisID: 'y2',
                    tension: 0.1
                },
                {
                    label: 'TCP Connections',
                    data: metrics.snapshots.map(s => s.tcpConnections),
                    borderColor: '#ba68c8',
                    backgroundColor: 'rgba(186, 104, 200, 0.1)',
                    yAxisID: 'y3',
                    tension: 0.1
                }
            ]
        });

        let html = `
            <div class="section">
                <h2>📈 System Metrics Time Plot</h2>
                <p class="note">Showing ${metrics.snapshots.length} of ${metrics.totalSnapshots} snapshots. Click legend to toggle series.</p>
                <div class="chart-container" style="height: 400px; position: relative; background: var(--bg-secondary); border-radius: 6px;">
                    <canvas id="${chartId}"></canvas>
                    <noscript>JavaScript required for charts</noscript>
                </div>
                <script type="application/json" id="${chartId}-data">${chartData}</script>
                ${this.generateMetricsStatsTable(metrics.stats, 'System Metrics Statistics')}
            </div>
        `;

        return html;
    }

    /**
     * Generate Client Configuration Time Plot section
     */
    generateClientConfigSection(config) {
        const chartId = 'clientConfigChart';
        const chartData = JSON.stringify({
            labels: config.snapshots.map(s => s.timestamp),
            datasets: [
                {
                    label: 'Processor Count',
                    data: config.snapshots.map(s => s.processorCount),
                    borderColor: '#26c6da',
                    backgroundColor: 'rgba(38, 198, 218, 0.1)',
                    yAxisID: 'y',
                    tension: 0.1
                },
                {
                    label: 'Clients Created',
                    data: config.snapshots.map(s => s.clientsCreated),
                    borderColor: '#66bb6a',
                    backgroundColor: 'rgba(102, 187, 106, 0.1)',
                    yAxisID: 'y1',
                    tension: 0.1
                },
                {
                    label: 'Active Clients',
                    data: config.snapshots.map(s => s.activeClients),
                    borderColor: '#ffa726',
                    backgroundColor: 'rgba(255, 167, 38, 0.1)',
                    yAxisID: 'y1',
                    tension: 0.1
                }
            ]
        });

        let html = `
            <div class="section">
                <h2>⚙️ Client Configuration Time Plot</h2>
                <p class="note">Showing ${config.snapshots.length} of ${config.totalSnapshots} entries. 
                    ${config.uniqueMachines.length > 0 ? `Machines: ${config.uniqueMachines.join(', ')}` : ''}
                    ${config.connectionModes.length > 0 ? ` | Modes: ${config.connectionModes.join(', ')}` : ''}
                </p>
                <div class="chart-container" style="height: 400px; position: relative;">
                    <canvas id="${chartId}"></canvas>
                </div>
                <script type="application/json" id="${chartId}-data">${chartData}</script>
                ${this.generateConfigStatsTable(config.stats, 'Client Configuration Statistics')}
            </div>
        `;

        return html;
    }

    /**
     * Generate statistics table for system metrics
     */
    generateMetricsStatsTable(stats, title) {
        const rows = [
            { name: 'CPU (%)', ...stats.cpu, format: v => v.toFixed(2) },
            { name: 'Memory (MB)', ...stats.memory, format: v => v.toFixed(2) },
            { name: 'Thread Wait (ms)', ...stats.threadWait, format: v => v.toFixed(2) },
            { name: 'TCP Connections', ...stats.tcpConnections, format: v => Math.round(v) }
        ];

        return this.generateStatsTableHtml(rows, title);
    }

    /**
     * Generate statistics table for client config
     */
    generateConfigStatsTable(stats, title) {
        const rows = [
            { name: 'Processor Count', ...stats.processorCount, format: v => Math.round(v) },
            { name: 'Clients Created', ...stats.clientsCreated, format: v => Math.round(v) },
            { name: 'Active Clients', ...stats.activeClients, format: v => Math.round(v) }
        ];

        return this.generateStatsTableHtml(rows, title);
    }

    /**
     * Generate common stats table HTML
     */
    generateStatsTableHtml(rows, title) {
        let html = `
            <div class="table-container" style="margin-top: 20px;">
                <div class="table-header">${this.escape(title)}</div>
                <table class="data-table">
                    <thead>
                        <tr>
                            <th class="row-num">#</th>
                            <th>Metric</th>
                            <th>Min</th>
                            <th>P50</th>
                            <th>P75</th>
                            <th>P90</th>
                            <th>P95</th>
                            <th>P99</th>
                            <th>Max</th>
                            <th>Avg</th>
                            <th>Count</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        rows.forEach((row, i) => {
            const fmt = row.format || (v => v.toFixed(2));
            html += `
                <tr>
                    <td class="row-num">${i + 1}</td>
                    <td><span class="str">${this.escape(row.name)}</span></td>
                    <td><span class="num">${fmt(row.min)}</span></td>
                    <td><span class="num">${fmt(row.p50)}</span></td>
                    <td><span class="num">${fmt(row.p75)}</span></td>
                    <td><span class="num">${fmt(row.p90)}</span></td>
                    <td><span class="num">${fmt(row.p95)}</span></td>
                    <td><span class="num">${fmt(row.p99)}</span></td>
                    <td><span class="num">${fmt(row.max)}</span></td>
                    <td><span class="num">${fmt(row.avg)}</span></td>
                    <td><span class="num">${row.count.toLocaleString()}</span></td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';
        return html;
    }

    /**
     * Escape HTML
     */
    escape(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    /**
     * Escape for attribute
     */
    escapeAttr(str) {
        return this.escape(str).replace(/[\n\r]/g, ' ');
    }

    /**
     * Generate safe ID
     */
    safeId(name) {
        if (!name) return 'unknown';
        return btoa(unescape(encodeURIComponent(name))).replace(/[+/=]/g, '_');
    }

    /**
     * Format file size
     */
    formatSize(bytes) {
        if (bytes < 1024) return bytes + 'B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + 'KB';
        return (bytes / (1024 * 1024)).toFixed(1) + 'MB';
    }
}

// Export for browser
window.ReportGenerator = ReportGenerator;
