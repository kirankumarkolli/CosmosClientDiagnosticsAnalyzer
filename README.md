# Cosmos Diagnostics Analyzer

A web service that analyzes Azure Cosmos DB diagnostics logs and produces LinqPad-style HTML reports.

## Features

- **Truncated JSON Repair**: Automatically repairs truncated JSON entries from log files
  - Handles `...` truncation markers
  - Properly closes unclosed braces/brackets in LIFO order
  - Handles unclosed strings and incomplete properties
  
- **LinqPad-style HTML Output**: Dark-themed tables with:
  - Click-to-copy cell values
  - Collapsible sections for nested data
  - Syntax highlighting for different data types
  
- **Web UI**: Drag & drop file upload interface

- **REST API**: Multiple endpoints for integration

## Getting Started

### Prerequisites

- .NET 10 SDK

### Running the Service

```bash
dotnet run --project Diagnostics/Diagnostics.csproj
```

Then open `http://localhost:5000/` in your browser.

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/diagnostics/analyze` | Upload file, get HTML report |
| POST | `/api/diagnostics/analyze/json` | Upload file, get JSON result |
| POST | `/api/diagnostics/analyze/text` | Send raw text body, get HTML |

### Query Parameters

- `latencyThreshold` (default: 600) - Latency threshold in milliseconds

### Example with curl

```bash
curl -X POST "http://localhost:5000/api/diagnostics/analyze?latencyThreshold=600" \
  -F "file=@CopilotDiagnostics.txt" \
  -o result.html
```

## Project Structure

```
Diagnostics/
├── Controllers/
│   └── DiagnosticsController.cs    # API endpoints
├── Models/
│   ├── CosmosDiagnosticsModels.cs  # Cosmos DB data models
│   └── DiagnosticsResult.cs        # Analysis result models
├── Services/
│   ├── DiagnosticsService.cs       # JSON parsing & analysis
│   └── HtmlDumpService.cs          # HTML generation
└── Program.cs                      # Web host setup
```

## Analysis Output

The analysis produces:

1. **Summary Statistics**
   - Total/Parsed/Repaired entries count
   - High latency entries count

2. **Operation Buckets**
   - Grouped by operation name
   - Min/Max duration and network call counts

3. **Network Interactions**
   - High latency network calls details
   - Transport timeline events

4. **Grouped Analysis**
   - By ResourceType → OperationType
   - By StatusCode → SubStatusCode
   - By LastTransportEvent with phase details

## License

MIT
