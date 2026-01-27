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
  - Sortable columns
  - JSON drill-down for all grouped data
  
- **Web UI**: Drag & drop file upload interface

- **REST API**: Multiple endpoints for integration

## Project Structure

```
├── Diagnostics/              # ASP.NET Core Web API (standalone, .NET 10)
├── Diagnostics.Core/         # Shared library (models & services, .NET 8)
├── Diagnostics.Functions/    # Azure Functions App (.NET 8)
└── .github/workflows/        # CI/CD pipelines
```

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for Azure Functions)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for Web API)
- [Azure Functions Core Tools v4](https://docs.microsoft.com/azure/azure-functions/functions-run-local) (for local Functions development)
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli) (for deployment)
- [GitHub CLI](https://cli.github.com/) (optional, for repo creation)

### Running the Web Service (Standalone)

```powershell
dotnet run --project Diagnostics\Diagnostics.csproj
```

Then open `http://localhost:5000/` in your browser.

### Running Azure Functions Locally

```powershell
# Install Azure Functions Core Tools (if not installed)
# Option 1: Using npm
npm install -g azure-functions-core-tools@4

# Option 2: Using winget
winget install Microsoft.AzureFunctionsCoreTools

# Option 3: Using Chocolatey
choco install azure-functions-core-tools

# Navigate to Functions project
cd Diagnostics.Functions

# Start the function app
func start
```

Or with Visual Studio:
1. Right-click on `Diagnostics.Functions` project
2. Select **Set as Startup Project**
3. Press F5 to debug

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/diagnostics/analyze` | Upload file, get HTML report |
| POST | `/api/diagnostics/analyze/json` | Upload file, get JSON result |
| POST | `/api/diagnostics/analyze/text` | Send raw text body, get HTML |
| GET | `/api/health` | Health check (Functions only) |

### Query Parameters

- `latencyThreshold` (default: 600) - Latency threshold in milliseconds

### Example with curl (Windows)

```powershell
# Using curl.exe (built into Windows 10/11)
curl.exe -X POST "http://localhost:5000/api/diagnostics/analyze?latencyThreshold=600" `
  -F "file=@CopilotDiagnostics.txt" `
  -o result.html

# Or using Invoke-RestMethod (native PowerShell)
$form = @{ file = Get-Item -Path "CopilotDiagnostics.txt" }
Invoke-RestMethod -Uri "http://localhost:5000/api/diagnostics/analyze?latencyThreshold=600" `
  -Method Post -Form $form -OutFile result.html
```

---

## 🚀 Deployment Guide

### Step 1: Create GitHub Repository

#### Option A: Using GitHub CLI (Recommended)

```powershell
# Navigate to project directory
cd C:\Users\kirankk\source\repos\Diagnostics

# Login to GitHub (first time only)
gh auth login

# Create repository and push
gh repo create cosmos-diagnostics-analyzer --public --source=. --push
```

#### Option B: Using Git Commands

```powershell
# 1. Create a new repository on GitHub.com (https://github.com/new)
#    Name: cosmos-diagnostics-analyzer
#    Do NOT initialize with README

# 2. Add remote and push
cd C:\Users\kirankk\source\repos\Diagnostics
git remote add origin https://github.com/YOUR_USERNAME/cosmos-diagnostics-analyzer.git
git branch -M main
git push -u origin main
```

---

### Step 2: Create Azure Function App

#### Option A: Using Azure Portal (Easiest)

1. Go to [Azure Portal](https://portal.azure.com)
2. Click **Create a resource** → Search for **Function App**
3. Fill in the details:
   - **Subscription**: Select your subscription
   - **Resource Group**: Create new → `rg-cosmos-diagnostics`
   - **Function App name**: `func-cosmos-diagnostics-YOUR_UNIQUE_ID` (must be globally unique)
   - **Runtime stack**: `.NET`
   - **Version**: `8 (LTS), isolated worker model`
   - **Region**: Select nearest region (e.g., `East US`)
   - **Operating System**: `Windows`
   - **Hosting Plan**: `Consumption (Serverless)` (pay per execution)
4. Click **Review + create** → **Create**
5. Wait for deployment to complete (~2 minutes)

#### Option B: Using Azure CLI (PowerShell)

```powershell
# Login to Azure
az login

# Set variables (customize these - change YOUR_UNIQUE_ID to something unique like your initials + date)
$RESOURCE_GROUP = "rg-cosmos-diagnostics"
$LOCATION = "eastus"
$UNIQUE_ID = "YOUR_UNIQUE_ID"  # Change this! e.g., "kk20240115"
$STORAGE_ACCOUNT = "stcosmosdiag$UNIQUE_ID"  # Must be 3-24 chars, lowercase letters and numbers only
$FUNCTION_APP = "func-cosmos-diagnostics-$UNIQUE_ID"

# Create resource group
az group create --name $RESOURCE_GROUP --location $LOCATION

# Create storage account (required for Functions)
az storage account create `
  --name $STORAGE_ACCOUNT `
  --resource-group $RESOURCE_GROUP `
  --location $LOCATION `
  --sku Standard_LRS

# Create function app
az functionapp create `
  --name $FUNCTION_APP `
  --resource-group $RESOURCE_GROUP `
  --storage-account $STORAGE_ACCOUNT `
  --consumption-plan-location $LOCATION `
  --runtime dotnet-isolated `
  --runtime-version 8 `
  --functions-version 4 `
  --os-type Windows

# Print the function app URL
Write-Host "Function App URL: https://$FUNCTION_APP.azurewebsites.net"
```

---

### Step 3: Get Publish Profile from Azure

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to your Function App
3. In the left menu, click **Overview**
4. Click **Get publish profile** button (top toolbar)
5. A `.PublishSettings` file will download
6. Open the file in a text editor and **copy the entire content**

---

### Step 4: Configure GitHub Secrets

1. Go to your GitHub repository: `https://github.com/YOUR_USERNAME/cosmos-diagnostics-analyzer`
2. Click **Settings** tab
3. In left sidebar, click **Secrets and variables** → **Actions**
4. Click **New repository secret**
5. Fill in:
   - **Name**: `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`
   - **Secret**: Paste the entire content of the `.PublishSettings` file
6. Click **Add secret**

---

### Step 5: Update GitHub Actions Workflow

Edit `.github/workflows/azure-functions.yml` and update the function app name:

```yaml
env:
  AZURE_FUNCTIONAPP_NAME: 'func-cosmos-diagnostics-YOUR_UNIQUE_ID'  # <-- Change this!
```

Commit and push the change:

```powershell
git add .github/workflows/azure-functions.yml
git commit -m "Update function app name in workflow"
git push origin main
```

---

### Step 6: Verify Deployment

1. Go to GitHub repository → **Actions** tab
2. You should see a workflow running
3. Wait for it to complete (green checkmark)
4. Your Function App is now deployed!

#### Test the deployed function:

```powershell
# Health check
Invoke-RestMethod -Uri "https://YOUR_FUNCTION_APP.azurewebsites.net/api/health"

# Analyze a diagnostics file (using curl.exe on Windows)
curl.exe -X POST "https://YOUR_FUNCTION_APP.azurewebsites.net/api/diagnostics/analyze" `
  -F "file=@CopilotDiagnostics.txt" `
  -o result.html

# Or using Invoke-RestMethod
$form = @{ file = Get-Item -Path "CopilotDiagnostics.txt" }
Invoke-RestMethod -Uri "https://YOUR_FUNCTION_APP.azurewebsites.net/api/diagnostics/analyze" `
  -Method Post -Form $form -OutFile result.html
```

---

### Alternative: Deploy from Visual Studio

1. Open solution in Visual Studio
2. Right-click on `Diagnostics.Functions` project
3. Select **Publish**
4. Choose **Azure** → **Azure Function App (Windows)**
5. Sign in to Azure if prompted
6. Select your existing Function App or create new
7. Click **Finish** → **Publish**

---

## 🔐 Authentication Setup (Microsoft Employees Only)

The application is configured to only allow Microsoft employees (microsoft.com tenant) to access it.

### Step 1: Register an App in Azure AD

1. Go to [Azure Portal](https://portal.azure.com) → **Azure Active Directory** → **App registrations**
2. Click **New registration**
3. Fill in:
   - **Name**: `Cosmos Diagnostics Analyzer`
   - **Supported account types**: `Accounts in this organizational directory only (Microsoft only - Single tenant)`
   - **Redirect URI**: 
     - Platform: `Web`
     - URL: `https://YOUR_APP_URL/signin-oidc` (e.g., `https://localhost:5000/signin-oidc` for local dev)
4. Click **Register**
5. Copy the **Application (client) ID** - you'll need this

### Step 2: Configure the Application

#### For Web API (Diagnostics project)

Update `appsettings.json`:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "72f988bf-86f1-41af-91ab-2d7cd011db47",
    "ClientId": "YOUR_CLIENT_ID_HERE",
    "CallbackPath": "/signin-oidc"
  }
}
```

Or set environment variable:
```bash
export AzureAd__ClientId="YOUR_CLIENT_ID_HERE"
```

#### For Azure Functions (EasyAuth)

1. Go to your Function App in Azure Portal
2. Navigate to **Authentication** (left menu)
3. Click **Add identity provider**
4. Select **Microsoft**
5. Configure:
   - **App registration type**: `Pick an existing app registration in this directory`
   - **App registration**: Select or enter your app registration
   - **Tenant type**: `Workforce`
   - **Restrict access**: `Require authentication`
   - **Unauthenticated requests**: `HTTP 401 Unauthorized`
6. Click **Add**

### Step 3: Add Redirect URIs

In your App Registration → **Authentication** → **Redirect URIs**, add:
- `https://YOUR_APP_URL/signin-oidc` (Web API)
- `https://YOUR_FUNCTION_APP.azurewebsites.net/.auth/login/aad/callback` (Azure Functions)

---

## Troubleshooting

### Common Issues

| Issue | Solution |
|-------|----------|
| `func` command not found | Install Azure Functions Core Tools: `npm install -g azure-functions-core-tools@4` |
| Build fails on GitHub | Ensure .NET 8 SDK is used in workflow |
| 401 Unauthorized on Azure | Check publish profile secret is correct |
| Function timeout | Increase timeout in `host.json` or use Premium plan for large files |
| Authentication redirect loop | Ensure Redirect URI matches exactly in Azure AD app registration |
| "AADSTS50011" error | Add the correct redirect URI to your app registration |

### Logs

- **Local**: Console output when running `func start`
- **Azure Portal**: Function App → **Log stream** (left menu)
- **Application Insights**: Function App → **Application Insights** → **Logs**

---

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
