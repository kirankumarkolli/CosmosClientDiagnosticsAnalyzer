/**
 * Main Application Module
 * Handles UI interactions, file processing, and export
 */

const app = {
    // State
    selectedFile: null,
    currentResult: null,
    currentJsonContent: '',

    // DOM elements (initialized on load)
    elements: {},

    /**
     * Initialize the application
     */
    init() {
        this.elements = {
            dropArea: document.getElementById('dropArea'),
            fileInput: document.getElementById('fileInput'),
            fileInfo: document.getElementById('fileInfo'),
            analyzeBtn: document.getElementById('analyzeBtn'),
            latencyThreshold: document.getElementById('latencyThreshold'),
            uploadSection: document.getElementById('upload-section'),
            progressSection: document.getElementById('progress-section'),
            progressFill: document.getElementById('progressFill'),
            progressText: document.getElementById('progressText'),
            resultsSection: document.getElementById('results-section'),
            resultsContainer: document.getElementById('results-container'),
            errorSection: document.getElementById('error-section'),
            errorMessage: document.getElementById('error-message'),
            downloadBtn: document.getElementById('downloadBtn'),
            newAnalysisBtn: document.getElementById('newAnalysisBtn'),
            retryBtn: document.getElementById('retryBtn'),
            jsonModal: document.getElementById('jsonModal'),
            jsonContent: document.getElementById('jsonContent'),
            modalClose: document.getElementById('modalClose'),
            copyJsonBtn: document.getElementById('copyJsonBtn'),
            formatJsonBtn: document.getElementById('formatJsonBtn'),
            versionInfo: document.getElementById('versionInfo')
        };

        this.setupEventListeners();
        this.displayVersion();
    },

    /**
     * Display version info
     */
    displayVersion() {
        if (window.VERSION && this.elements.versionInfo) {
            this.elements.versionInfo.textContent = `Version: ${VERSION.commit} (${VERSION.date})`;
        }
    },

    /**
     * Setup all event listeners
     */
    setupEventListeners() {
        const { dropArea, fileInput, analyzeBtn, downloadBtn, newAnalysisBtn, retryBtn,
                modalClose, copyJsonBtn, formatJsonBtn, jsonModal } = this.elements;

        // File input
        dropArea.addEventListener('click', () => fileInput.click());
        dropArea.addEventListener('dragover', e => this.handleDragOver(e));
        dropArea.addEventListener('dragleave', e => this.handleDragLeave(e));
        dropArea.addEventListener('drop', e => this.handleDrop(e));
        fileInput.addEventListener('change', e => this.handleFileSelect(e));

        // Buttons
        analyzeBtn.addEventListener('click', () => this.analyze());
        downloadBtn.addEventListener('click', () => this.downloadHtml());
        newAnalysisBtn.addEventListener('click', () => this.reset());
        retryBtn.addEventListener('click', () => this.reset());

        // Modal
        modalClose.addEventListener('click', () => this.closeModal());
        copyJsonBtn.addEventListener('click', () => this.copyJson());
        formatJsonBtn.addEventListener('click', () => this.formatJson());
        jsonModal.addEventListener('click', e => {
            if (e.target === jsonModal) this.closeModal();
        });

        // Keyboard
        document.addEventListener('keydown', e => {
            if (e.key === 'Escape') this.closeModal();
        });

        // Table sorting
        document.addEventListener('click', e => {
            const th = e.target.closest('th.sortable');
            if (th) {
                const table = th.closest('table');
                const col = parseInt(th.dataset.col);
                if (table && !isNaN(col)) {
                    this.sortTable(table, col, th);
                }
            }
        });
    },

    /**
     * Handle drag over
     */
    handleDragOver(e) {
        e.preventDefault();
        this.elements.dropArea.classList.add('dragover');
    },

    /**
     * Handle drag leave
     */
    handleDragLeave(e) {
        e.preventDefault();
        this.elements.dropArea.classList.remove('dragover');
    },

    /**
     * Handle file drop
     */
    handleDrop(e) {
        e.preventDefault();
        this.elements.dropArea.classList.remove('dragover');
        const files = e.dataTransfer.files;
        if (files.length > 0) {
            this.selectFile(files[0]);
        }
    },

    /**
     * Handle file selection
     */
    handleFileSelect(e) {
        if (e.target.files.length > 0) {
            this.selectFile(e.target.files[0]);
        }
    },

    /**
     * Select and display file info
     */
    selectFile(file) {
        this.selectedFile = file;
        this.elements.fileInfo.textContent = `📄 ${file.name} (${this.formatSize(file.size)})`;
        this.elements.fileInfo.classList.add('visible');
        this.elements.analyzeBtn.disabled = false;
    },

    /**
     * Format file size
     */
    formatSize(bytes) {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    },

    /**
     * Main analysis function
     */
    async analyze() {
        if (!this.selectedFile) return;

        const { analyzeBtn } = this.elements;
        const btnText = analyzeBtn.querySelector('.btn-text');
        const btnLoading = analyzeBtn.querySelector('.btn-loading');

        // Show loading
        btnText.hidden = true;
        btnLoading.hidden = false;
        analyzeBtn.disabled = true;
        this.showSection('progress');
        this.updateProgress('Reading file...', 5);

        try {
            // Read file
            const content = await this.readFile(this.selectedFile);
            this.updateProgress('Parsing JSON lines...', 10);

            // Allow UI to update
            await this.sleep(50);

            // Parse
            const parser = new JsonParser();
            const diagnostics = parser.parseLines(content, (msg, pct) => {
                this.updateProgress(msg, pct);
            });

            const stats = parser.getStats();
            this.updateProgress(`Parsed ${diagnostics.length} entries (${stats.repaired} repaired)`, 42);
            await this.sleep(50);

            // Analyze
            const threshold = parseInt(this.elements.latencyThreshold.value) || 600;
            const analyzer = new Analyzer();
            this.currentResult = analyzer.analyze(diagnostics, threshold, (msg, pct) => {
                this.updateProgress(msg, pct);
            });

            // Add parser stats to result
            this.currentResult.parsedEntries = diagnostics.length;
            this.currentResult.repairedEntries = stats.repaired;
            this.currentResult.failedEntries = stats.failed;

            // Generate report
            this.updateProgress('Generating report...', 95);
            await this.sleep(50);

            const generator = new ReportGenerator();
            const html = generator.generate(this.currentResult);

            // Display
            this.elements.resultsContainer.innerHTML = html;
            this.initializeCharts();
            this.showSection('results');

        } catch (error) {
            console.error('Analysis error:', error);
            this.showError(error.message || 'An error occurred during analysis');
        } finally {
            btnText.hidden = false;
            btnLoading.hidden = true;
            analyzeBtn.disabled = false;
        }
    },

    /**
     * Read file as text
     */
    readFile(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = e => resolve(e.target.result);
            reader.onerror = () => reject(new Error('Failed to read file'));
            reader.readAsText(file);
        });
    },

    /**
     * Sleep helper
     */
    sleep(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    },

    /**
     * Update progress display
     */
    updateProgress(text, percent) {
        this.elements.progressText.textContent = text;
        this.elements.progressFill.style.width = percent + '%';
    },

    /**
     * Show specific section
     */
    showSection(section) {
        const { uploadSection, progressSection, resultsSection, errorSection } = this.elements;
        uploadSection.hidden = section !== 'upload';
        progressSection.hidden = section !== 'progress';
        resultsSection.hidden = section !== 'results';
        errorSection.hidden = section !== 'error';
    },

    /**
     * Show error
     */
    showError(message) {
        this.elements.errorMessage.textContent = message;
        this.showSection('error');
    },

    /**
     * Reset UI
     */
    reset() {
        this.selectedFile = null;
        this.currentResult = null;
        this.elements.fileInput.value = '';
        this.elements.fileInfo.textContent = '';
        this.elements.fileInfo.classList.remove('visible');
        this.elements.analyzeBtn.disabled = true;
        this.elements.progressFill.style.width = '0%';
        this.elements.resultsContainer.innerHTML = '';
        this.destroyCharts();
        this.showSection('upload');
    },

    /**
     * Store chart instances for cleanup
     */
    chartInstances: [],

    /**
     * Initialize Chart.js charts after rendering
     */
    initializeCharts() {
        this.destroyCharts();
        
        // System Metrics Chart
        const systemCanvas = document.getElementById('systemMetricsChart');
        if (systemCanvas && window.Chart) {
            const dataEl = document.getElementById('systemMetricsChart-data');
            if (dataEl) {
                try {
                    const chartData = JSON.parse(dataEl.textContent);
                    const chart = new Chart(systemCanvas.getContext('2d'), {
                        type: 'line',
                        data: chartData,
                        options: this.getChartOptions('System Metrics', [
                            { id: 'y', title: 'CPU (%)', position: 'left' },
                            { id: 'y1', title: 'Memory (MB)', position: 'right' },
                            { id: 'y2', title: 'Thread Wait (ms)', position: 'right', display: false },
                            { id: 'y3', title: 'TCP Connections', position: 'right', display: false }
                        ])
                    });
                    this.chartInstances.push(chart);
                } catch (e) {
                    console.error('Error creating system metrics chart:', e);
                }
            }
        }

        // Client Config Chart
        const configCanvas = document.getElementById('clientConfigChart');
        if (configCanvas && window.Chart) {
            const dataEl = document.getElementById('clientConfigChart-data');
            if (dataEl) {
                try {
                    const chartData = JSON.parse(dataEl.textContent);
                    const chart = new Chart(configCanvas.getContext('2d'), {
                        type: 'line',
                        data: chartData,
                        options: this.getChartOptions('Client Configuration', [
                            { id: 'y', title: 'Processor Count', position: 'left' },
                            { id: 'y1', title: 'Client Count', position: 'right' }
                        ])
                    });
                    this.chartInstances.push(chart);
                } catch (e) {
                    console.error('Error creating client config chart:', e);
                }
            }
        }
    },

    /**
     * Get chart options with multiple Y axes
     */
    getChartOptions(title, yAxes) {
        const scales = {
            x: {
                type: 'category',
                display: true,
                title: { display: true, text: 'Time', color: '#d4d4d4' },
                ticks: { color: '#d4d4d4', maxRotation: 45, maxTicksLimit: 20 },
                grid: { color: 'rgba(255,255,255,0.1)' }
            }
        };

        yAxes.forEach((axis, i) => {
            scales[axis.id] = {
                type: 'linear',
                display: axis.display !== false,
                position: axis.position,
                title: { display: true, text: axis.title, color: '#d4d4d4' },
                ticks: { color: '#d4d4d4' },
                grid: { color: i === 0 ? 'rgba(255,255,255,0.1)' : 'transparent' }
            };
        });

        return {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: 'index', intersect: false },
            plugins: {
                legend: {
                    labels: { color: '#d4d4d4' },
                    onClick: (e, legendItem, legend) => {
                        const index = legendItem.datasetIndex;
                        const chart = legend.chart;
                        const meta = chart.getDatasetMeta(index);
                        meta.hidden = meta.hidden === null ? !chart.data.datasets[index].hidden : null;
                        chart.update();
                    }
                },
                title: { display: false }
            },
            scales: scales
        };
    },

    /**
     * Destroy existing chart instances
     */
    destroyCharts() {
        if (this.chartInstances) {
            this.chartInstances.forEach(chart => {
                if (chart && typeof chart.destroy === 'function') {
                    chart.destroy();
                }
            });
        }
        this.chartInstances = [];
    },

    /**
     * Toggle collapsible section
     */
    toggleSection(sectionId) {
        const content = document.getElementById(sectionId);
        const icon = document.getElementById(sectionId + '-icon');
        if (!content) return;

        const isVisible = content.classList.contains('visible');
        content.classList.toggle('visible');
        if (icon) {
            icon.textContent = isVisible ? '▶' : '▼';
        }
    },

    /**
     * Show bucket details
     */
    showBucket(bucketId) {
        // Hide all detail sections
        document.querySelectorAll('.detail-section').forEach(el => {
            el.classList.remove('visible');
        });

        const el = document.getElementById('bucket-' + bucketId);
        if (el) {
            el.classList.add('visible');
            el.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    },

    /**
     * Close bucket details
     */
    closeBucket(bucketId) {
        const el = document.getElementById('bucket-' + bucketId);
        if (el) {
            el.classList.remove('visible');
        }
    },

    /**
     * Show group details
     */
    showGroup(groupId) {
        document.querySelectorAll('.detail-section').forEach(el => {
            el.classList.remove('visible');
        });

        const el = document.getElementById('group-' + groupId);
        if (el) {
            el.classList.add('visible');
            el.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    },

    /**
     * Close group details
     */
    closeGroup(groupId) {
        const el = document.getElementById('group-' + groupId);
        if (el) {
            el.classList.remove('visible');
        }
    },

    /**
     * Show JSON in modal
     */
    showJson(jsonId) {
        const el = document.getElementById(jsonId);
        if (!el) return;

        // Store the trigger element for returning focus
        this.jsonTriggerElement = el.closest('tr') || el.closest('td') || el;

        this.currentJsonContent = el.textContent.trim();
        this.elements.jsonContent.textContent = this.currentJsonContent;
        this.elements.jsonModal.classList.add('visible');
        document.body.style.overflow = 'hidden';
    },

    /**
     * Close modal and return to original position
     */
    closeModal() {
        this.elements.jsonModal.classList.remove('visible');
        document.body.style.overflow = '';

        // Scroll back to the trigger element
        if (this.jsonTriggerElement) {
            this.jsonTriggerElement.scrollIntoView({ behavior: 'smooth', block: 'center' });
            // Flash the row to show where we returned
            this.jsonTriggerElement.classList.add('copy-flash');
            setTimeout(() => {
                this.jsonTriggerElement.classList.remove('copy-flash');
            }, 500);
        }
    },

    /**
     * Copy JSON to clipboard
     */
    async copyJson() {
        try {
            await navigator.clipboard.writeText(this.currentJsonContent);
            const btn = this.elements.copyJsonBtn;
            const original = btn.textContent;
            btn.textContent = '✓ Copied!';
            setTimeout(() => btn.textContent = original, 2000);
        } catch (e) {
            alert('Failed to copy to clipboard');
        }
    },

    /**
     * Format JSON
     */
    formatJson() {
        try {
            const parsed = JSON.parse(this.currentJsonContent);
            const formatted = JSON.stringify(parsed, null, 2);
            this.currentJsonContent = formatted;
            this.elements.jsonContent.textContent = formatted;
            
            const btn = this.elements.formatJsonBtn;
            const original = btn.textContent;
            btn.textContent = '✓ Formatted!';
            setTimeout(() => btn.textContent = original, 2000);
        } catch (e) {
            alert('Invalid JSON - cannot format');
        }
    },

    /**
     * Sort table by column
     */
    sortTable(table, colIndex, th) {
        const tbody = table.querySelector('tbody');
        if (!tbody) return;

        const rows = Array.from(tbody.querySelectorAll('tr'));
        const isAsc = th.classList.contains('asc');

        // Update header classes
        table.querySelectorAll('th.sortable').forEach(h => {
            h.classList.remove('asc', 'desc');
        });
        th.classList.add(isAsc ? 'desc' : 'asc');

        const direction = isAsc ? -1 : 1;

        rows.sort((a, b) => {
            const cellA = a.cells[colIndex];
            const cellB = b.cells[colIndex];
            if (!cellA || !cellB) return 0;

            let valA = cellA.dataset.sort || cellA.textContent.trim();
            let valB = cellB.dataset.sort || cellB.textContent.trim();

            const numA = parseFloat(valA);
            const numB = parseFloat(valB);

            if (!isNaN(numA) && !isNaN(numB)) {
                return (numA - numB) * direction;
            }

            return valA.localeCompare(valB) * direction;
        });

        // Re-number and re-stripe rows
        rows.forEach((row, i) => {
            if (row.cells[0]) {
                row.cells[0].textContent = i + 1;
            }
            tbody.appendChild(row);
        });
    },

    /**
     * Download as HTML
     */
    downloadHtml() {
        if (!this.elements.resultsContainer.innerHTML) return;

        const html = this.generateStandaloneHtml();
        const blob = new Blob([html], { type: 'text/html' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `diagnostics-report-${new Date().toISOString().slice(0, 10)}.html`;
        a.click();
        URL.revokeObjectURL(url);
    },

    /**
     * Generate standalone HTML with embedded styles and scripts
     */
    generateStandaloneHtml() {
        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Cosmos Diagnostics Report</title>
    <script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js"><\/script>
    <style>${this.getEmbeddedStyles()}</style>
</head>
<body>
    <div class="container">
        <h1>🔍 Cosmos Diagnostics Analysis Report</h1>
        <p style="color: var(--text-muted); margin-bottom: 10px;">Generated: ${new Date().toISOString()}</p>
        <p style="color: var(--text-muted); margin-bottom: 30px; font-family: monospace; font-size: 12px;">Analyzer Version: ${window.VERSION?.commit || 'unknown'} (${window.VERSION?.date || 'unknown'})</p>
        ${this.elements.resultsContainer.innerHTML}
    </div>
    <div id="jsonModal" class="modal">
        <div class="modal-content">
            <div class="modal-header">
                <h3>📄 JSON Content</h3>
                <button class="modal-close" onclick="closeModal()">&times;</button>
            </div>
            <div class="modal-actions">
                <button class="btn btn-small" onclick="copyJson()">📋 Copy</button>
                <button class="btn btn-small" onclick="formatJson()">🔧 Format</button>
            </div>
            <pre id="jsonContent" class="json-display"></pre>
        </div>
    </div>
    <script>${this.getEmbeddedScripts()}</script>
</body>
</html>`;
    },

    /**
     * Get embedded styles for standalone HTML
     */
    getEmbeddedStyles() {
        return `
:root{--bg-color:#1e1e1e;--bg-secondary:#252526;--bg-tertiary:#2d2d2d;--text-color:#d4d4d4;--text-muted:#808080;--border-color:#3e3e3e;--accent-color:#569cd6;--number-color:#b5cea8;--string-color:#ce9178;--null-color:#808080;--success-color:#4ec9b0;--error-color:#f14c4c;--warning-color:#cca700;--link-color:#4fc3f7;--even-row:#252526;--odd-row:#1e1e1e;--hover-row:#094771}
*{box-sizing:border-box;margin:0;padding:0}body{font-family:'Segoe UI',sans-serif;background:var(--bg-color);color:var(--text-color);line-height:1.6;padding:20px}
.container{max-width:1800px;margin:0 auto}h1{color:var(--accent-color);margin-bottom:10px}h2{color:#9cdcfe;margin-bottom:15px}h3{color:var(--success-color)}
.section{margin:25px 0;padding:25px;background:var(--bg-tertiary);border-radius:8px;border:1px solid var(--border-color)}
.table-container{margin:15px 0;overflow-x:auto;border-radius:6px;border:1px solid var(--border-color)}
.table-header{background:linear-gradient(135deg,#2d5a7b,#1e3a5f);color:white;padding:10px 16px;font-weight:600}
.data-table{width:100%;border-collapse:collapse;font-size:13px;background:var(--bg-color)}
.data-table th{background:var(--bg-tertiary);color:var(--accent-color);text-align:left;padding:12px 14px;border-bottom:1px solid var(--border-color);font-weight:600}
.data-table th.sortable{cursor:pointer;padding-right:28px;position:relative}.data-table th.sortable:hover{background:#3a3d41}
.data-table th .sort-icon{position:absolute;right:10px;top:50%;transform:translateY(-50%);opacity:0.4}
.data-table th.asc .sort-icon,.data-table th.desc .sort-icon{opacity:1;color:var(--link-color)}
.data-table td{padding:10px 14px;border-bottom:1px solid var(--border-color)}
.data-table tr:nth-child(even){background:var(--even-row)}.data-table tr:nth-child(odd){background:var(--odd-row)}.data-table tr:hover{background:var(--hover-row)}
.row-num{color:#6a9955;font-size:11px;text-align:center;width:45px}
.num{color:var(--number-color)}.str{color:var(--string-color)}.null{color:var(--null-color)}.warning{color:var(--warning-color)}
.link{color:var(--link-color);cursor:pointer}.link:hover{text-decoration:underline}
.clickable-row{cursor:pointer}.note{color:#6a9955;font-style:italic;font-size:13px;margin:10px 0}
.btn-view{background:#0e639c;color:white;border:none;padding:5px 12px;border-radius:4px;cursor:pointer;font-size:12px}.btn-view:hover{background:#1177bb}
.detail-section{display:none;margin:15px 0;padding:20px;background:var(--bg-secondary);border:2px solid var(--link-color);border-radius:8px;position:relative}
.detail-section.visible{display:block}.btn-close{position:absolute;top:15px;right:15px;background:var(--error-color);color:white;border:none;width:28px;height:28px;border-radius:4px;cursor:pointer;font-size:18px}
.collapsible-header{display:flex;justify-content:space-between;align-items:center;cursor:pointer;padding:12px 16px;background:linear-gradient(135deg,#2d3748,#1a202c);border-radius:6px;margin-bottom:15px}
.collapsible-header:hover{background:linear-gradient(135deg,#3d4758,#2a303c)}.collapsible-header h3,.collapsible-header h4{margin:0}
.collapse-icon{color:var(--link-color)}.collapsible-content{display:none}.collapsible-content.visible{display:block}
.subsection{margin:20px 0;padding:18px;background:var(--bg-color);border-radius:6px;border:1px solid var(--border-color)}.subsection h4{color:var(--success-color);margin-bottom:12px}
.modal{display:none;position:fixed;z-index:1000;left:0;top:0;width:100%;height:100%;background:rgba(0,0,0,0.85)}
.modal.visible{display:flex;align-items:center;justify-content:center}
.modal-content{background:var(--bg-color);border:1px solid var(--border-color);border-radius:8px;width:90%;max-width:1200px;max-height:85vh;display:flex;flex-direction:column}
.modal-header{display:flex;justify-content:space-between;align-items:center;padding:16px 20px;background:var(--bg-tertiary);border-bottom:1px solid var(--border-color)}
.modal-header h3{margin:0;color:var(--link-color)}.modal-close{background:none;border:none;color:var(--text-muted);font-size:28px;cursor:pointer}
.modal-actions{padding:12px 20px;background:var(--bg-secondary);display:flex;gap:10px}
.btn{padding:6px 14px;border:none;border-radius:4px;cursor:pointer;background:var(--accent-color);color:white}.btn:hover{background:var(--accent-hover)}
.json-display{margin:0;padding:20px;overflow:auto;flex:1;background:var(--bg-color);color:var(--string-color);font-family:Consolas,monospace;font-size:13px;white-space:pre-wrap}
details{margin:10px 0}summary{cursor:pointer;color:var(--accent-color)}
`;
    },

    /**
     * Get embedded scripts for standalone HTML
     */
    getEmbeddedScripts() {
        return `
let currentJson='';
const app={
    toggleSection(id){const c=document.getElementById(id),i=document.getElementById(id+'-icon');if(c){c.classList.toggle('visible');if(i)i.textContent=c.classList.contains('visible')?'▼':'▶'}},
    showBucket(id){document.querySelectorAll('.detail-section').forEach(e=>e.classList.remove('visible'));const el=document.getElementById('bucket-'+id);if(el){el.classList.add('visible');el.scrollIntoView({behavior:'smooth'})}},
    closeBucket(id){const el=document.getElementById('bucket-'+id);if(el)el.classList.remove('visible')},
    showGroup(id){document.querySelectorAll('.detail-section').forEach(e=>e.classList.remove('visible'));const el=document.getElementById('group-'+id);if(el){el.classList.add('visible');el.scrollIntoView({behavior:'smooth'})}},
    closeGroup(id){const el=document.getElementById('group-'+id);if(el)el.classList.remove('visible')},
    showJson(id){const el=document.getElementById(id);if(el){this.triggerEl=el.closest('tr')||el;currentJson=el.textContent.trim();document.getElementById('jsonContent').textContent=currentJson;document.getElementById('jsonModal').classList.add('visible');document.body.style.overflow='hidden'}},
    triggerEl:null
};
function closeModal(){document.getElementById('jsonModal').classList.remove('visible');document.body.style.overflow='';if(app.triggerEl){app.triggerEl.scrollIntoView({behavior:'smooth',block:'center'});app.triggerEl.style.background='var(--hover-row)';setTimeout(()=>app.triggerEl.style.background='',500)}}
function copyJson(){navigator.clipboard.writeText(currentJson).then(()=>{const b=event.target;b.textContent='✓ Copied!';setTimeout(()=>b.textContent='📋 Copy',2000)})}
function formatJson(){try{const f=JSON.stringify(JSON.parse(currentJson),null,2);currentJson=f;document.getElementById('jsonContent').textContent=f;const b=event.target;b.textContent='✓ Formatted!';setTimeout(()=>b.textContent='🔧 Format',2000)}catch(e){alert('Invalid JSON')}}
document.addEventListener('click',e=>{const th=e.target.closest('th.sortable');if(th){const t=th.closest('table'),c=parseInt(th.dataset.col);if(t&&!isNaN(c)){const tb=t.querySelector('tbody');if(!tb)return;const r=Array.from(tb.querySelectorAll('tr')),a=th.classList.contains('asc');t.querySelectorAll('th.sortable').forEach(h=>h.classList.remove('asc','desc'));th.classList.add(a?'desc':'asc');const d=a?-1:1;r.sort((x,y)=>{const ca=x.cells[c],cb=y.cells[c];if(!ca||!cb)return 0;let va=ca.dataset.sort||ca.textContent.trim(),vb=cb.dataset.sort||cb.textContent.trim();const na=parseFloat(va),nb=parseFloat(vb);return!isNaN(na)&&!isNaN(nb)?(na-nb)*d:va.localeCompare(vb)*d});r.forEach((row,i)=>{if(row.cells[0])row.cells[0].textContent=i+1;tb.appendChild(row)})}}});
document.addEventListener('keydown',e=>{if(e.key==='Escape')closeModal()});

// Initialize charts on load
function initCharts(){
    const chartOpts=(yAxes)=>({
        responsive:true,maintainAspectRatio:false,
        interaction:{mode:'index',intersect:false},
        plugins:{legend:{labels:{color:'#d4d4d4'}}},
        scales:Object.assign({x:{type:'category',display:true,ticks:{color:'#d4d4d4',maxRotation:45,maxTicksLimit:20},grid:{color:'rgba(255,255,255,0.1)'}}},
            yAxes.reduce((a,y,i)=>(a[y.id]={type:'linear',display:y.display!==false,position:y.position,title:{display:true,text:y.title,color:'#d4d4d4'},ticks:{color:'#d4d4d4'},grid:{color:i===0?'rgba(255,255,255,0.1)':'transparent'}},a),{}))
    });
    ['systemMetricsChart','clientConfigChart'].forEach(id=>{
        const c=document.getElementById(id),d=document.getElementById(id+'-data');
        if(c&&d&&window.Chart){
            try{
                const data=JSON.parse(d.textContent);
                const yAxes=id==='systemMetricsChart'?
                    [{id:'y',title:'CPU (%)',position:'left'},{id:'y1',title:'Memory (MB)',position:'right'},{id:'y2',title:'Thread Wait (ms)',position:'right',display:false},{id:'y3',title:'TCP Connections',position:'right',display:false}]:
                    [{id:'y',title:'Processor Count',position:'left'},{id:'y1',title:'Client Count',position:'right'}];
                new Chart(c.getContext('2d'),{type:'line',data:data,options:chartOpts(yAxes)});
            }catch(e){console.error('Chart error:',e)}
        }
    });
}
if(document.readyState==='complete')initCharts();else window.addEventListener('load',initCharts);
`;
    }
};

// Initialize on DOM load
document.addEventListener('DOMContentLoaded', () => app.init());
