/**
 * Main Application Logic
 * Handles file upload, processing, and UI interactions
 */

// Global state
let currentResult = null;
let currentJsonContent = '';

// DOM Elements
const dropArea = document.getElementById('dropArea');
const fileInput = document.getElementById('fileInput');
const fileName = document.getElementById('fileName');
const analyzeBtn = document.getElementById('analyzeBtn');
const latencyThreshold = document.getElementById('latencyThreshold');
const uploadSection = document.getElementById('upload-section');
const progressSection = document.getElementById('progress-section');
const progressFill = document.getElementById('progressFill');
const progressText = document.getElementById('progressText');
const resultsSection = document.getElementById('results-section');
const resultsContainer = document.getElementById('results-container');
const errorSection = document.getElementById('error-section');
const errorMessage = document.getElementById('error-message');
const downloadBtn = document.getElementById('downloadBtn');
const newAnalysisBtn = document.getElementById('newAnalysisBtn');
const retryBtn = document.getElementById('retryBtn');

// File handling
let selectedFile = null;

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    setupEventListeners();
});

function setupEventListeners() {
    // Drag and drop
    dropArea.addEventListener('click', () => fileInput.click());
    dropArea.addEventListener('dragover', handleDragOver);
    dropArea.addEventListener('dragleave', handleDragLeave);
    dropArea.addEventListener('drop', handleDrop);
    
    // File input
    fileInput.addEventListener('change', handleFileSelect);
    
    // Buttons
    analyzeBtn.addEventListener('click', analyzeFile);
    downloadBtn.addEventListener('click', downloadHtml);
    newAnalysisBtn.addEventListener('click', resetUI);
    retryBtn.addEventListener('click', resetUI);
}

function handleDragOver(e) {
    e.preventDefault();
    dropArea.classList.add('dragover');
}

function handleDragLeave(e) {
    e.preventDefault();
    dropArea.classList.remove('dragover');
}

function handleDrop(e) {
    e.preventDefault();
    dropArea.classList.remove('dragover');
    
    const files = e.dataTransfer.files;
    if (files.length > 0) {
        selectFile(files[0]);
    }
}

function handleFileSelect(e) {
    if (e.target.files.length > 0) {
        selectFile(e.target.files[0]);
    }
}

function selectFile(file) {
    selectedFile = file;
    fileName.textContent = `📄 ${file.name} (${formatFileSize(file.size)})`;
    fileName.classList.add('visible');
    analyzeBtn.disabled = false;
}

function formatFileSize(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
}

async function analyzeFile() {
    if (!selectedFile) return;
    
    // Show loading state on button
    const btnText = analyzeBtn.querySelector('.btn-text');
    const btnLoading = analyzeBtn.querySelector('.btn-loading');
    if (btnText) btnText.hidden = true;
    if (btnLoading) btnLoading.hidden = false;
    analyzeBtn.disabled = true;
    
    // Show progress
    showSection('progress');
    updateProgress('Reading file...', 5);
    
    try {
        // Read file
        const content = await readFile(selectedFile);
        updateProgress('Parsing diagnostics...', 10);
        
        // Allow UI to update
        await sleep(50);
        
        // Parse
        const parser = new DiagnosticsParser();
        const threshold = parseInt(latencyThreshold.value) || 600;
        
        currentResult = parser.analyzeDiagnostics(content, threshold, (text, percent) => {
            updateProgress(text, percent);
        });
        
        // Generate HTML
        updateProgress('Generating report...', 95);
        await sleep(50);
        
        
        const generator = new HtmlGenerator();
        const html = generator.generateHtml(currentResult);
        
        // Display results
        resultsContainer.innerHTML = html;
        showSection('results');
        
        // Initialize interactive elements
        initializeInteractiveElements();
        
    } catch (error) {
        console.error('Analysis error:', error);
        showError(error.message || 'An error occurred while analyzing the file');
    } finally {
        // Reset button state
        const btnText = analyzeBtn.querySelector('.btn-text');
        const btnLoading = analyzeBtn.querySelector('.btn-loading');
        if (btnText) btnText.hidden = false;
        if (btnLoading) btnLoading.hidden = true;
        analyzeBtn.disabled = false;
    }
}

function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

function readFile(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = (e) => resolve(e.target.result);
        reader.onerror = () => reject(new Error('Failed to read file'));
        reader.readAsText(file);
    });
}

function updateProgress(text, percent) {
    progressText.textContent = text;
    progressFill.style.width = percent + '%';
}

