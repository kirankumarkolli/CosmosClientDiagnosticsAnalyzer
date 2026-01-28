# Cosmos Diagnostics Analyzer

A **100% client-side** web app that analyzes Azure Cosmos DB diagnostics logs. **Your data never leaves your browser!**

## Quick Start

**Run Locally:**
```powershell
# Windows
Start-Process "docs\index.html"

# macOS/Linux  
open docs/index.html
```

**Or deploy to GitHub Pages** (see below)

---

## Features

- **Truncated JSON Repair** - Automatically fixes incomplete/malformed JSON lines
- **Percentile Analysis** - P50, P75, P90, P95, P99 latency metrics
- **Operation Bucketing** - Group by operation name with drill-down
- **Network Analysis** - ResourceType, StatusCode, TransportEvent groupings
- **Transport Timeline** - Phase breakdown (Created → Completed) with bottleneck detection
- **Endpoint Statistics** - Top endpoints by frequency per phase
- **Dark Theme** - LinqPad-inspired styling with syntax highlighting
- **Sortable Tables** - Click any column header to sort
- **JSON Viewer** - Modal with copy and format options
- **Export Reports** - Download self-contained HTML files
- **Privacy First** - All processing in browser, no data sent anywhere

---

## Input Format

The analyzer expects a text file with one JSON object per line (JSONL format):

```
{"Summary":{"DirectCalls":...},"name":"Operation Name","duration in milliseconds":123.45,...}
{"Summary":{"DirectCalls":...},"name":"Operation Name","duration in milliseconds":456.78,...}
```

Supports Azure Cosmos DB SDK diagnostics output including:
- Client configuration
- Store response statistics  
- Transport request timeline
- Replica health status

---

## How to Use

1. Open `docs/index.html` in your browser
2. Drag & drop your diagnostics file (or click to browse)
3. Set latency threshold (default: 600ms)
4. Click **Analyze Diagnostics**
5. Explore results:
   - Click operation names to see detailed entries
   - Click column headers to sort tables
   - Click "View" buttons to see raw JSON
   - Expand collapsible sections for more details
6. Click **Download HTML** to save a standalone report

---

## Deploy to GitHub Pages

### Step 1: Push to GitHub

```powershell
git add .
git commit -m "Add diagnostics analyzer"
git push origin main
```

### Step 2: Enable GitHub Pages

1. Go to your repository on GitHub
2. Click **Settings** → **Pages**
3. Under **Build and deployment**:
   - Source: `Deploy from a branch`
   - Branch: `main`
   - Folder: `/docs`
4. Click **Save**
5. Wait 1-2 minutes for deployment

Your app will be available at: `https://YOUR_USERNAME.github.io/REPO_NAME/`

---

## Project Structure

```
docs/                         <- GitHub Pages source (static files only)
├── index.html               <- Main application page
├── css/
│   └── styles.css           <- Dark theme styles
└── js/
    ├── json-parser.js       <- JSON parsing & repair
    ├── analyzer.js          <- Analysis engine with percentiles
    ├── report-generator.js  <- HTML report generation
    └── app.js               <- Main application logic

Diagnostics.Core/            <- C# library (optional server version)
Diagnostics.Functions/       <- Azure Functions (optional)
Diagnostics.Web/             <- ASP.NET web API (optional)
```

---

## Technical Details

### JSON Repair Algorithm

The parser handles truncated JSON by:
1. Attempting direct `JSON.parse()`
2. Removing trailing `...` markers and commas
3. Tracking unclosed brackets, braces, and strings
4. Closing structures in LIFO order
5. Retrying up to 10 iterations

### Analysis Metrics

- **Per Operation Bucket**: count, min, max, P50, P75, P90, P95, P99, network call range
- **Per Network Interaction**: duration, status codes, BE latency, transport phases
- **Per Transport Event**: phase breakdown with endpoint distribution

---

## License

MIT
