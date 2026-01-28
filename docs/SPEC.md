# Cosmos Diagnostics Analyzer - Web App Implementation Plan

## ✅ STATUS: COMPLETED (2026-01-28)

### Bug Fixes
- **2026-01-28**: Fixed button showing "Analyzing..." on page load - CSS `[hidden]` attribute override
- **2026-01-28**: Added repaired/failed JSON counts to Parsing Statistics summary
- **2026-01-28**: JSON column now shows repair status (✓ Valid or 🔧 Repaired) for each entry
- **2026-01-28**: Modal close now scrolls back to originating table row with highlight flash

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
- Status codes, latencies, replica health, exceptions

---

## Technical Requirements

### Constraints
- **No build tools** - Pure HTML/CSS/JavaScript
- **No external dependencies** - Self-contained, prefer to leverage JS popular libraries don't re-invent
- **Client-side only** - Data never leaves browser
- **GitHub Pages compatible** - Static files in `/docs` folder
- **Single-file export** - Generate standalone HTML reports

### Browser Support
- Modern browsers (Chrome, Firefox, Edge, Safari)
- ES6+ JavaScript features

---

## Functional Specifications

### 1. File Input Module

| Feature | Specification |
|---------|--------------|
| Drag & drop | Drop zone with visual feedback |
| File picker | Click to browse, accept `.txt`, `.json`, `.log` |
| File info | Display filename and size |
| Large file handling | Process in chunks, show progress |
| LatencyThreshold | textbox input to accept integer type and use it to filter |

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

**Computed Metrics:**
- Per bucket: min/max duration, min/max network call count, total count, percentile P50, P75, P90, P95, P99 durations
- Per network interaction: duration, status codes, BE latency, transport timeline phases
- Per phase: percentile P50, P75, P90, P95, P99 durations, endpoint distribution, top 10 endpoints by frequency

### 4. Report Generator

| Section | Content |
|---------|---------|
| Summary | Total lines, successfully parsed, repaired (truncated JSON fixed), failed to parse, latency threshold, high-latency count, high-latency rate |
| Operation Buckets | Table with clickable drill-down |
| High Latency Network | Top 100 interactions (collapsible) |
| GroupBy ResourceType→OperationType | Grouped table with entries |
| GroupBy StatusCode→SubStatusCode | Grouped table with entries |
| GroupBy LastTransportEvent | With phase breakdown and endpoint stats |

**Table Features:**
- Sortable columns (click header to toggle asc/desc)
- Row numbers
- Clickable rows to show detail sections
- JSON viewer for raw data with repair status indicator (✓ Valid or 🔧 Repaired)

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

---

## File Structure

```
docs/
├── index.html              # Main application page
├── css/
│   └── styles.css          # Application styles
└── js/
    ├── json-parser.js      # JSON parsing and repair
    ├── analyzer.js         # Analysis engine
    ├── report-generator.js # HTML report generation
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
| `docs/js/json-parser.js` | 6 KB | JSON parsing & repair |
| `docs/js/analyzer.js` | 14 KB | Analysis engine |
| `docs/js/report-generator.js` | 23 KB | HTML report generation |
| `docs/js/app.js` | 24 KB | Application logic |

### Features Implemented

- ✅ Drag-and-drop file upload with visual feedback
- ✅ Truncated JSON repair (10-iteration algorithm)
- ✅ Percentile metrics (P50, P75, P90, P95, P99)
- ✅ Operation bucketing with click-to-drill-down
- ✅ GroupBy ResourceType → OperationType
- ✅ GroupBy StatusCode → SubStatusCode  
- ✅ GroupBy LastTransportEvent with phase breakdown
- ✅ Endpoint statistics per phase
- ✅ Sortable tables (click headers)
- ✅ JSON viewer modal with copy/format
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

None - pure HTML/CSS/JavaScript implementation.
