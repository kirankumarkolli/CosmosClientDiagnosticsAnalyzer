# Cosmos Diagnostics Analyzer

A **100% client-side** web app that analyzes Azure Cosmos DB diagnostics logs. **Your data never leaves your browser!**

## Quick Start (Windows)

**Use Online:** Visit https://YOUR_USERNAME.github.io/cosmos-diagnostics-analyzer/

**Run Locally:**
```powershell
git clone https://github.com/YOUR_USERNAME/cosmos-diagnostics-analyzer.git
Start-Process "docs\index.html"
```

---

## Deploy to GitHub Pages (Windows)

### Step 1: Create GitHub Repository

**Option A - GitHub CLI:**
```powershell
cd C:\Users\kirankk\source\repos\Diagnostics
gh auth login
gh repo create cosmos-diagnostics-analyzer --public --source=. --push
```

**Option B - Manual:**
1. Go to https://github.com/new
2. Name: `cosmos-diagnostics-analyzer`
3. Do NOT initialize with README
4. Click Create repository
5. Run in PowerShell:
```powershell
cd C:\Users\kirankk\source\repos\Diagnostics
git remote add origin https://github.com/YOUR_USERNAME/cosmos-diagnostics-analyzer.git
git branch -M main
git push -u origin main
```

### Step 2: Enable GitHub Pages

1. Go to your repository: `https://github.com/YOUR_USERNAME/cosmos-diagnostics-analyzer`
2. Click **Settings** (top menu)
3. Click **Pages** (left sidebar)
4. Under **Build and deployment**:
   - Source: `Deploy from a branch`
   - Branch: `main`
   - Folder: `/docs`
5. Click **Save**
6. Wait 1-2 minutes

### Step 3: Verify Deployment

```powershell
Start-Process "https://YOUR_USERNAME.github.io/cosmos-diagnostics-analyzer/"
```

---

## Features

- **Truncated JSON Repair** - Automatically fixes incomplete JSON
- **Dark Theme** - LinqPad-style output with syntax highlighting
- **Sortable Tables** - Click headers to sort any column
- **JSON Drill-down** - View raw JSON for any entry
- **Privacy First** - All processing in browser, no server
- **Export Reports** - Download standalone HTML files

---

## Project Structure

```
docs/                         <- GitHub Pages source
??? index.html               <- Main page
??? css/
?   ??? styles.css           <- Dark theme
??? js/
    ??? app.js               <- Application logic
    ??? diagnostics-parser.js <- JSON parsing & repair
    ??? html-generator.js    <- Report generation
```

---

## How to Use

1. Open the web page (online or locally)
2. Drag & drop your diagnostics file (or click to browse)
3. Set latency threshold (default: 600ms)
4. Click "Analyze Diagnostics"
5. Explore results:
   - Click bucket names to drill down
   - Click column headers to sort
   - Click "View JSON" to see raw data
6. Download report as standalone HTML

---

## Optional: Server Version

For Azure Functions deployment, see the `/Diagnostics.Functions` project.

```powershell
cd Diagnostics.Functions
func start
```

---

## License

MIT
