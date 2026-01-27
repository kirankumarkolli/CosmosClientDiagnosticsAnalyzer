using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Diagnostics.Core.Services;

namespace Diagnostics.Functions;

public class DiagnosticsFunction
{
    private readonly ILogger<DiagnosticsFunction> _logger;
    private readonly DiagnosticsService _diagnosticsService;
    private readonly HtmlDumpService _htmlDumpService;

    public DiagnosticsFunction(
        ILogger<DiagnosticsFunction> logger,
        DiagnosticsService diagnosticsService,
        HtmlDumpService htmlDumpService)
    {
        _logger = logger;
        _diagnosticsService = diagnosticsService;
        _htmlDumpService = htmlDumpService;
    }

    /// <summary>
    /// Analyze diagnostics file and return HTML report
    /// POST /api/diagnostics/analyze
    /// </summary>
    [Function("AnalyzeDiagnostics")]
    public async Task<IActionResult> AnalyzeFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "diagnostics/analyze")] HttpRequest req)
    {
        _logger.LogInformation("Processing diagnostics file upload");

        if (!req.HasFormContentType)
        {
            return new BadRequestObjectResult("Please upload a file using multipart/form-data");
        }

        var form = await req.ReadFormAsync();
        var file = form.Files.GetFile("file");

        if (file == null || file.Length == 0)
        {
            return new BadRequestObjectResult("Please provide a diagnostics file");
        }

        // Get latency threshold from query string
        int latencyThreshold = 600;
        if (req.Query.TryGetValue("latencyThreshold", out var thresholdValue) && 
            int.TryParse(thresholdValue, out var parsed))
        {
            latencyThreshold = parsed;
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();

        var result = _diagnosticsService.AnalyzeDiagnostics(content, latencyThreshold);
        var html = _htmlDumpService.GenerateHtml(result);

        return new ContentResult
        {
            Content = html,
            ContentType = "text/html",
            StatusCode = 200
        };
    }

    /// <summary>
    /// Analyze diagnostics file and return JSON result
    /// POST /api/diagnostics/analyze/json
    /// </summary>
    [Function("AnalyzeDiagnosticsJson")]
    public async Task<IActionResult> AnalyzeFileJson(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "diagnostics/analyze/json")] HttpRequest req)
    {
        _logger.LogInformation("Processing diagnostics file upload (JSON response)");

        if (!req.HasFormContentType)
        {
            return new BadRequestObjectResult("Please upload a file using multipart/form-data");
        }

        var form = await req.ReadFormAsync();
        var file = form.Files.GetFile("file");

        if (file == null || file.Length == 0)
        {
            return new BadRequestObjectResult("Please provide a diagnostics file");
        }

        int latencyThreshold = 600;
        if (req.Query.TryGetValue("latencyThreshold", out var thresholdValue) && 
            int.TryParse(thresholdValue, out var parsed))
        {
            latencyThreshold = parsed;
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();

        var result = _diagnosticsService.AnalyzeDiagnostics(content, latencyThreshold);

        return new OkObjectResult(result);
    }

    /// <summary>
    /// Analyze raw text content and return HTML report
    /// POST /api/diagnostics/analyze/text
    /// </summary>
    [Function("AnalyzeDiagnosticsText")]
    public async Task<IActionResult> AnalyzeText(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "diagnostics/analyze/text")] HttpRequest req)
    {
        _logger.LogInformation("Processing raw diagnostics text");

        int latencyThreshold = 600;
        if (req.Query.TryGetValue("latencyThreshold", out var thresholdValue) && 
            int.TryParse(thresholdValue, out var parsed))
        {
            latencyThreshold = parsed;
        }

        using var reader = new StreamReader(req.Body);
        var content = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(content))
        {
            return new BadRequestObjectResult("Please provide diagnostics content in the request body");
        }

        var result = _diagnosticsService.AnalyzeDiagnostics(content, latencyThreshold);
        var html = _htmlDumpService.GenerateHtml(result);

        return new ContentResult
        {
            Content = html,
            ContentType = "text/html",
            StatusCode = 200
        };
    }

    /// <summary>
    /// Health check endpoint
    /// GET /api/health
    /// </summary>
    [Function("HealthCheck")]
    public IActionResult HealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
    {
        return new OkObjectResult(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
