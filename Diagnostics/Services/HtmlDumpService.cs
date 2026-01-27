using System.Collections;
using System.Reflection;
using System.Text;
using Diagnostics.Models;

namespace Diagnostics.Services;

public class HtmlDumpService
{
    public string GenerateHtml(DiagnosticsResult result)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head>");
        sb.AppendLine("<title>Cosmos Diagnostics Analysis</title>");
        sb.AppendLine(GetStyles());
        sb.AppendLine("</head><body>");
        sb.AppendLine("<div class='container'>");
        
        sb.AppendLine("<h1>🔍 Cosmos Diagnostics Analysis</h1>");
        
        // Summary section
        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<h2>📊 Summary</h2>");
        sb.AppendLine(DumpTable("Parsing Statistics", new[]
        {
            new { Metric = "Total Entries", Value = result.TotalEntries },
            new { Metric = "Parsed Entries", Value = result.ParsedEntries },
            new { Metric = "Repaired Entries", Value = result.RepairedEntries },
            new { Metric = "High Latency Entries", Value = result.HighLatencyEntries }
        }));
        sb.AppendLine("</div>");
        
        // Operation Buckets
        if (result.OperationBuckets.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<h2>📦 Operation Buckets</h2>");
            sb.AppendLine("<p class='note'>Click on a bucket name to see related entries</p>");
            sb.AppendLine(DumpOperationBucketsTable(result.OperationBuckets));
            sb.AppendLine("</div>");
            
            // Hidden sections for each bucket's entries
            foreach (var bucket in result.OperationBuckets)
            {
                var bucketEntries = result.AllHighLatencyDiagnostics
                    .Where(e => e.Name == bucket.Bucket)
                    .Take(50)
                    .ToList();
                    
                var bucketId = GetSafeId(bucket.Bucket);
                sb.AppendLine($"<div id='bucket-{bucketId}' class='section bucket-details' style='display:none;'>");
                sb.AppendLine($"<h2>📋 Entries for: {System.Web.HttpUtility.HtmlEncode(bucket.Bucket)}</h2>");
                sb.AppendLine("<p class='note'>Click on column headers to sort</p>");
                sb.AppendLine($"<button class='btn-close' onclick=\"document.getElementById('bucket-{bucketId}').style.display='none'\">✕ Close</button>");
                sb.AppendLine(DumpTable($"Showing {bucketEntries.Count} of {bucket.Count} entries", bucketEntries, sortable: true, tableId: $"bucket-table-{bucketId}"));
                sb.AppendLine("</div>");
            }
        }
        
        // High Latency Network Interactions
        if (result.HighLatencyNetworkInteractions.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine($"<h2>🌐 High Latency Network Interactions (Top {Math.Min(100, result.HighLatencyNetworkInteractions.Count)})</h2>");
            sb.AppendLine(DumpTable("Network Interactions", result.HighLatencyNetworkInteractions.Take(20)));
            if (result.HighLatencyNetworkInteractions.Count > 20)
            {
                sb.AppendLine($"<p class='note'>Showing 20 of {result.HighLatencyNetworkInteractions.Count} interactions</p>");
            }
            sb.AppendLine("</div>");
        }
        
        // Resource Type Groups
        if (result.ResourceTypeGroups.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<h2>📁 GroupBy {ResourceType → OperationType}</h2>");
            sb.AppendLine(DumpTable("Resource Type Groups", result.ResourceTypeGroups));
            sb.AppendLine("</div>");
        }
        
        // Status Code Groups
        if (result.StatusCodeGroups.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<h2>🔢 GroupBy {StatusCode → SubStatusCode}</h2>");
            sb.AppendLine(DumpTable("Status Code Groups", result.StatusCodeGroups));
            sb.AppendLine("</div>");
        }
        
        // Transport Event Groups
        if (result.TransportEventGroups.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<h2>🚀 GroupBy LastTransportEvent</h2>");
            foreach (var group in result.TransportEventGroups)
            {
                sb.AppendLine($"<div class='subsection'>");
                sb.AppendLine($"<h3>{group.Status} ({group.Count} items)</h3>");
                if (group.PhaseDetails.Any())
                {
                    sb.AppendLine(DumpTable($"Phase Details", group.PhaseDetails.Select(p => new
                    {
                        p.Phase,
                        p.Count,
                        p.MinDuration,
                        p.MaxDuration,
                        p.EndpointCount
                    })));
                    
                    // Show top endpoints for each phase
                    foreach (var phase in group.PhaseDetails.Where(p => p.Top10Endpoints.Any()))
                    {
                        sb.AppendLine($"<details><summary>Top Endpoints for {phase.Phase ?? "Unknown"}</summary>");
                        sb.AppendLine(DumpTable("Endpoints", phase.Top10Endpoints));
                        sb.AppendLine("</details>");
                    }
                }
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div>");
        }
        
        sb.AppendLine("</div>");
        sb.AppendLine(GetScripts());
        sb.AppendLine("</body></html>");
        
        return sb.ToString();
    }

