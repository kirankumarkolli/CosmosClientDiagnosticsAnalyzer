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

## Project Structure

```
├── Diagnostics/              # ASP.NET Core Web API (standalone)
├── Diagnostics.Core/         # Shared library (models & services)
├── Diagnostics.Functions/    # Azure Functions App
└── .github/workflows/        # CI/CD pipelines
```

## Getting Started

### Prerequisites

- .NET 8 SDK (for Azure Functions)
- .NET 10 SDK (for Web API)
- Azure Functions Core Tools (for local Functions development)

### Running the Web Service (Standalone)

```bash
dotnet run --project Diagnostics/Diagnostics.csproj
```

Then open `http://localhost:5000/` in your browser.

### Running Azure Functions Locally

```bash
cd Diagnostics.Functions
func start
```

Or with Visual Studio, set `Diagnostics.Functions` as startup project.

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/diagnostics/analyze` | Upload file, get HTML report |
| POST | `/api/diagnostics/analyze/json` | Upload file, get JSON result |
| POST | `/api/diagnostics/analyze/text` | Send raw text body, get HTML |
| GET | `/api/health` | Health check (Functions only) |

### Query Parameters

- `latencyThreshold` (default: 600) - Latency threshold in milliseconds

### Example with curl

```bash
curl -X POST "http://localhost:5000/api/diagnostics/analyze?latencyThreshold=600" \
  -F "file=@CopilotDiagnostics.txt" \
  -o result.html
```

## Deployment

### Deploy to Azure Functions

#### Option 1: GitHub Actions (Recommended)

1. Create an Azure Function App in the Azure Portal
2. Download the Publish Profile from the Function App
3. Add the publish profile as a GitHub secret named `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`
4. Update `.github/workflows/azure-functions.yml` with your function app name
5. Push to `main` branch to trigger deployment

#### Option 2: Azure CLI

```bash
# Login to Azure
az login

# Create resource group
az group create --name rg-diagnostics --location eastus

# Create storage account
az storage account create --name stdiagnostics --resource-group rg-diagnostics --sku Standard_LRS

# Create function app
az functionapp create \
  --name func-diagnostics \
  --resource-group rg-diagnostics \
  --storage-account stdiagnostics \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --runtime-version 8 \
  --functions-version 4

# Deploy
cd Diagnostics.Functions
func azure functionapp publish func-diagnostics
```

#### Option 3: Visual Studio

1. Right-click on `Diagnostics.Functions` project
2. Select **Publish**
3. Choose **Azure** → **Azure Function App (Windows)**
4. Select or create a Function App
5. Click **Publish**

### GitHub Repository Setup

```bash
# Create GitHub repo (using GitHub CLI)
gh repo create cosmos-diagnostics-analyzer --public --source=. --push

# Or manually:
git remote add origin https://github.com/YOUR_USERNAME/cosmos-diagnostics-analyzer.git
git branch -M main
git push -u origin main
```

## Analysis Output

The analysis produces:

1. **Summary Statistics**
   - Total/Parsed/Repaired entries count
   - High latency entries count

2. **Operation Buckets**
   - Grouped by operation name
   - Min/Max duration and network call counts
   - Click to drill-down with JSON

3. **Network Interactions**
   - High latency network calls details
   - Transport timeline events

4. **Grouped Analysis**
   - By ResourceType → OperationType
   - By StatusCode → SubStatusCode
   - By LastTransportEvent with phase details
   - All with JSON drill-down

## License

MIT