function showSection(section) {
    uploadSection.hidden = section !== 'upload';
    progressSection.hidden = section !== 'progress';
    resultsSection.hidden = section !== 'results';
    errorSection.hidden = section !== 'error';
}

function showError(message) {
    errorMessage.textContent = message;
    showSection('error');
}

function resetUI() {
    selectedFile = null;
    fileInput.value = '';
    fileName.textContent = '';
    fileName.classList.remove('visible');
    analyzeBtn.disabled = true;
    progressFill.style.width = '0%';
    resultsContainer.innerHTML = '';
    currentResult = null;
    showSection('upload');
}

function downloadHtml() {
    if (!resultsContainer.innerHTML) return;
    
    const fullHtml = `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Cosmos Diagnostics Analysis Report</title>
    <style>${getEmbeddedStyles()}</style>
</head>
<body>
    <div class="container">
        <h1>🔍 Cosmos Diagnostics Analysis</h1>
        ${resultsContainer.innerHTML}
    </div>
    <script>${getEmbeddedScripts()}</script>
</body>
</html>`;
    
    const blob = new Blob([fullHtml], { type: 'text/html' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `diagnostics-report-${new Date().toISOString().slice(0, 10)}.html`;
    a.click();
    URL.revokeObjectURL(url);
}

function getEmbeddedStyles() {
    // Return minimal inline styles for standalone HTML
    return `
        :root{--bg-color:#1e1e1e;--text-color:#d4d4d4;--header-bg:#2d2d2d;--border-color:#3e3e3e;--accent-color:#569cd6;--number-color:#b5cea8;--string-color:#ce9178;--null-color:#808080;--even-row:#252526;--odd-row:#1e1e1e;--hover-row:#094771}
        *{box-sizing:border-box}body{font-family:'Segoe UI',sans-serif;background:var(--bg-color);color:var(--text-color);margin:0;padding:20px;line-height:1.6}
        .container{max-width:1800px;margin:0 auto}h1{color:var(--accent-color);border-bottom:2px solid var(--accent-color);padding-bottom:10px}h2{color:#9cdcfe;margin-top:30px}h3{color:#4ec9b0}
        .section{margin:20px 0;padding:20px;background:var(--header-bg);border-radius:8px;border:1px solid var(--border-color)}
        .dump-container{margin:10px 0;overflow-x:auto}.dump-header{background:linear-gradient(135deg,#2d5a7b,#1e3a5f);color:#fff;padding:8px 15px;font-weight:600;border-radius:4px 4px 0 0}
        .dump-table{width:100%;border-collapse:collapse;font-size:13px;background:var(--bg-color)}.dump-table th{background:var(--header-bg);color:var(--accent-color);text-align:left;padding:10px 12px;border:1px solid var(--border-color)}
        .dump-table td{padding:8px 12px;border:1px solid var(--border-color)}.dump-table tr.even{background:var(--even-row)}.dump-table tr.odd{background:var(--odd-row)}.dump-table tr:hover{background:var(--hover-row)}
        .row-num{color:#6a9955;font-size:11px;text-align:center;width:40px}.number{color:var(--number-color)}.string{color:var(--string-color)}.null{color:var(--null-color);font-style:italic}.note{color:#6a9955;font-style:italic}
        .bucket-link{color:#4fc3f7;text-decoration:none;cursor:pointer}.bucket-link:hover{text-decoration:underline}
        .bucket-details{position:relative;border:2px solid #4fc3f7;display:none}.btn-close{position:absolute;top:15px;right:15px;background:#dc3545;color:white;border:none;padding:5px 12px;border-radius:4px;cursor:pointer}
        .btn-view,.btn-json{background:#0e639c;color:white;border:none;padding:4px 10px;border-radius:4px;cursor:pointer;font-size:12px}.btn-view:hover,.btn-json:hover{background:#1177bb}
        .clickable-row{cursor:pointer}.clickable-row:hover{background:var(--hover-row)!important}.clickable-header{cursor:pointer}.clickable-header:hover{color:#4fc3f7}.click-hint{font-size:12px;color:#6a9955;margin-left:10px}
        .section-header.collapsible{display:flex;justify-content:space-between;align-items:center;cursor:pointer;padding:5px 10px;margin:-20px -20px 15px -20px;background:linear-gradient(135deg,#2d3748,#1a202c);border-radius:8px 8px 0 0}
        .collapse-icon{color:#4fc3f7}.sortable th.sortable{cursor:pointer;position:relative;padding-right:25px}.sort-icon{position:absolute;right:8px;opacity:0.4}
        .modal{display:none;position:fixed;z-index:1000;left:0;top:0;width:100%;height:100%;background:rgba(0,0,0,0.8)}.modal-content{background:var(--bg-color);margin:5% auto;border:1px solid var(--border-color);border-radius:8px;width:90%;max-width:1200px;max-height:80vh;display:flex;flex-direction:column}
        .modal-header{display:flex;justify-content:space-between;align-items:center;padding:15px 20px;background:var(--header-bg);border-radius:8px 8px 0 0}.modal-header h3{margin:0;color:#4fc3f7}.modal-close{background:none;border:none;color:var(--null-color);font-size:28px;cursor:pointer}
        .modal-actions{padding:10px 20px;background:var(--even-row);display:flex;gap:10px}.btn-copy,.btn-format{background:#0e639c;color:white;border:none;padding:6px 12px;border-radius:4px;cursor:pointer}
        .json-display{margin:0;padding:20px;overflow:auto;flex:1;color:var(--string-color);font-family:Consolas,monospace;font-size:13px;white-space:pre-wrap}.json-content{display:none}
        details{margin:10px 0;padding:10px;background:var(--even-row);border-radius:4px}summary{cursor:pointer;color:var(--accent-color)}
    `;
}

function getEmbeddedScripts() {
    return `
        let currentJsonContent='';
        function toggleSection(id){const c=document.getElementById(id),i=document.getElementById(id+'-icon');c.style.display=c.style.display==='none'?'block':'none';i.textContent=c.style.display==='none'?'▶':'▼'}
        function showBucket(id){document.querySelectorAll('.bucket-details').forEach(e=>e.style.display='none');const el=document.getElementById('bucket-'+id);if(el){el.style.display='block';el.scrollIntoView({behavior:'smooth'})}}
        function showGroup(id){document.querySelectorAll('.bucket-details').forEach(e=>e.style.display='none');const el=document.getElementById('group-'+id);if(el){el.style.display='block';el.scrollIntoView({behavior:'smooth'})}}
        function showJson(id){const el=document.getElementById(id);if(el){currentJsonContent=el.textContent.trim();document.getElementById('jsonModalContent').textContent=currentJsonContent;document.getElementById('jsonModal').style.display='block'}}
        function closeJsonModal(e){if(e&&e.target!==document.getElementById('jsonModal'))return;document.getElementById('jsonModal').style.display='none'}
        function copyJsonContent(){navigator.clipboard.writeText(currentJsonContent).then(()=>{const b=document.querySelector('.btn-copy');b.textContent='✓ Copied!';setTimeout(()=>b.textContent='📋 Copy to Clipboard',2000)})}
        function formatJson(){try{const p=JSON.parse(currentJsonContent.trim()),f=JSON.stringify(p,null,2);document.getElementById('jsonModalContent').textContent=f;currentJsonContent=f}catch(e){alert('Invalid JSON')}}
        function sortTable(id,col){const t=document.getElementById(id);if(!t)return;const b=t.querySelector('tbody'),r=Array.from(b.querySelectorAll('tr')),h=t.querySelectorAll('thead th')[col],asc=h.classList.contains('asc');t.querySelectorAll('th.sortable').forEach(x=>x.classList.remove('asc','desc'));h.classList.add(asc?'desc':'asc');const d=asc?-1:1;r.sort((a,b)=>{let va=a.cells[col].getAttribute('data-sort')||a.cells[col].innerText.trim(),vb=b.cells[col].getAttribute('data-sort')||b.cells[col].innerText.trim();const na=parseFloat(va),nb=parseFloat(vb);return!isNaN(na)&&!isNaN(nb)?(na-nb)*d:va.localeCompare(vb)*d});r.forEach((row,i)=>{row.cells[0].innerText=i+1;row.className=(i+1)%2===0?'even':'odd';b.appendChild(row)})}
        document.addEventListener('keydown',e=>{if(e.key==='Escape')closeJsonModal()});
    `;
}

// Interactive element handlers
function initializeInteractiveElements() {
    // Add click-to-copy for cells
    document.querySelectorAll('.dump-table td').forEach(cell => {
        if (!cell.querySelector('a') && !cell.querySelector('button')) {
            cell.addEventListener('click', function() {
                const text = this.innerText;
                navigator.clipboard.writeText(text).then(() => {
                    const original = this.style.background;
                    this.style.background = '#094771';
                    setTimeout(() => this.style.background = original, 200);
                });
            });
            cell.style.cursor = 'pointer';
            cell.title = 'Click to copy';
        }
    });
}

// Global functions for HTML onclick handlers
window.toggleSection = function(sectionId) {
    const content = document.getElementById(sectionId);
    const icon = document.getElementById(sectionId + '-icon');
    
    if (content.style.display === 'none') {
        content.style.display = 'block';
        if (icon) icon.textContent = '▼';
    } else {
        content.style.display = 'none';
        if (icon) icon.textContent = '▶';
    }
};

window.showBucket = function(bucketId) {
    document.querySelectorAll('.bucket-details').forEach(el => {
        el.style.display = 'none';
    });
    
    const bucketEl = document.getElementById('bucket-' + bucketId);
    if (bucketEl) {
        bucketEl.style.display = 'block';
        bucketEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
};

window.showGroup = function(groupId) {
    document.querySelectorAll('.bucket-details').forEach(el => {
        el.style.display = 'none';
    });
    
    const groupEl = document.getElementById('group-' + groupId);
    if (groupEl) {
        groupEl.style.display = 'block';
        groupEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
};

window.showJson = function(jsonId) {
    const jsonEl = document.getElementById(jsonId);
    if (!jsonEl) return;
    
    currentJsonContent = jsonEl.textContent.trim();
    document.getElementById('jsonModalContent').textContent = currentJsonContent;
    document.getElementById('jsonModal').style.display = 'block';
    document.body.style.overflow = 'hidden';
};

window.closeJsonModal = function(event) {
    if (event && event.target !== document.getElementById('jsonModal')) return;
    document.getElementById('jsonModal').style.display = 'none';
    document.body.style.overflow = 'auto';
};

window.copyJsonContent = function() {
    navigator.clipboard.writeText(currentJsonContent).then(() => {
        const btn = document.querySelector('.btn-copy');
        const originalText = btn.innerText;
        btn.innerText = '✓ Copied!';
        btn.style.background = '#28a745';
        setTimeout(() => {
            btn.innerText = originalText;
            btn.style.background = '#0e639c';
        }, 2000);
    });
};

window.formatJson = function() {
    try {
        const trimmed = currentJsonContent.trim();
        const parsed = JSON.parse(trimmed);
        const formatted = JSON.stringify(parsed, null, 2);
        document.getElementById('jsonModalContent').textContent = formatted;
        currentJsonContent = formatted;
        
        const btn = document.querySelector('.btn-format');
        const originalText = btn.innerText;
        btn.innerText = '✓ Formatted!';
        btn.style.background = '#28a745';
        setTimeout(() => {
            btn.innerText = originalText;
            btn.style.background = '#0e639c';
        }, 2000);
    } catch (e) {
        alert('Invalid JSON - cannot format\\n\\nError: ' + e.message);
    }
};

window.sortTable = function(tableId, colIndex) {
    const table = document.getElementById(tableId);
    if (!table) return;
    
    const tbody = table.querySelector('tbody');
    const rows = Array.from(tbody.querySelectorAll('tr'));
    const th = table.querySelectorAll('thead th')[colIndex];
    
    const isAsc = th.classList.contains('asc');
    
    table.querySelectorAll('th.sortable').forEach(h => {
        h.classList.remove('asc', 'desc');
    });
    
    let direction = 1;
    if (isAsc) {
        th.classList.add('desc');
        direction = -1;
    } else {
        th.classList.add('asc');
        direction = 1;
    }
    
    rows.sort((a, b) => {
        const cellA = a.cells[colIndex];
        const cellB = b.cells[colIndex];
        
        let valA = cellA.getAttribute('data-sort') || cellA.innerText.trim();
        let valB = cellB.getAttribute('data-sort') || cellB.innerText.trim();
        
        const numA = parseFloat(valA);
        const numB = parseFloat(valB);
        
        if (!isNaN(numA) && !isNaN(numB)) {
            return (numA - numB) * direction;
        }
        
        return valA.localeCompare(valB) * direction;
    });
    
    rows.forEach((row, index) => {
        row.cells[0].innerText = index + 1;
        row.className = (index + 1) % 2 === 0 ? 'even' : 'odd';
        tbody.appendChild(row);
    });
};

// Close modal with Escape key
document.addEventListener('keydown', function(e) {
    if (e.key === 'Escape') {
        closeJsonModal();
    }
});