    private string DumpTable<T>(string title, IEnumerable<T> items, bool sortable = false, string? tableId = null)
    {
        var sb = new StringBuilder();
        var itemsList = items.ToList();
        
        if (!itemsList.Any())
        {
            sb.AppendLine($"<p class='empty'>No data for {title}</p>");
            return sb.ToString();
        }
        
        var id = tableId ?? $"table-{Guid.NewGuid():N}";
        sb.AppendLine($"<div class='dump-container'>");
        sb.AppendLine($"<div class='dump-header'>{title}</div>");
        sb.AppendLine($"<table class='dump-table{(sortable ? " sortable" : "")}' id='{id}'>");
        
        // Get properties
        var type = typeof(T);
        PropertyInfo[] properties;
        
        if (type.IsAnonymousType())
        {
            properties = type.GetProperties();
        }
        else
        {
            properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => !IsComplexType(p.PropertyType))
                .ToArray();
        }
        
        // Header
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th class='row-num'>#</th>");
        int colIndex = 1;
        foreach (var prop in properties)
        {
            if (sortable)
            {
                sb.AppendLine($"<th class='sortable' data-col='{colIndex}' onclick=\"sortTable('{id}', {colIndex})\">{FormatPropertyName(prop.Name)} <span class='sort-icon'>⇅</span></th>");
            }
            else
            {
                sb.AppendLine($"<th>{FormatPropertyName(prop.Name)}</th>");
            }
            colIndex++;
        }
        sb.AppendLine("</tr></thead>");
        
        // Body
        sb.AppendLine("<tbody>");
        int rowNum = 0;
        foreach (var item in itemsList)
        {
            rowNum++;
            sb.AppendLine($"<tr class='{(rowNum % 2 == 0 ? "even" : "odd")}'>");
            sb.AppendLine($"<td class='row-num'>{rowNum}</td>");
            foreach (var prop in properties)
            {
                var value = prop.GetValue(item);
                var sortValue = GetSortValue(value);
                sb.AppendLine($"<td data-sort='{sortValue}'>{FormatValue(value)}</td>");
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");
        
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");
        
        return sb.ToString();
    }

    private static string GetSortValue(object? value)
    {
        if (value == null) return "";
        if (value is double d) return d.ToString("F6");
        if (value is int i) return i.ToString("D10");
        if (value is DateTime dt) return dt.ToString("o");
        return System.Web.HttpUtility.HtmlEncode(value.ToString() ?? "");
    }

    private string DumpOperationBucketsTable(List<OperationBucket> buckets)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<div class='dump-container'>");
        sb.AppendLine("<div class='dump-header'>OperationName Buckets</div>");
        sb.AppendLine("<table class='dump-table'>");
        
        // Header
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th class='row-num'>#</th>");
        sb.AppendLine("<th>Bucket</th>");
        sb.AppendLine("<th>Min</th>");
        sb.AppendLine("<th>Max</th>");
        sb.AppendLine("<th>Min NW Count</th>");
        sb.AppendLine("<th>Max NW Count</th>");
        sb.AppendLine("<th>Count</th>");
        sb.AppendLine("</tr></thead>");
        
        // Body
        sb.AppendLine("<tbody>");
        int rowNum = 0;
        foreach (var bucket in buckets)
        {
            rowNum++;
            var bucketId = GetSafeId(bucket.Bucket);
            sb.AppendLine($"<tr class='{(rowNum % 2 == 0 ? "even" : "odd")}'>");
            sb.AppendLine($"<td class='row-num'>{rowNum}</td>");
            sb.AppendLine($"<td><a href='#' class='bucket-link' onclick=\"showBucket('{bucketId}'); return false;\">{System.Web.HttpUtility.HtmlEncode(bucket.Bucket)}</a></td>");
            sb.AppendLine($"<td><span class='number'>{bucket.Min:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{bucket.Max:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{bucket.MinNWCount:N0}</span></td>");
            sb.AppendLine($"<td><span class='number'>{bucket.MaxNWCount:N0}</span></td>");
            sb.AppendLine($"<td><span class='number'>{bucket.Count:N0}</span></td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");
        
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");
        
        return sb.ToString();
    }

    private static string GetSafeId(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "unknown";
        // Create a safe HTML id from the name
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name))
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static string FormatPropertyName(string name)
    {
        // Convert PascalCase to readable format
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && sb.Length > 0)
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string FormatValue(object? value)
    {
        if (value == null)
            return "<span class='null'>null</span>";
        
        if (value is double d)
            return $"<span class='number'>{d:F2}</span>";
        
        if (value is int i)
            return $"<span class='number'>{i:N0}</span>";
        
        if (value is DateTime dt)
            return $"<span class='date'>{dt:yyyy-MM-dd HH:mm:ss}</span>";
        
        if (value is Enum e)
            return $"<span class='enum'>{e}</span>";
        
        if (value is string s)
        {
            if (s.Length > 50)
                return $"<span class='string' title='{System.Web.HttpUtility.HtmlEncode(s)}'>{System.Web.HttpUtility.HtmlEncode(s[..47])}...</span>";
            return $"<span class='string'>{System.Web.HttpUtility.HtmlEncode(s)}</span>";
        }
        
        return System.Web.HttpUtility.HtmlEncode(value.ToString() ?? "");
    }

