using Diagnostics.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Diagnostics.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Require Microsoft authentication
public class DiagnosticsController : ControllerBase
{
    private readonly DiagnosticsService _diagnosticsService;
    private readonly HtmlDumpService _htmlDumpService;

    public DiagnosticsController(DiagnosticsService diagnosticsService, HtmlDumpService htmlDumpService)
    {
        _diagnosticsService = diagnosticsService;
        _htmlDumpService = htmlDumpService;
    }

    /// <summary>
    /// Analyze diagnostics file and return HTML report (LinqPad Dump style)
    /// </summary>
    [HttpPost("analyze")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100MB
    [RequestFormLimits(MultipartBodyLengthLimit = 100 * 1024 * 1024)]
    public async Task<IActionResult> AnalyzeFile(
        IFormFile file, 
        [FromQuery] int latencyThreshold = 600)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Please provide a diagnostics file");
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();

        var result = _diagnosticsService.AnalyzeDiagnostics(content, latencyThreshold);
        var html = _htmlDumpService.GenerateHtml(result);

        return Content(html, "text/html");
    }

    /// <summary>
    /// Analyze diagnostics file and return JSON result
    /// </summary>
    [HttpPost("analyze/json")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    public async Task<IActionResult> AnalyzeFileJson(
        IFormFile file, 
        [FromQuery] int latencyThreshold = 600)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Please provide a diagnostics file");
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();

        var result = _diagnosticsService.AnalyzeDiagnostics(content, latencyThreshold);
        return Ok(result);
    }

    /// <summary>
    /// Analyze diagnostics from raw text body
    /// </summary>
    [HttpPost("analyze/text")]
    [Consumes("text/plain")]
    public IActionResult AnalyzeText(
        [FromBody] string content, 
        [FromQuery] int latencyThreshold = 600)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return BadRequest("Please provide diagnostics content");
        }

        var result = _diagnosticsService.AnalyzeDiagnostics(content, latencyThreshold);
        var html = _htmlDumpService.GenerateHtml(result);

        return Content(html, "text/html");
    }
}
