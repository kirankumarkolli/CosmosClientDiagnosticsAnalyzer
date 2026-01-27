/**
 * HTML Generator for Diagnostics Results
 * Generates LinqPad-style HTML reports
 */

class HtmlGenerator {
    constructor() {
        this.jsonModalId = 0;
    }

    /**
     * Generate full HTML report
     */
    generateHtml(result) {
        let html = '';

        // Summary section
        html += this.generateSection('📊 Summary', this.generateSummaryTable(result));

        // Operation Buckets
        if (result.operationBuckets.length > 0) {
            html += this.generateSection('📦 Operation Buckets', 
                '<p class="note">Click on a bucket name to see related entries</p>' +
                this.generateOperationBucketsTable(result.operationBuckets) +
                this.generateBucketDetailsSections(result)
            );
        }

        // High Latency Network Interactions (collapsible)
        if (result.highLatencyNetworkInteractions.length > 0) {
            html += this.generateCollapsibleSection(
                '🌐 High Latency Network Interactions',
                `Top ${Math.min(100, result.highLatencyNetworkInteractions.length)}`,
                this.generateTable('Network Interactions', 
                    result.highLatencyNetworkInteractions.slice(0, 20),
                    ['resourceType', 'operationType', 'statusCode', 'subStatusCode', 'durationInMs', 'lastEvent', 'beLatencyInMs']
                ) +
                (result.highLatencyNetworkInteractions.length > 20 
                    ? `<p class="note">Showing 20 of ${result.highLatencyNetworkInteractions.length} interactions</p>` 
                    : ''),
                'nwInteractions'
            );
        }

        // Resource Type Groups
        if (result.resourceTypeGroups.length > 0) {
            html += this.generateSection('📁 GroupBy {ResourceType → OperationType}',
                '<p class="note">Click on a row to see related entries with JSON</p>' +
                this.generateGroupedTable('Resource Type Groups', result.resourceTypeGroups, 'resourceType') +
                this.generateGroupDetailsSections(result.resourceTypeGroups, 'resourceType')
            );
        }

        // Status Code Groups
        if (result.statusCodeGroups.length > 0) {
            html += this.generateSection('🔢 GroupBy {StatusCode → SubStatusCode}',
                '<p class="note">Click on a row to see related entries with JSON</p>' +
                this.generateGroupedTable('Status Code Groups', result.statusCodeGroups, 'statusCode') +
                this.generateGroupDetailsSections(result.statusCodeGroups, 'statusCode')
            );
        }

        // Transport Event Groups
        if (result.transportEventGroups.length > 0) {
            html += this.generateSection('🚀 GroupBy LastTransportEvent',
                '<p class="note">Click on an event to see related entries with JSON</p>' +
                this.generateTransportEventGroups(result.transportEventGroups)
            );
        }

        // JSON Modal
        html += this.generateJsonModal();

        return html;
    }

    /**
     * Generate a section container
     */
    generateSection(title, content) {
        return `
            <div class="section">
                <h2>${title}</h2>
                ${content}
            </div>
        `;
    }

    /**
     * Generate collapsible section
     */
    generateCollapsibleSection(title, subtitle, content, id) {
        return `
            <div class="section">
                <div class="section-header collapsible" onclick="toggleSection('${id}')">
                    <h2>${title} (${subtitle})</h2>
                    <span class="collapse-icon" id="${id}-icon">▶</span>
                </div>
                <div id="${id}" class="section-content" style="display:none;">
                    ${content}
                </div>
            </div>
        `;
    }

    /**
     * Generate summary table
     */
    generateSummaryTable(result) {
        return this.generateTable('Parsing Statistics', [
            { metric: 'Total Entries', value: result.totalEntries },
            { metric: 'Parsed Entries', value: result.parsedEntries },
            { metric: 'Repaired Entries', value: result.repairedEntries },
            { metric: 'High Latency Entries', value: result.highLatencyEntries }
        ], ['metric', 'value']);
    }

    /**
     * Generate operation buckets table
     */
    generateOperationBucketsTable(buckets) {
        let html = `
            <div class="dump-container">
                <div class="dump-header">OperationName Buckets</div>
                <table class="dump-table">
                    <thead>
                        <tr>
                            <th class="row-num">#</th>
                            <th>Bucket</th>
                            <th>Min</th>
                            <th>Max</th>
                            <th>Min NW Count</th>
                            <th>Max NW Count</th>
                            <th>Count</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        buckets.forEach((bucket, index) => {
            const bucketId = this.getSafeId(bucket.bucket);
            html += `
                <tr class="${index % 2 === 0 ? 'even' : 'odd'}">
                    <td class="row-num">${index + 1}</td>
                    <td><a href="#" class="bucket-link" onclick="showBucket('${bucketId}'); return false;">${this.escapeHtml(bucket.bucket)}</a></td>
                    <td><span class="number">${bucket.min.toFixed(2)}</span></td>
                    <td><span class="number">${bucket.max.toFixed(2)}</span></td>
                    <td><span class="number">${bucket.minNWCount.toLocaleString()}</span></td>
                    <td><span class="number">${bucket.maxNWCount.toLocaleString()}</span></td>
                    <td><span class="number">${bucket.count.toLocaleString()}</span></td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';
        return html;
    }