    private static bool IsComplexType(Type type)
    {
        if (type == typeof(string)) return false;
        if (type.IsPrimitive) return false;
        if (type.IsEnum) return false;
        if (type == typeof(DateTime)) return false;
        if (type == typeof(decimal)) return false;
        if (Nullable.GetUnderlyingType(type) != null)
            return IsComplexType(Nullable.GetUnderlyingType(type)!);
        if (typeof(IEnumerable).IsAssignableFrom(type)) return true;
        return type.IsClass && type != typeof(string);
    }

    private static string GetStyles()
    {
        return @"
<style>
:root {
    --bg-color: #1e1e1e;
    --text-color: #d4d4d4;
    --header-bg: #2d2d2d;
    --border-color: #3e3e3e;
    --accent-color: #569cd6;
    --number-color: #b5cea8;
    --string-color: #ce9178;
    --null-color: #808080;
    --enum-color: #4ec9b0;
    --date-color: #dcdcaa;
    --even-row: #252526;
    --odd-row: #1e1e1e;
    --hover-row: #094771;
}

* { box-sizing: border-box; }

body {
    font-family: 'Segoe UI', Consolas, monospace;
    background-color: var(--bg-color);
    color: var(--text-color);
    margin: 0;
    padding: 20px;
    line-height: 1.5;
}

.container {
    max-width: 1800px;
    margin: 0 auto;
}

h1 {
    color: var(--accent-color);
    border-bottom: 2px solid var(--accent-color);
    padding-bottom: 10px;
}

h2 {
    color: #9cdcfe;
    margin-top: 30px;
    margin-bottom: 15px;
}

h3 {
    color: #4ec9b0;
    margin: 10px 0;
}

.section {
    margin-bottom: 30px;
    padding: 20px;
    background: var(--header-bg);
    border-radius: 8px;
    border: 1px solid var(--border-color);
}

.subsection {
    margin: 15px 0;
    padding: 15px;
    background: var(--bg-color);
    border-radius: 4px;
}

.dump-container {
    margin: 10px 0;
    overflow-x: auto;
}

.dump-header {
    background: linear-gradient(135deg, #2d5a7b, #1e3a5f);
    color: #fff;
    padding: 8px 15px;
    font-weight: 600;
    border-radius: 4px 4px 0 0;
    font-size: 14px;
}

.dump-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 13px;
}

.dump-table th {
    background: var(--header-bg);
    color: var(--accent-color);
    padding: 10px 12px;
    text-align: left;
    border: 1px solid var(--border-color);
    font-weight: 600;
    white-space: nowrap;
}

.dump-table td {
    padding: 8px 12px;
    border: 1px solid var(--border-color);
    vertical-align: top;
}

.dump-table tr.even { background: var(--even-row); }
.dump-table tr.odd { background: var(--odd-row); }
.dump-table tr:hover { background: var(--hover-row); }

.row-num {
    color: #6a9955;
    font-size: 11px;
    text-align: center;
    width: 40px;
}

.number { color: var(--number-color); }
.string { color: var(--string-color); }
.null { color: var(--null-color); font-style: italic; }
.enum { color: var(--enum-color); }
.date { color: var(--date-color); }

.empty {
    color: var(--null-color);
    font-style: italic;
    padding: 10px;
}

.note {
    color: #6a9955;
    font-style: italic;
    margin-top: 10px;
}

details {
    margin: 10px 0;
    padding: 10px;
    background: var(--even-row);
    border-radius: 4px;
}

summary {
    cursor: pointer;
    color: var(--accent-color);
    font-weight: 500;
}

summary:hover {
    color: #7ec8e3;
}

/* Responsive */
@media (max-width: 768px) {
    body { padding: 10px; }
    .dump-table { font-size: 11px; }
    .dump-table th, .dump-table td { padding: 5px 8px; }
}

/* Clickable bucket links */
.bucket-link {
    color: #4fc3f7;
    text-decoration: none;
    font-weight: 500;
    cursor: pointer;
}

.bucket-link:hover {
    color: #81d4fa;
    text-decoration: underline;
}

/* Bucket details section */
.bucket-details {
    position: relative;
    border: 2px solid #4fc3f7;
    animation: slideIn 0.3s ease-out;
}

@keyframes slideIn {
    from { opacity: 0; transform: translateY(-10px); }
    to { opacity: 1; transform: translateY(0); }
}

.btn-close {
    position: absolute;
    top: 15px;
    right: 15px;
    background: #dc3545;
    color: white;
    border: none;
    padding: 5px 12px;
    border-radius: 4px;
    cursor: pointer;
    font-size: 14px;
}

.btn-close:hover {
    background: #c82333;
}

/* Sortable table headers */
.sortable th.sortable {
    cursor: pointer;
    user-select: none;
    position: relative;
    padding-right: 25px;
}

.sortable th.sortable:hover {
    background: #3a3d41;
}

.sort-icon {
    position: absolute;
    right: 8px;
    opacity: 0.4;
    font-size: 12px;
}

.sortable th.sortable.asc .sort-icon::after {
    content: '▲';
}

.sortable th.sortable.desc .sort-icon::after {
    content: '▼';
}

.sortable th.sortable.asc .sort-icon,
.sortable th.sortable.desc .sort-icon {
    opacity: 1;
    color: #4fc3f7;
}

.sortable th.sortable.asc .sort-icon,
.sortable th.sortable.desc .sort-icon {
    content: '';
}
</style>";
    }

