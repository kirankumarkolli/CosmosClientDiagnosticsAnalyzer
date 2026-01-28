# Cosmos Diagnostics Analyzer - Web App Implementation Plan

## ✅ STATUS: COMPLETED (2026-01-28)

### Upcoming Features
- **Timeline Visualization**: Chrome-style network waterfall (Gantt chart) in JSON Modal

### Bug Fixes
- **2026-01-28**: Fixed button showing "Analyzing..." on page load - CSS `[hidden]` attribute override
- **2026-01-28**: Added repaired/failed JSON counts to Parsing Statistics summary
- **2026-01-28**: JSON column now shows repair status (✓ Valid or 🔧 Repaired) for each entry
- **2026-01-28**: Modal close now scrolls back to originating table row with highlight flash
- **2026-01-28**: Added version display (commit hash + date) in footer and exported reports
- **2026-01-28**: Fixed JSON view - store raw JSON without HTML encoding so View/Format works correctly
- **2026-01-28**: Operation bucket drill-down now groups entries by percentile ranges (≤P50, P50-P75, P75-P90, P90-P95, P95-P99, >P99)

---

## Overview

**Goal:** Create a fresh single-page web application (HTML/CSS/JavaScript only) to analyze Azure Cosmos DB diagnostics JSON logs, hostable on GitHub Pages.

**Input Format:** 
- Text file with one JSON object per line (JSONL format)
- Each line contains Azure Cosmos DB diagnostics with nested hierarchical structure
- Lines may be truncated/malformed and need repair
- Sample: 3,107 lines, ~31MB of diagnostics data

**Key JSON Structure (per line):**
```
{
  "Summary": { "DirectCalls": {...}, "GatewayCalls": {...} },
  "name": "Operation Name",
  "start datetime": "ISO timestamp",
  "duration in milliseconds": number,
  "data": { "Client Configuration": {...}, ... },
  "children": [ nested operations... ]
}
```

**Critical nested data:**
- `children[].data["Client Side Request Stats"].StoreResponseStatistics[]` - Network interactions
- `transportRequestTimeline.requestTimeline[]` - Request phase timing (Created, ChannelAcquisitionStarted, Pipelined, Transit Time, Received, Completed)
- `data["Client Configuration"]` - Client configuration metrics
- `children[].data["Client Side Request Stats"].SystemInfo.systemHistory[]` - System metrics over time
- Status codes, latencies, replica health, exceptions

---

## Technical Requirements

### Constraints
- **No build tools** - Pure HTML/CSS/JavaScript
- **No external dependencies** - Self-contained, prefer to leverage JS popular libraries don't re-invent
- **Client-side only** - Data never leaves browser
- **GitHub Pages compatible** - Static files in `/docs` folder
- **Single-file export** - Generate standalone HTML reports
- **Chart.js** - Use Chart.js library for time-series plots (CDN or embedded)

### Browser Support
- Modern browsers (Chrome, Firefox, Edge, Safari)
- ES6+ JavaScript features

---

## Functional Specifications

### 1. File Input Module

| Feature | Specification |
|---------|--------------|
| Drag & drop | Drop zone with visual feedback |
| File picker | Click to browse |
| **Supported formats** | `.txt`, `.json`, `.log`, `.xlsx`, `.xls`, `.xlsb`, `.csv`, `.ods` |
| File info | Display filename and size |
| Large file handling | Process in chunks, show progress |
| LatencyThreshold | textbox input to accept integer type and use it to filter |

**Excel File Support:**

| Format | Extension | Library |
|--------|-----------|---------|
| Excel 2007+ | .xlsx | SheetJS |
| Excel 97-2004 | .xls | SheetJS |
| Excel Binary | .xlsb | SheetJS |
| CSV/TSV | .csv | SheetJS |
| OpenDocument | .ods | SheetJS |

**Excel Parsing Behavior:**
- Reads **first sheet** only
- Extracts **column A** (first column)
- Skips header row if first cell doesn't start with `{`
- Only includes cells that look like JSON objects (start with `{`)
- Converts extracted cells to newline-separated text for JsonParser

**Limitations:**
- Password-protected files not supported
- Formulas read as computed values only
- Large files (50MB+) may be slow in browser

### 2. JSON Parser Module

| Feature | Specification |
|---------|--------------|
| Line-by-line parsing | Split by newline, parse each independently |
| Truncated JSON repair | Close unclosed brackets/braces/strings |
| Key normalization | Handle both `"duration in milliseconds"` and `durationInMs` |
| Error tolerance | Skip unparseable lines, count failures |
| Progress reporting | Callback for UI progress updates |