    /**
     * Generate bucket details sections
     */
    generateBucketDetailsSections(result) {
        let html = '';

        for (const bucket of result.operationBuckets) {
            const bucketEntries = result.allHighLatencyDiagnostics
                .filter(e => e.name === bucket.bucket)
                .slice(0, 50);

            const bucketId = this.getSafeId(bucket.bucket);
            html += `
                <div id="bucket-${bucketId}" class="section bucket-details" style="display:none;">
                    <h2>📋 Entries for: ${this.escapeHtml(bucket.bucket)}</h2>
                    <p class="note">Click on column headers to sort</p>
                    <button class="btn-close" onclick="document.getElementById('bucket-${bucketId}').style.display='none'">✕ Close</button>
                    ${this.generateEntriesTable(`Showing ${bucketEntries.length} of ${bucket.count} entries`, bucketEntries, `bucket-table-${bucketId}`)}
                </div>
            `;
        }

        return html;
    }

    /**
     * Generate entries table with JSON column
     */
    generateEntriesTable(title, entries, tableId) {
        let html = `
            <div class="dump-container">
                <div class="dump-header">${title}</div>
                <table class="dump-table sortable" id="${tableId}">
                    <thead>
                        <tr>
                            <th class="row-num">#</th>
                            <th class="sortable" data-col="1" onclick="sortTable('${tableId}', 1)">Name <span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="2" onclick="sortTable('${tableId}', 2)">Start Time <span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="3" onclick="sortTable('${tableId}', 3)">Duration <span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="4" onclick="sortTable('${tableId}', 4)">Direct Calls <span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="5" onclick="sortTable('${tableId}', 5)">Gateway Calls <span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="6" onclick="sortTable('${tableId}', 6)">Total Calls <span class="sort-icon">⇅</span></th>
                            <th>JSON</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        entries.forEach((entry, index) => {
            const jsonId = `json-${++this.jsonModalId}`;
            html += `
                <tr class="${index % 2 === 0 ? 'even' : 'odd'}">
                    <td class="row-num">${index + 1}</td>
                    <td data-sort="${this.escapeHtml(entry.name || '')}"><span class="string">${this.escapeHtml(entry.name)}</span></td>
                    <td data-sort="${this.escapeHtml(entry.startTime || '')}"><span class="string">${this.escapeHtml(entry.startTime)}</span></td>
                    <td data-sort="${entry.duration}"><span class="number">${entry.duration.toFixed(2)}</span></td>
                    <td data-sort="${entry.directCallCount}"><span class="number">${entry.directCallCount.toLocaleString()}</span></td>
                    <td data-sort="${entry.gatewayCallCount}"><span class="number">${entry.gatewayCallCount.toLocaleString()}</span></td>
                    <td data-sort="${entry.totalCallCount}"><span class="number">${entry.totalCallCount.toLocaleString()}</span></td>
                    <td>
                        <button class="btn-json" onclick="showJson('${jsonId}')">📄 View JSON (${(entry.rawJson?.length || 0).toLocaleString()} chars)</button>
                        <div id="${jsonId}" class="json-content" style="display:none;">${this.escapeHtml(entry.rawJson || '')}</div>
                    </td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';
        return html;
    }

    /**
     * Generate grouped table
     */
    generateGroupedTable(title, groups, prefix) {
        let html = `
            <div class="dump-container">
                <div class="dump-header">${title}</div>
                <table class="dump-table">
                    <thead>
                        <tr>
                            <th class="row-num">#</th>
                            <th>Key</th>
                            <th>Count</th>
                            <th>Action</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        groups.forEach((group, index) => {
            const groupId = this.getSafeId(`${prefix}-${group.key}`);
            html += `
                <tr class="${index % 2 === 0 ? 'even' : 'odd'} clickable-row" onclick="showGroup('${groupId}')">
                    <td class="row-num">${index + 1}</td>
                    <td><span class="string">${this.escapeHtml(group.key)}</span></td>
                    <td><span class="number">${group.count.toLocaleString()}</span></td>
                    <td><button class="btn-view" onclick="event.stopPropagation(); showGroup('${groupId}')">📄 View Entries</button></td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';
        return html;
    }