    private static string GetScripts()
    {
        return @"
<script>
// Add click-to-copy for cell values (excluding links)
document.querySelectorAll('.dump-table td').forEach(cell => {
    if (!cell.querySelector('a')) {
        cell.addEventListener('click', function() {
            const text = this.innerText;
            navigator.clipboard.writeText(text).then(() => {
                const original = this.style.background;
                this.style.background = '#094771';
                setTimeout(() => this.style.background = original, 200);
            });
        });
        cell.style.cursor = 'pointer';
        cell.title = 'Click to copy';
    }
});

// Show bucket details
function showBucket(bucketId) {
    // Hide all bucket details first
    document.querySelectorAll('.bucket-details').forEach(el => {
        el.style.display = 'none';
    });
    
    // Show the selected bucket
    const bucketEl = document.getElementById('bucket-' + bucketId);
    if (bucketEl) {
        bucketEl.style.display = 'block';
        bucketEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
}

// Table sorting
function sortTable(tableId, colIndex) {
    const table = document.getElementById(tableId);
    if (!table) return;
    
    const tbody = table.querySelector('tbody');
    const rows = Array.from(tbody.querySelectorAll('tr'));
    const th = table.querySelectorAll('thead th')[colIndex];
    
    // Determine sort direction
    const isAsc = th.classList.contains('asc');
    const isDesc = th.classList.contains('desc');
    
    // Remove sort classes from all headers
    table.querySelectorAll('th.sortable').forEach(h => {
        h.classList.remove('asc', 'desc');
    });
    
    // Set new sort direction
    let direction = 1; // ascending
    if (isAsc) {
        th.classList.add('desc');
        direction = -1;
    } else {
        th.classList.add('asc');
        direction = 1;
    }
    
    // Sort rows
    rows.sort((a, b) => {
        const cellA = a.cells[colIndex];
        const cellB = b.cells[colIndex];
        
        // Get sort value from data attribute or text content
        let valA = cellA.getAttribute('data-sort') || cellA.innerText.trim();
        let valB = cellB.getAttribute('data-sort') || cellB.innerText.trim();
        
        // Try to parse as numbers
        const numA = parseFloat(valA);
        const numB = parseFloat(valB);
        
        if (!isNaN(numA) && !isNaN(numB)) {
            return (numA - numB) * direction;
        }
        
        // String comparison
        return valA.localeCompare(valB) * direction;
    });
    
    // Re-append sorted rows and update row numbers
    rows.forEach((row, index) => {
        row.cells[0].innerText = index + 1;
        row.className = (index + 1) % 2 === 0 ? 'even' : 'odd';
        tbody.appendChild(row);
    });
}
</script>";
    }
}

// Extension to check for anonymous types
public static class TypeExtensions
{
    public static bool IsAnonymousType(this Type type)
    {
        return type.Name.Contains("AnonymousType") 
            || (type.Name.StartsWith("<>") && type.Name.Contains("AnonymousType"));
    }
}
