// Timeline visualization for JSON Modal
// Chrome DevTools-style network waterfall (Gantt chart)

const Timeline = (function() {
    const PHASE_COLORS = {
        'Created': '#4CAF50',
        'ChannelAcquisitionStarted': '#2196F3',
        'Pipelined': '#FF9800',
        'Transit Time': '#9C27B0',
        'Received': '#00BCD4',
        'Completed': '#607D8B'
    };

    const PHASE_SHORT_NAMES = {
        'Created': 'Created',
        'ChannelAcquisitionStarted': 'Channel',
        'Pipelined': 'Pipelined',
        'Transit Time': 'Transit',
        'Received': 'Received',
        'Completed': 'Completed'
    };

    let timelineData = [];
    let timelineZoom = 1;
    let minTime = 0, maxTime = 0, totalDuration = 0;
    let isVisible = false;

    // Extract timeline data from JSON
    function extractTimelineData(jsonStr) {
        try {
            const data = JSON.parse(jsonStr);
            const requests = [];

            function findStoreResults(obj) {
                if (!obj || typeof obj !== 'object') return;
                if (Array.isArray(obj)) {
                    obj.forEach(findStoreResults);
                    return;
                }

                const clientStats = obj['Client Side Request Stats'] || obj.ClientSideRequestStats;
                if (clientStats) {
                    const storeStats = clientStats.StoreResponseStatistics || clientStats['Store Response Statistics'];
                    if (storeStats && Array.isArray(storeStats)) {
                        storeStats.forEach(stat => {
                            const storeResult = stat.StoreResult || stat['Store Result'];
                            if (storeResult) {
                                const timeline = storeResult.transportRequestTimeline || storeResult.TransportRequestTimeline;
                                const requestTimeline = timeline?.requestTimeline || timeline?.RequestTimeline;

                                if (requestTimeline && Array.isArray(requestTimeline)) {
                                    const phases = requestTimeline.map(phase => ({
                                        name: phase.event || phase.Event || 'Unknown',
                                        startTime: new Date(phase.startTimeUtc || phase.StartTimeUtc).getTime(),
                                        duration: phase.durationInMs || phase.DurationInMs || 0
                                    })).filter(p => !isNaN(p.startTime));

                                    if (phases.length > 0) {
                                        requests.push({
                                            statusCode: storeResult.StatusCode || 'Unknown',
                                            endpoint: truncateEndpoint(storeResult.StorePhysicalAddress || ''),
                                            fullEndpoint: storeResult.StorePhysicalAddress || '',
                                            duration: stat.DurationInMs || 0,
                                            beLatency: storeResult.BELatencyInMs || '',
                                            startTime: Math.min(...phases.map(p => p.startTime)),
                                            phases
                                        });
                                    }
                                }
                            }
                        });
                    }
                }

                for (const key in obj) {
                    if (obj.hasOwnProperty(key)) {
                        findStoreResults(obj[key]);
                    }
                }
            }

            findStoreResults(data);
            requests.sort((a, b) => a.startTime - b.startTime);
            return requests;
        } catch (e) {
            console.error('Timeline parse error:', e);
            return [];
        }
    }

    function truncateEndpoint(endpoint) {
        if (!endpoint) return '';
        try {
            const path = new URL(endpoint).pathname;
            return path.length > 28 ? '...' + path.slice(-25) : path;
        } catch {
            return endpoint.length > 28 ? '...' + endpoint.slice(-25) : endpoint;
        }
    }

    function calculateTimeRange() {
        if (timelineData.length === 0) return;
        minTime = Math.min(...timelineData.map(r => r.startTime));
        maxTime = Math.max(...timelineData.map(r => {
            const lastPhase = r.phases[r.phases.length - 1];
            return lastPhase.startTime + lastPhase.duration;
        }));
        totalDuration = maxTime - minTime;
    }

    function formatMs(ms) {
        if (ms >= 1000) return (ms / 1000).toFixed(2) + 's';
        return ms.toFixed(1) + 'ms';
    }

    function escapeHtml(str) {
        if (!str) return '';
        return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function renderLegend() {
        return Object.entries(PHASE_COLORS).map(([name, color]) =>
            `<div class="timeline-legend-item">
                <div class="timeline-legend-color" style="background:${color}"></div>
                <span>${PHASE_SHORT_NAMES[name] || name}</span>
            </div>`
        ).join('');
    }

    function renderAxis() {
        let html = '';
        for (let i = 0; i <= 8; i++) {
            html += `<span>${formatMs(i * totalDuration / 8)}</span>`;
        }
        return html;
    }

    function renderRows() {
        let html = '';
        timelineData.forEach((req, idx) => {
            const barStart = ((req.startTime - minTime) / totalDuration) * 100 * timelineZoom;
            const barWidth = (req.duration / totalDuration) * 100 * timelineZoom;

            let phasesHtml = '';
            req.phases.forEach(phase => {
                const width = (phase.duration / req.duration) * 100;
                const color = PHASE_COLORS[phase.name] || '#666';
                phasesHtml += `<div class="timeline-phase" style="width:${Math.max(width, 1)}%;background:${color}"></div>`;
            });

            let statusClass = 'timeline-status-ok';
            const sc = req.statusCode.toLowerCase();
            if (sc.includes('error') || sc === '500' || sc === '503') {
                statusClass = 'timeline-status-error';
            } else if (sc === '429' || sc.includes('retry')) {
                statusClass = 'timeline-status-retry';
            }

            html += `<div class="timeline-row" data-idx="${idx}">
                <div class="timeline-label">
                    <span class="timeline-status ${statusClass}">${escapeHtml(req.statusCode)}</span>
                    <span title="${escapeHtml(req.fullEndpoint)}">${escapeHtml(req.endpoint)}</span>
                </div>
                <div class="timeline-bar-container">
                    <div class="timeline-bar" style="left:${barStart}%;width:${Math.max(barWidth, 0.5)}%;"
                         onmouseenter="Timeline.showTooltip(event, ${idx})"
                         onmouseleave="Timeline.hideTooltip()">
                        ${phasesHtml}
                    </div>
                    <span class="timeline-duration">${formatMs(req.duration)}</span>
                </div>
            </div>`;
        });
        return html;
    }

    function render() {
        const container = document.getElementById('timelineContainer');
        if (!container) return;

        if (timelineData.length === 0) {
            container.innerHTML = `
                <div class="timeline-header">
                    <h4 class="timeline-title">Transport Request Timeline</h4>
                </div>
                <div class="timeline-no-data">No transport timeline data found in this JSON</div>`;
            return;
        }

        const avg = timelineData.reduce((s, r) => s + r.duration, 0) / timelineData.length;

        container.innerHTML = `
            <div class="timeline-header">
                <div>
                    <h4 class="timeline-title">Transport Request Timeline</h4>
                    <div class="timeline-stats">${timelineData.length} request(s) • Span: ${formatMs(totalDuration)} • Avg: ${formatMs(avg)}</div>
                </div>
                <div class="timeline-controls">
                    <button class="timeline-zoom-btn" onclick="Timeline.zoom(-1)">➖</button>
                    <button class="timeline-zoom-btn" onclick="Timeline.zoom(1)">➕</button>
                    <button class="timeline-zoom-btn" onclick="Timeline.resetZoom()">⟲</button>
                </div>
            </div>
            <div class="timeline-legend">${renderLegend()}</div>
            <div class="timeline-chart">
                <div class="timeline-axis">${renderAxis()}</div>
                <div class="timeline-rows">${renderRows()}</div>
            </div>`;
    }

    // Public API
    return {
        init(jsonStr) {
            timelineData = extractTimelineData(jsonStr);
            timelineZoom = 1;
            if (timelineData.length > 0) {
                calculateTimeRange();
            }
            return timelineData.length > 0;
        },

        toggle() {
            const container = document.getElementById('timelineContainer');
            const btn = document.getElementById('timelineBtn');
            const jsonContent = document.getElementById('jsonContent');
            if (!container || !btn) return;

            isVisible = !isVisible;
            if (isVisible) {
                render();
                container.classList.add('visible');
                btn.textContent = '📄 Show JSON';
                if (jsonContent) jsonContent.style.display = 'none';
            } else {
                container.classList.remove('visible');
                btn.textContent = '📊 Show Timeline';
                if (jsonContent) jsonContent.style.display = 'block';
            }
        },

        hide() {
            const container = document.getElementById('timelineContainer');
            const btn = document.getElementById('timelineBtn');
            const jsonContent = document.getElementById('jsonContent');
            if (container) container.classList.remove('visible');
            if (btn) btn.textContent = '📊 Show Timeline';
            if (jsonContent) jsonContent.style.display = 'block';
            isVisible = false;
            timelineZoom = 1;
        },

        zoom(direction) {
            timelineZoom = Math.max(0.5, Math.min(10, direction > 0 ? timelineZoom * 1.5 : timelineZoom / 1.5));
            render();
        },

        resetZoom() {
            timelineZoom = 1;
            render();
        },

        showTooltip(event, idx) {
            const req = timelineData[idx];
            if (!req) return;

            const tooltip = document.getElementById('timelineTooltip');
            if (!tooltip) return;

            let phasesHtml = '';
            req.phases.forEach(phase => {
                const color = PHASE_COLORS[phase.name] || '#666';
                phasesHtml += `<div class="timeline-tooltip-phase">
                    <div class="timeline-tooltip-phase-color" style="background:${color}"></div>
                    <span>${phase.name}</span>
                    <span class="timeline-tooltip-value">${formatMs(phase.duration)}</span>
                </div>`;
            });

            tooltip.innerHTML = `
                <div class="timeline-tooltip-header">${escapeHtml(req.statusCode)} Request</div>
                <div class="timeline-tooltip-row">
                    <span>Duration:</span>
                    <span class="timeline-tooltip-value">${formatMs(req.duration)}</span>
                </div>
                ${req.beLatency ? `<div class="timeline-tooltip-row">
                    <span>BE Latency:</span>
                    <span class="timeline-tooltip-value">${req.beLatency}ms</span>
                </div>` : ''}
                <div class="timeline-tooltip-phases">
                    <div class="timeline-tooltip-phases-title">Phases:</div>
                    ${phasesHtml}
                </div>`;

            tooltip.classList.add('visible');

            // Position tooltip
            let left = event.clientX + 15;
            let top = event.clientY - 10;
            if (left + 320 > window.innerWidth) left = event.clientX - 330;
            if (top + tooltip.offsetHeight > window.innerHeight) {
                top = window.innerHeight - tooltip.offsetHeight - 10;
            }
            tooltip.style.left = left + 'px';
            tooltip.style.top = top + 'px';
        },

        hideTooltip() {
            const tooltip = document.getElementById('timelineTooltip');
            if (tooltip) tooltip.classList.remove('visible');
        },

        hasData() {
            return timelineData.length > 0;
        }
    };
})();