    /**
     * Generate group details sections
     */
    generateGroupDetailsSections(groups, prefix) {
        let html = '';

        for (const group of groups) {
            const groupId = this.getSafeId(`${prefix}-${group.key}`);
            html += `
                <div id="group-${groupId}" class="section bucket-details" style="display:none;">
                    <h2>📋 Entries for: ${this.escapeHtml(group.key)}</h2>
                    <button class="btn-close" onclick="document.getElementById('group-${groupId}').style.display='none'">✕ Close</button>
                    ${this.generateGroupEntriesTable(`Showing ${group.entries.length} of ${group.count} entries`, group.entries, `group-table-${groupId}`)}
                </div>
            `;
        }

        return html;
    }

    /**
     * Generate group entries table
     */
    generateGroupEntriesTable(title, entries, tableId) {
        let html = `
            <div class="dump-container">
                <div class="dump-header">${title}</div>
                <table class="dump-table sortable" id="${tableId}">
                    <thead>
                        <tr>
                            <th class="row-num">#</th>
                            <th class="sortable" data-col="1" onclick="sortTable('${tableId}', 1)">Duration <span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="2" onclick="sortTable('${tableId}', 2)">Status Code <span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="3" onclick="sortTable('${tableId}', 3)">Sub Status <span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="4" onclick="sortTable('${tableId}', 4)">Resource Type <span class="sort-icon">⇅</span></th>
                            <th class="sortable" data-col="5" onclick="sortTable('${tableId}', 5)">Operation Type <span class="sort-icon">⇅</span></th>
                            <th>JSON</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        entries.forEach((entry, index) => {
            const jsonId = `json-${++this.jsonModalId}`;
            html += `
                <tr class="${index % 2 === 0 ? 'even' : 'odd'}">
                    <td class="row-num">${index + 1}</td>
                    <td data-sort="${entry.durationInMs}"><span class="number">${entry.durationInMs.toFixed(2)}</span></td>
                    <td data-sort="${this.escapeHtml(entry.statusCode || '')}"><span class="string">${this.escapeHtml(entry.statusCode)}</span></td>
                    <td data-sort="${this.escapeHtml(entry.subStatusCode || '')}"><span class="string">${this.escapeHtml(entry.subStatusCode)}</span></td>
                    <td data-sort="${this.escapeHtml(entry.resourceType || '')}"><span class="string">${this.escapeHtml(entry.resourceType)}</span></td>
                    <td data-sort="${this.escapeHtml(entry.operationType || '')}"><span class="string">${this.escapeHtml(entry.operationType)}</span></td>
                    <td>
                        <button class="btn-json" onclick="showJson('${jsonId}')">📄 View JSON</button>
                        <div id="${jsonId}" class="json-content" style="display:none;">${this.escapeHtml(entry.rawJson || '')}</div>
                    </td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';
        return html;
    }

    /**
     * Generate transport event groups
     */
    generateTransportEventGroups(groups) {
        let html = '';

        for (const group of groups) {
            const groupId = this.getSafeId(`transport-${group.status}`);
            html += `
                <div class="subsection">
                    <h3 class="clickable-header" onclick="showGroup('${groupId}')">${group.status} (${group.count} items) <span class="click-hint">👆 click to view entries</span></h3>
                    ${this.generatePhaseDetailsTable(group.phaseDetails, group.status)}
                </div>
            `;

            // Phase detail sections
            for (const phase of group.phaseDetails.filter(p => p.entries.length > 0)) {
                const phaseId = this.getSafeId(`phase-${group.status}-${phase.phase}`);
                html += `
                    <div id="group-${phaseId}" class="section bucket-details" style="display:none;">
                        <h2>📋 Entries for Phase: ${this.escapeHtml(phase.phase)}</h2>
                        <button class="btn-close" onclick="document.getElementById('group-${phaseId}').style.display='none'">✕ Close</button>
                        ${this.generateGroupEntriesTable(`Showing ${phase.entries.length} of ${phase.count} entries`, phase.entries, `phase-table-${phaseId}`)}
                    </div>
                `;
            }
        }

        // Transport event detail sections
        for (const group of groups) {
            const groupId = this.getSafeId(`transport-${group.status}`);
            html += `
                <div id="group-${groupId}" class="section bucket-details" style="display:none;">
                    <h2>📋 Entries for: ${group.status}</h2>
                    <button class="btn-close" onclick="document.getElementById('group-${groupId}').style.display='none'">✕ Close</button>
                    ${this.generateGroupEntriesTable(`Showing ${group.entries.length} of ${group.count} entries`, group.entries, `group-table-${groupId}`)}
                </div>
            `;
        }

        return html;
    }

