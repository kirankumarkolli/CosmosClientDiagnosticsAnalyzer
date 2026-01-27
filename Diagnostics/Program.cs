using Diagnostics.Services;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// Configure max request size for large diagnostic files (100MB)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100MB
});

// Add Microsoft Identity authentication (Microsoft employees only)
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        options.Instance = "https://login.microsoftonline.com/";
        options.TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47"; // Microsoft tenant ID
        options.ClientId = builder.Configuration["AzureAd:ClientId"] ?? "YOUR_CLIENT_ID"; // Set in appsettings.json or environment variable
        options.CallbackPath = "/signin-oidc";
    });

builder.Services.AddAuthorization(options =>
{
    // Require authenticated users by default
    options.FallbackPolicy = options.DefaultPolicy;
});

// Add services
builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Cosmos Diagnostics Analyzer", Version = "v1" });
});

// Configure form options for large file uploads
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100 * 1024 * 1024; // 100MB
});

builder.Services.AddScoped<DiagnosticsService>();
builder.Services.AddScoped<HtmlDumpService>();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cosmos Diagnostics Analyzer v1"));
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

// Serve upload page at root (requires authentication)
app.MapGet("/", () => Results.Content(GetUploadPage(), "text/html")).RequireAuthorization();

app.Run();

static string GetUploadPage() => """
<!DOCTYPE html>
<html>
<head>
    <title>Cosmos Diagnostics Analyzer</title>
    <style>
        :root {
            --bg-color: #1e1e1e;
            --text-color: #d4d4d4;
            --accent-color: #569cd6;
            --border-color: #3e3e3e;
        }
        * { box-sizing: border-box; }
        body {
            font-family: 'Segoe UI', sans-serif;
            background: var(--bg-color);
            color: var(--text-color);
            margin: 0;
            padding: 40px;
            min-height: 100vh;
        }
        .container {
            max-width: 800px;
            margin: 0 auto;
        }
        h1 {
            color: var(--accent-color);
            text-align: center;
            margin-bottom: 40px;
        }
        .upload-area {
            border: 2px dashed var(--border-color);
            border-radius: 12px;
            padding: 60px 40px;
            text-align: center;
            transition: all 0.3s;
            cursor: pointer;
        }
        .upload-area:hover, .upload-area.dragover {
            border-color: var(--accent-color);
            background: rgba(86, 156, 214, 0.1);
        }
        .upload-area h2 {
            margin: 0 0 10px;
            color: var(--accent-color);
        }
        .upload-area p {
            margin: 0;
            color: #808080;
        }
        input[type="file"] { display: none; }
        .options {
            margin-top: 30px;
            padding: 20px;
            background: #252526;
            border-radius: 8px;
        }
        .options label {
            display: block;
            margin-bottom: 10px;
            color: #9cdcfe;
        }
        .options input[type="number"] {
            background: var(--bg-color);
            border: 1px solid var(--border-color);
            color: var(--text-color);
            padding: 8px 12px;
            border-radius: 4px;
            width: 120px;
        }
        .btn {
            display: inline-block;
            margin-top: 20px;
            padding: 12px 30px;
            background: var(--accent-color);
            color: #fff;
            border: none;
            border-radius: 6px;
            cursor: pointer;
            font-size: 16px;
            transition: background 0.3s;
        }
        .btn:hover { background: #4a8bc2; }
        .btn:disabled { background: #555; cursor: not-allowed; }
        .file-name {
            margin-top: 15px;
            padding: 10px;
            background: #094771;
            border-radius: 4px;
            display: none;
        }
        .loading {
            display: none;
            margin-top: 20px;
            color: var(--accent-color);
        }
        .spinner {
            display: inline-block;
            width: 20px;
            height: 20px;
            border: 2px solid var(--accent-color);
            border-top-color: transparent;
            border-radius: 50%;
            animation: spin 1s linear infinite;
            margin-right: 10px;
            vertical-align: middle;
        }
        @keyframes spin { to { transform: rotate(360deg); } }
        .api-info {
            margin-top: 40px;
            padding: 20px;
            background: #252526;
            border-radius: 8px;
        }
        .api-info h3 { color: #4ec9b0; margin-top: 0; }
        .api-info code {
            background: var(--bg-color);
            padding: 2px 6px;
            border-radius: 3px;
            color: #ce9178;
        }
    </style>
</head>
<body>
    <div class="container">
        <h1>🔍 Cosmos Diagnostics Analyzer</h1>
        
        <form id="uploadForm" action="/api/diagnostics/analyze" method="post" enctype="multipart/form-data" target="_blank">
            <div class="upload-area" id="dropArea">
                <h2>📁 Drop your diagnostics file here</h2>
                <p>or click to browse</p>
                <input type="file" name="file" id="fileInput" accept=".txt,.json,.log">
                <div class="file-name" id="fileName"></div>
            </div>
            
            <div class="options">
                <label>
                    Latency Threshold (ms):
                    <input type="number" name="latencyThreshold" value="600" min="0">
                </label>
            </div>
            
            <button type="submit" class="btn" id="analyzeBtn" disabled>Analyze Diagnostics</button>
            
            <div class="loading" id="loading">
                <span class="spinner"></span>
                Analyzing diagnostics...
            </div>
        </form>
        
        <div class="api-info">
            <h3>📡 API Endpoints</h3>
            <p><strong>POST</strong> <code>/api/diagnostics/analyze</code> - Upload file, get HTML report</p>
            <p><strong>POST</strong> <code>/api/diagnostics/analyze/json</code> - Upload file, get JSON result</p>
            <p><strong>POST</strong> <code>/api/diagnostics/analyze/text</code> - Send raw text, get HTML report</p>
            <p><a href="/swagger" style="color: var(--accent-color);">View Swagger Documentation →</a></p>
        </div>
    </div>
    
    <script>
        const dropArea = document.getElementById('dropArea');
        const fileInput = document.getElementById('fileInput');
        const fileName = document.getElementById('fileName');
        const analyzeBtn = document.getElementById('analyzeBtn');
        const loading = document.getElementById('loading');
        const form = document.getElementById('uploadForm');
        
        dropArea.addEventListener('click', () => fileInput.click());
        
        ['dragenter', 'dragover'].forEach(e => {
            dropArea.addEventListener(e, (ev) => {
                ev.preventDefault();
                dropArea.classList.add('dragover');
            });
        });
        
        ['dragleave', 'drop'].forEach(e => {
            dropArea.addEventListener(e, (ev) => {
                ev.preventDefault();
                dropArea.classList.remove('dragover');
            });
        });
        
        dropArea.addEventListener('drop', (e) => {
            fileInput.files = e.dataTransfer.files;
            updateFileName();
        });
        
        fileInput.addEventListener('change', updateFileName);
        
        function updateFileName() {
            if (fileInput.files.length > 0) {
                fileName.textContent = '📄 ' + fileInput.files[0].name;
                fileName.style.display = 'block';
                analyzeBtn.disabled = false;
            }
        }
        
        form.addEventListener('submit', () => {
            loading.style.display = 'block';
            analyzeBtn.disabled = true;
        });
    </script>
</body>
</html>
""";