**JSON Repair Algorithm:**
1. Attempt direct `JSON.parse()`
2. If fails: remove trailing `...` markers
3. Track open brackets/braces/strings
4. Close unclosed elements in LIFO order
5. Retry up to 10 iterations

### 3. Analysis Engine

| Feature | Specification |
|---------|--------------|
| Latency threshold | User-configurable (default: 600ms) |
| Operation bucketing | Group by operation name |
| Network extraction | Extract from recursive `children` tree |
| Grouping | By ResourceType→OperationType, StatusCode→SubStatusCode |
| Transport events | Group by last event + bottleneck phase |
| Endpoint analysis | Count unique physical addresses per phase |
| Percentile calculation | P50, P75, P90, P95, P99 for latency distributions |

**Percentile Requirements:**

| Percentile | Range | Description |
|------------|-------|-------------|
| P50 (Median) | ≤ P50 | All requests up to and equal to P50 value |
| P75 | (P50, P75] | Requests between P50 and P75 (exclusive of P50, inclusive of P75) |
| P90 | (P75, P90] | Requests between P75 and P90 (exclusive of P75, inclusive of P90) |
| P95 | (P90, P95] | Requests between P90 and P95 (exclusive of P90, inclusive of P95) |
| P99 | (P90, P99] | Requests between P90 and P99 (exclusive of P90, inclusive of P99) |

**Percentile Calculation Formula:**
```
index = ceil((percentile / 100) * count) - 1
value = sortedArray[max(0, min(index, count - 1))]
```

**Where Percentiles are Displayed:**

| Section | Percentiles Shown |
|---------|------------------|
| Operation Buckets | P50, P75, P90, P95, P99 (columns) |
| GroupBy ResourceType→OperationType | P50, P75, P90, P95, P99 (columns) |
| GroupBy StatusCode→SubStatusCode | P50, P75, P90, P95, P99 (columns) |
| Transport Event Groups | P50, P75, P90, P95, P99 (header) |
| Phase Breakdown | P50, P75, P90, P95, P99 (columns) |

**Computed Metrics:**
- Per bucket: min/max duration, min/max network call count, total count, percentile P50, P75, P90, P95, P99 durations
- Per network interaction: duration, status codes, BE latency, transport timeline phases
- Per phase: percentile P50, P75, P90, P95, P99 durations, endpoint distribution, top 10 endpoints by frequency

### 4. Report Generator

| Section | Content |
|---------|---------|
| Summary | Total lines, successfully parsed, repaired (truncated JSON fixed), failed to parse, latency threshold, high-latency count, high-latency rate |
| **System Metrics Time Plot** | Interactive chart with CPU%, Memory (MB), Thread Wait (ms), TCP Connections over time |
| **Client Configuration Time Plot** | Interactive chart with ProcessorCount, ClientsCreated, ActiveClients over time |
| Operation Buckets | Table with clickable percentile drill-down |
| High Latency Network | Top 100 interactions (collapsible) |
| GroupBy ResourceType→OperationType | Sortable table, click row to expand entries |
| GroupBy StatusCode→SubStatusCode | Sortable table, click row to expand entries |
| GroupBy LastTransportEvent | Sortable table, click row to expand phase breakdown with percentile drill-down and endpoint stats |