    /**
     * Generate phase details table
     */
    generatePhaseDetailsTable(phases, transportEvent) {
        if (!phases || phases.length === 0) return '';

        let html = `
            <div class="dump-container">
                <div class="dump-header">Phase Details</div>
                <table class="dump-table">
                    <thead>
                        <tr>
                            <th class="row-num">#</th>
                            <th>Phase</th>
                            <th>Count</th>
                            <th>Min Duration</th>
                            <th>Max Duration</th>
                            <th>Endpoint Count</th>
                            <th>Action</th>
                        </tr>
                    </thead>
                    <tbody>
        `;

        phases.forEach((phase, index) => {
            const phaseId = this.getSafeId(`phase-${transportEvent}-${phase.phase}`);
            html += `
                <tr class="${index % 2 === 0 ? 'even' : 'odd'} clickable-row" onclick="showGroup('${phaseId}')">
                    <td class="row-num">${index + 1}</td>
                    <td><span class="string">${this.escapeHtml(phase.phase)}</span></td>
                    <td><span class="number">${phase.count.toLocaleString()}</span></td>
                    <td><span class="number">${phase.minDuration.toFixed(2)}</span></td>
                    <td><span class="number">${phase.maxDuration.toFixed(2)}</span></td>
                    <td><span class="number">${phase.endpointCount.toLocaleString()}</span></td>
                    <td><button class="btn-view" onclick="event.stopPropagation(); showGroup('${phaseId}')">📄 View Entries</button></td>
                </tr>
            `;
        });

        html += '</tbody></table></div>';

        // Top endpoints details
        for (const phase of phases.filter(p => p.top10Endpoints.length > 0)) {
            html += `
                <details>
                    <summary>Top Endpoints for ${this.escapeHtml(phase.phase || 'Unknown')}</summary>
                    ${this.generateTable('Endpoints', phase.top10Endpoints, ['endpoint', 'count'])}
                </details>
            `;
        }

        return html;
    }

    /**
     * Generate generic table
     */
    generateTable(title, items, columns) {
        if (!items || items.length === 0) {
            return `<p class="empty">No data for ${title}</p>`;
        }

        let html = `
            <div class="dump-container">
                <div class="dump-header">${title}</div>
                <table class="dump-table">
                    <thead>
                        <tr>
                            <th class="row-num">#</th>
                            ${columns.map(col => `<th>${this.formatColumnName(col)}</th>`).join('')}
                        </tr>
                    </thead>
                    <tbody>
        `;

        items.forEach((item, index) => {
            html += `<tr class="${index % 2 === 0 ? 'even' : 'odd'}">`;
            html += `<td class="row-num">${index + 1}</td>`;
            columns.forEach(col => {
                const value = item[col];
                html += `<td>${this.formatValue(value)}</td>`;
            });
            html += '</tr>';
        });

        html += '</tbody></table></div>';
        return html;
    }

    /**
     * Generate JSON modal
     */
    generateJsonModal() {
        return `
            <div id="jsonModal" class="modal" onclick="closeJsonModal(event)">
                <div class="modal-content" onclick="event.stopPropagation()">
                    <div class="modal-header">
                        <h3>📄 JSON Content</h3>
                        <button class="modal-close" onclick="closeJsonModal()">&times;</button>
                    </div>
                    <div class="modal-actions">
                        <button class="btn-copy" onclick="copyJsonContent()">📋 Copy to Clipboard</button>
                        <button class="btn-format" onclick="formatJson()">🔧 Format JSON</button>
                    </div>
                    <pre id="jsonModalContent" class="json-display"></pre>
                </div>
            </div>
        `;
    }

    /**
     * Format column name
     */
    formatColumnName(name) {
        return name.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase()).trim();
    }

    /**
     * Format value for display
     */
    formatValue(value) {
        if (value === null || value === undefined) {
            return '<span class="null">null</span>';
        }
        if (typeof value === 'number') {
            return `<span class="number">${typeof value === 'number' && !Number.isInteger(value) ? value.toFixed(2) : value.toLocaleString()}</span>`;
        }
        if (typeof value === 'string') {
            return `<span class="string">${this.escapeHtml(value)}</span>`;
        }
        return this.escapeHtml(String(value));
    }

    /**
     * Escape HTML
     */
    escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    /**
     * Generate safe ID from string
     */
    getSafeId(name) {
        if (!name) return 'unknown';
        return btoa(encodeURIComponent(name)).replace(/[+/=]/g, '_');
    }
}

// Export for use
window.HtmlGenerator = HtmlGenerator;