**System Metrics Time Plot:**
| Metric | JSON Path | Display |
|--------|-----------|---------|
| CPU (%) | `systemHistory[].Cpu` | Blue line (#4fc3f7) |
| Memory (MB) | `systemHistory[].Memory` / 1MB | Green line (#81c784) |
| Thread Wait (ms) | `systemHistory[].ThreadInfo.ThreadWaitIntervalInMs` | Orange line (#ffb74d) |
| TCP Connections | `systemHistory[].NumberOfOpenTcpConnection` | Purple line (#ba68c8) |

Statistics table: Min, P50, P75, P90, P95, P99, Max, Avg for each metric.

**Client Configuration Time Plot:**
| Metric | JSON Path | Display |
|--------|-----------|---------|
| ProcessorCount | `data["Client Configuration"].ProcessorCount` | Teal line |
| ClientsCreated | `data["Client Configuration"].NumberOfClientsCreated` | Green line |
| ActiveClients | `data["Client Configuration"].NumberOfActiveClients` | Orange line |

Statistics table: Min, P50, P75, P90, P95, P99, Max, Avg for each metric.

**Table Features:**
- Sortable columns (click header to toggle asc/desc)
- Row numbers
- Clickable rows to show detail sections
- JSON viewer for raw data with repair status indicator (✓ Valid or 🔧 Repaired)
- Raw JSON stored unencoded for proper View/Format functionality

**Percentile Drill-Down (Click-Through):**
| Table | Drill-Down Behavior |
|-------|---------------------|
| Operation Buckets | Click any percentile value to expand grouped entries by percentile range |
| Phase Breakdown | Click any percentile value to expand entries grouped by range (≤P50, P50-P75, etc.) |

Percentile groups shown:
- ≤P50: All entries up to and equal to P50 value
- P50-P75: Entries between P50 and P75 (exclusive/inclusive)
- P75-P90: Entries between P75 and P90
- P90-P95: Entries between P90 and P95
- P95-P99: Entries between P95 and P99
- >P99: Entries above P99 value

### 5. UI/UX Design

| Element | Specification |
|---------|--------------|
| Theme | Dark theme (LinqPad-inspired) |
| Layout | Single column, max-width 1800px |
| Typography | Segoe UI family, monospace for code |
| Color scheme | See CSS variables below |
| Responsive | Mobile-friendly with breakpoints |

**CSS Variables:**
```css
--bg-color: #1e1e1e
--text-color: #d4d4d4
--accent-color: #569cd6
--number-color: #b5cea8
--string-color: #ce9178
```

### 6. Export Feature

| Feature | Specification |
|---------|--------------|
| Format | Self-contained HTML |
| Styles | Minified embedded CSS |
| Scripts | Minified embedded JS |
| Interactivity | All features work offline |
| Modal behavior | Closing JSON modal returns focus to originating row with highlight |
| Version tracking | Git commit hash displayed in footer and embedded in exported reports |

### 7. Timeline Visualization (JSON Modal)

**Feature:** Chrome DevTools-style network waterfall (Gantt chart) for Transport Request Timeline events.

**Trigger:** "📊 Show Timeline" button in JSON Modal (toggles between Timeline and JSON view)

**Behavior:** When timeline is shown, JSON content is hidden. Button toggles between "📊 Show Timeline" and "📄 Show JSON".

**Data Source:** `StoreResponseStatistics[].StoreResult.transportRequestTimeline.requestTimeline[]`

| Phase | Color | Description |
|-------|-------|-------------|
| Created | #4CAF50 (green) | Request object created |
| ChannelAcquisitionStarted | #2196F3 (blue) | Channel acquisition began |
| Pipelined | #FF9800 (orange) | Request pipelined to channel |
| Transit Time | #9C27B0 (purple) | Network round-trip time |
| Received | #00BCD4 (cyan) | Response received |
| Completed | #607D8B (gray) | Request completed |

**UI Components:**
| Component | Description |
|-----------|-------------|
| Legend | Color-coded phase labels |
| Time Axis | Auto-scaled horizontal axis (0ms to max duration) |
| Swimlanes | One row per StoreResult request |
| Row Label | StatusCode + truncated endpoint |
| Waterfall Bar | Stacked colored segments for each phase |
| Tooltip | Hover to show all phase durations |
| Zoom Controls | ➕ Zoom In, ➖ Zoom Out, ⟲ Reset |

**Timing Calculation:**
- Start Time: Inferred from first phase `startTimeUtc`
- Bar Position: Relative to earliest request across all StoreResults
- Phase Width: Proportional to `durationInMs`

---

## File Structure

```
docs/
├── index.html              # Main application page
├── css/
│   └── styles.css          # Application styles
└── js/
    ├── echarts.min.js      # ECharts library for time-series charts
    ├── xlsx.min.js         # SheetJS library for Excel parsing
    ├── version.js          # Version info (commit hash, date)
    ├── json-parser.js      # JSON parsing and repair
    ├── excel-parser.js     # Excel file parsing (extracts column A)
    ├── analyzer.js         # Analysis engine
    ├── report-generator.js # HTML report generation
    ├── timeline.js         # Timeline visualization for JSON modal
    └── app.js              # Main application logic
```

---

## Implementation Workplan

### Phase 1: Setup & Core Parser
- [x] Delete existing `/docs` folder content
- [x] Create new `index.html` with basic structure
- [x] Create `css/styles.css` with CSS variables and base styles
- [x] Implement `js/json-parser.js`:
  - [x] `parseLines(content)` - Split and parse each line
  - [x] `repairJson(json)` - Fix truncated JSON
  - [x] `normalizeKeys(obj)` - Standardize key names

### Phase 2: Analysis Engine
- [x] Implement `js/analyzer.js`:
  - [x] `analyze(diagnostics, threshold)` - Main analysis entry
  - [x] `getRecursiveChildren(obj)` - Flatten hierarchy
  - [x] `extractNetworkInteractions(items)` - Get store stats
  - [x] `groupBy(items, keyFn)` - Generic grouping
  - [x] `computeTransportEventGroups(items)` - Phase analysis

### Phase 3: Report Generation
- [x] Implement `js/report-generator.js`:
  - [x] `generateSummary(result)` - Summary section
  - [x] `generateBucketsTable(buckets)` - Operation buckets
  - [x] `generateGroupedSection(groups)` - Drill-down tables
  - [x] `generateTransportSection(events)` - Phase details
  - [x] `generateJsonModal()` - JSON viewer modal

### Phase 4: Application UI
- [x] Implement `js/app.js`:
  - [x] File input handling (drag/drop + picker)
  - [x] Progress display
  - [x] Results rendering
  - [x] Export functionality
- [x] Complete `css/styles.css`:
  - [x] Upload area styles
  - [x] Table styles with sorting indicators
  - [x] Modal styles
  - [x] Responsive breakpoints

### Phase 5: Interactive Features
- [x] Add column sorting (click headers)
- [x] Add bucket drill-down (click to expand)
- [x] Add JSON viewer (modal with format/copy)
- [x] Add section collapse/expand
- [x] Add cell copy-on-click

### Phase 6: Testing & Polish
- [x] Test with sample data (CopilotDiagnostics.txt - 3107 lines)
- [x] Test truncated JSON repair
- [x] Test large file performance
- [x] Test exported HTML works standalone
- [x] Update README with deployment instructions

---

## ✅ Implementation Results

### Files Created

| File | Size | Description |
|------|------|-------------|
| `docs/index.html` | 3.9 KB | Main application page |
| `docs/css/styles.css` | 14 KB | Dark theme styles |
| `docs/js/echarts.min.js` | ~1 MB | ECharts library |
| `docs/js/xlsx.min.js` | ~944 KB | SheetJS Excel library |
| `docs/js/json-parser.js` | 6 KB | JSON parsing & repair |
| `docs/js/excel-parser.js` | 3 KB | Excel file parsing |
| `docs/js/analyzer.js` | 14 KB | Analysis engine |
| `docs/js/report-generator.js` | 23 KB | HTML report generation |
| `docs/js/timeline.js` | 8 KB | Timeline visualization |
| `docs/js/app.js` | 24 KB | Application logic |

### Features Implemented

- ✅ Drag-and-drop file upload with visual feedback
- ✅ **Excel file support** (.xlsx, .xls, .xlsb, .csv, .ods) - extracts diagnostics from column A
- ✅ Truncated JSON repair (10-iteration algorithm)
- ✅ Percentile metrics (P50, P75, P90, P95, P99)
- ✅ Operation bucketing with click-to-drill-down
- ✅ GroupBy ResourceType → OperationType
- ✅ GroupBy StatusCode → SubStatusCode  
- ✅ GroupBy LastTransportEvent with phase breakdown
- ✅ Endpoint statistics per phase
- ✅ Sortable tables (click headers)
- ✅ JSON viewer modal with copy/format
- ✅ **Timeline visualization** - Chrome-style waterfall in JSON modal
- ✅ **System Metrics Time Plot** - Interactive ECharts with CPU, Memory, Thread Wait, TCP
- ✅ **Client Configuration Time Plot** - Interactive ECharts with client metrics
- ✅ Self-contained HTML export
- ✅ Dark theme (LinqPad-inspired)
- ✅ Responsive design
- ✅ README updated with documentation

### To Deploy

```powershell
git add .
git commit -m "Fresh implementation of diagnostics analyzer"
git push origin main
```

Then enable GitHub Pages: Settings → Pages → Deploy from `/docs` on `main` branch.

---

## Notes

- Focus on reliability over performance initially
- Use requestAnimationFrame or setTimeout for UI responsiveness during large file processing
- Keep exported HTML under 5MB for practical sharing
- All processing client-side - emphasize privacy in UI

---

## Dependencies

| Library | Version | Size | Purpose |
|---------|---------|------|---------|
| ECharts | 5.x | ~1MB | Time-series charts (System Metrics, Client Config) |
| SheetJS | 0.20.1 | ~944KB | Excel file parsing (.xlsx, .xls, .xlsb, .csv, .ods) |

Both libraries are embedded locally (no CDN) for offline capability.
