using System.Collections;
using System.Reflection;
using System.Text;
using Diagnostics.Core.Models;

namespace Diagnostics.Core.Services;

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
        
        // High Latency Network Interactions (collapsible, hidden by default)
        if (result.HighLatencyNetworkInteractions.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine($"<div class='section-header collapsible' onclick=\"toggleSection('nwInteractions')\">");
            sb.AppendLine($"<h2>🌐 High Latency Network Interactions (Top {Math.Min(100, result.HighLatencyNetworkInteractions.Count)})</h2>");
            sb.AppendLine("<span class='collapse-icon' id='nwInteractions-icon'>▶</span>");
            sb.AppendLine("</div>");
            sb.AppendLine("<div id='nwInteractions' class='section-content' style='display:none;'>");
            sb.AppendLine(DumpTable("Network Interactions", result.HighLatencyNetworkInteractions.Take(20)));
            if (result.HighLatencyNetworkInteractions.Count > 20)
            {
                sb.AppendLine($"<p class='note'>Showing 20 of {result.HighLatencyNetworkInteractions.Count} interactions</p>");
            }
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");
        }
        
        // Resource Type Groups
        if (result.ResourceTypeGroups.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<h2>📁 GroupBy {ResourceType → OperationType}</h2>");
            sb.AppendLine("<p class='note'>Click on a row to see all entries, or click on a percentile value to see entries at that level</p>");
            sb.AppendLine(DumpGroupedResultTable("Resource Type Groups", result.ResourceTypeGroups, "resourceType"));
            sb.AppendLine("</div>");
            
            // Hidden sections for each group's entries
            foreach (var group in result.ResourceTypeGroups)
            {
                var groupId = GetSafeId($"resourceType-{group.Key}");
                sb.AppendLine($"<div id='group-{groupId}' class='section bucket-details' style='display:none;'>");
                sb.AppendLine($"<h2>📋 Entries for: {System.Web.HttpUtility.HtmlEncode(group.Key)}</h2>");
                sb.AppendLine($"<button class='btn-close' onclick=\"document.getElementById('group-{groupId}').style.display='none'\">✕ Close</button>");
                sb.AppendLine(DumpTable($"Showing {group.Entries.Count} of {group.Count} entries", group.Entries, sortable: true, tableId: $"group-table-{groupId}"));
                sb.AppendLine("</div>");
                
                // Hidden sections for percentile entries
                sb.AppendLine(CreatePercentileSection(group, "resourceType", "P50", group.P50, group.EntriesAtP50));
                sb.AppendLine(CreatePercentileSection(group, "resourceType", "P75", group.P75, group.EntriesAtP75));
                sb.AppendLine(CreatePercentileSection(group, "resourceType", "P90", group.P90, group.EntriesAtP90));
                sb.AppendLine(CreatePercentileSection(group, "resourceType", "P95", group.P95, group.EntriesAtP95));
            }
        }
        
        // Status Code Groups
        if (result.StatusCodeGroups.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<h2>🔢 GroupBy {StatusCode → SubStatusCode}</h2>");
            sb.AppendLine("<p class='note'>Click on a row to see all entries, or click on a percentile value to see entries at that level</p>");
            sb.AppendLine(DumpGroupedResultTable("Status Code Groups", result.StatusCodeGroups, "statusCode"));
            sb.AppendLine("</div>");
            
            
            // Hidden sections for each group's entries
            foreach (var group in result.StatusCodeGroups)
            {
                var groupId = GetSafeId($"statusCode-{group.Key}");
                sb.AppendLine($"<div id='group-{groupId}' class='section bucket-details' style='display:none;'>");
                sb.AppendLine($"<h2>📋 Entries for: {System.Web.HttpUtility.HtmlEncode(group.Key)}</h2>");
                sb.AppendLine($"<button class='btn-close' onclick=\"document.getElementById('group-{groupId}').style.display='none'\">✕ Close</button>");
                sb.AppendLine(DumpTable($"Showing {group.Entries.Count} of {group.Count} entries", group.Entries, sortable: true, tableId: $"group-table-{groupId}"));
                sb.AppendLine("</div>");
                
                // Hidden sections for percentile entries
                sb.AppendLine(CreatePercentileSection(group, "statusCode", "P50", group.P50, group.EntriesAtP50));
                sb.AppendLine(CreatePercentileSection(group, "statusCode", "P75", group.P75, group.EntriesAtP75));
                sb.AppendLine(CreatePercentileSection(group, "statusCode", "P90", group.P90, group.EntriesAtP90));
                sb.AppendLine(CreatePercentileSection(group, "statusCode", "P95", group.P95, group.EntriesAtP95));
            }
        }
        
        // Transport Exception Groups
        if (result.TransportExceptionGroups.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<h2>⚠️ GroupBy TransportException</h2>");
            sb.AppendLine("<p class='note'>Click on a row to see related entries with JSON</p>");
            sb.AppendLine(DumpTransportExceptionTable("Transport Exception Groups", result.TransportExceptionGroups));
            sb.AppendLine("</div>");
            
            // Hidden sections for each group's entries
            foreach (var group in result.TransportExceptionGroups)
            {
                var groupId = GetSafeId($"transportException-{group.Key}");
                sb.AppendLine($"<div id='group-{groupId}' class='section bucket-details' style='display:none;'>");
                sb.AppendLine($"<h2>📋 Entries for: {System.Web.HttpUtility.HtmlEncode(group.Key)}</h2>");
                sb.AppendLine($"<button class='btn-close' onclick=\"document.getElementById('group-{groupId}').style.display='none'\">✕ Close</button>");
                sb.AppendLine(DumpTable($"Showing {group.Entries.Count} of {group.Count} entries", group.Entries, sortable: true, tableId: $"group-table-{groupId}"));
                sb.AppendLine("</div>");
            }
        }
        
        // Transport Event Groups
        if (result.TransportEventGroups.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<h2>🚀 GroupBy LastTransportEvent</h2>");
            sb.AppendLine("<p class='note'>Click on an event to see related entries with JSON</p>");
            
            // Summary table with percentiles
            sb.AppendLine(DumpTransportEventSummaryTable(result.TransportEventGroups));
            
            foreach (var group in result.TransportEventGroups)
            {
                var groupId = GetSafeId($"transport-{group.Status}");
                sb.AppendLine($"<div class='subsection'>");
                sb.AppendLine($"<h3 class='clickable-header' onclick=\"showGroup('{groupId}')\">{group.Status} ({group.Count} items) <span class='click-hint'>👆 click to view entries</span></h3>");
                if (group.PhaseDetails.Any())
                {
                    sb.AppendLine(DumpPhaseDetailsTable("Phase Details", group.PhaseDetails, group.Status.ToString()));
                    
                    // Show top endpoints for each phase
                    foreach (var phase in group.PhaseDetails.Where(p => p.Top10Endpoints.Any()))
                    {
                        sb.AppendLine($"<details><summary>Top Endpoints for {phase.Phase ?? "Unknown"}</summary>");
                        sb.AppendLine(DumpTable("Endpoints", phase.Top10Endpoints));
                        sb.AppendLine("</details>");
                    }
                }
                sb.AppendLine("</div>");
                
                // Hidden sections for phase entries
                foreach (var phase in group.PhaseDetails.Where(p => p.Entries.Any()))
                {
                    var phaseId = GetSafeId($"phase-{group.Status}-{phase.Phase}");
                    sb.AppendLine($"<div id='group-{phaseId}' class='section bucket-details' style='display:none;'>");
                    sb.AppendLine($"<h2>📋 Entries for Phase: {phase.Phase ?? "Unknown"}</h2>");
                    sb.AppendLine($"<button class='btn-close' onclick=\"document.getElementById('group-{phaseId}').style.display='none'\">✕ Close</button>");
                    sb.AppendLine(DumpTable($"Showing {phase.Entries.Count} of {phase.Count} entries", phase.Entries, sortable: true, tableId: $"phase-table-{phaseId}"));
                    sb.AppendLine("</div>");
                }
            }
            sb.AppendLine("</div>");
            
            // Hidden sections for transport event entries
            foreach (var group in result.TransportEventGroups)
            {
                var groupId = GetSafeId($"transport-{group.Status}");
                sb.AppendLine($"<div id='group-{groupId}' class='section bucket-details' style='display:none;'>");
                sb.AppendLine($"<h2>📋 Entries for: {group.Status}</h2>");
                sb.AppendLine($"<button class='btn-close' onclick=\"document.getElementById('group-{groupId}').style.display='none'\">✕ Close</button>");
                sb.AppendLine(DumpTable($"Showing {group.Entries.Count} of {group.Count} entries", group.Entries, sortable: true, tableId: $"group-table-{groupId}"));
                sb.AppendLine("</div>");
            }
        }
        
        // JSON Modal
        sb.AppendLine(@"
<div id='jsonModal' class='modal' onclick='closeJsonModal(event)'>
    <div class='modal-content' onclick='event.stopPropagation()'>
        <div class='modal-header'>
            <h3>📄 JSON Content</h3>
            <button class='modal-close' onclick='closeJsonModal()'>&times;</button>
        </div>
        <div class='modal-actions'>
            <button class='btn-copy' onclick='copyJsonContent()'>📋 Copy to Clipboard</button>
            <button class='btn-format' onclick='formatJson()'>🔧 Format JSON</button>
        </div>
        <pre id='jsonModalContent' class='json-display'></pre>
    </div>
</div>");
        
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
                
                // Special handling for RawJson property
                if (prop.Name == "RawJson" && value is string jsonStr && !string.IsNullOrEmpty(jsonStr))
                {
                    var jsonId = $"json-{Guid.NewGuid():N}";
                    sb.AppendLine($"<td data-sort='{jsonStr.Length}'>");
                    sb.AppendLine($"<button class='btn-json' onclick=\"showJson('{jsonId}')\">📄 View JSON ({jsonStr.Length:N0} chars)</button>");
                    sb.AppendLine($"<div id='{jsonId}' class='json-content' style='display:none;'>{System.Web.HttpUtility.HtmlEncode(jsonStr)}</div>");
                    sb.AppendLine("</td>");
                }
                else
                {
                    sb.AppendLine($"<td data-sort='{sortValue}'>{FormatValue(value)}</td>");
                }
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
        sb.AppendLine("<th>P50</th>");
        sb.AppendLine("<th>P75</th>");
        sb.AppendLine("<th>P90</th>");
        sb.AppendLine("<th>P95</th>");
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
            sb.AppendLine($"<td><span class='number'>{bucket.P50:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{bucket.P75:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{bucket.P90:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{bucket.P95:F2}</span></td>");
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

    private string DumpGroupedResultTable(string title, List<GroupedResult> groups, string prefix)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<div class='dump-container'>");
        sb.AppendLine($"<div class='dump-header'>{title}</div>");
        sb.AppendLine("<table class='dump-table'>");
        
        
        // Header
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th class='row-num'>#</th>");
        sb.AppendLine("<th>Key</th>");
        sb.AppendLine("<th>Count</th>");
        sb.AppendLine("<th>Min</th>");
        sb.AppendLine("<th>P50</th>");
        sb.AppendLine("<th>P75</th>");
        sb.AppendLine("<th>P90</th>");
        sb.AppendLine("<th>P95</th>");
        sb.AppendLine("<th>Max</th>");
        sb.AppendLine("<th>Action</th>");
        sb.AppendLine("</tr></thead>");
        
        // Body
        sb.AppendLine("<tbody>");
        int rowNum = 0;
        foreach (var group in groups)
        {
            rowNum++;
            var groupId = GetSafeId($"{prefix}-{group.Key}");
            var p50Id = GetSafeId($"{prefix}-{group.Key}-p50");
            var p75Id = GetSafeId($"{prefix}-{group.Key}-p75");
            var p90Id = GetSafeId($"{prefix}-{group.Key}-p90");
            var p95Id = GetSafeId($"{prefix}-{group.Key}-p95");
            sb.AppendLine($"<tr class='{(rowNum % 2 == 0 ? "even" : "odd")} clickable-row' onclick=\"showGroup('{groupId}')\">");
            sb.AppendLine($"<td class='row-num'>{rowNum}</td>");
            sb.AppendLine($"<td><span class='string'>{System.Web.HttpUtility.HtmlEncode(group.Key)}</span></td>");
            sb.AppendLine($"<td><span class='number'>{group.Count:N0}</span></td>");
            sb.AppendLine($"<td><span class='number'>{group.Min:F2}</span></td>");
            sb.AppendLine($"<td class='clickable-cell' onclick=\"event.stopPropagation(); showGroup('{p50Id}')\"><span class='number percentile-link'>{group.P50:F2}</span></td>");
            sb.AppendLine($"<td class='clickable-cell' onclick=\"event.stopPropagation(); showGroup('{p75Id}')\"><span class='number percentile-link'>{group.P75:F2}</span></td>");
            sb.AppendLine($"<td class='clickable-cell' onclick=\"event.stopPropagation(); showGroup('{p90Id}')\"><span class='number percentile-link'>{group.P90:F2}</span></td>");
            sb.AppendLine($"<td class='clickable-cell' onclick=\"event.stopPropagation(); showGroup('{p95Id}')\"><span class='number percentile-link'>{group.P95:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{group.Max:F2}</span></td>");
            sb.AppendLine($"<td><button class='btn-view' onclick=\"event.stopPropagation(); showGroup('{groupId}')\">📄 View Entries</button></td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");
        
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");
        
        return sb.ToString();
    }

    private string DumpTransportExceptionTable(string title, List<GroupedResult> groups)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<div class='dump-container'>");
        sb.AppendLine($"<div class='dump-header'>{title}</div>");
        sb.AppendLine("<table class='dump-table'>");
        
        // Header (without percentiles)
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th class='row-num'>#</th>");
        sb.AppendLine("<th>Exception Message</th>");
        sb.AppendLine("<th>Count</th>");
        sb.AppendLine("<th>Action</th>");
        sb.AppendLine("</tr></thead>");
        
        // Body
        sb.AppendLine("<tbody>");
        int rowNum = 0;
        foreach (var group in groups)
        {
            rowNum++;
            var groupId = GetSafeId($"transportException-{group.Key}");
            sb.AppendLine($"<tr class='{(rowNum % 2 == 0 ? "even" : "odd")} clickable-row' onclick=\"showGroup('{groupId}')\">");
            sb.AppendLine($"<td class='row-num'>{rowNum}</td>");
            sb.AppendLine($"<td><span class='string'>{System.Web.HttpUtility.HtmlEncode(group.Key)}</span></td>");
            sb.AppendLine($"<td><span class='number'>{group.Count:N0}</span></td>");
            sb.AppendLine($"<td><button class='btn-view' onclick=\"event.stopPropagation(); showGroup('{groupId}')\">📄 View Entries</button></td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");
        
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");
        
        return sb.ToString();
    }

    private string DumpPhaseDetailsTable(string title, List<PhaseDetail> phases, string transportEvent)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<div class='dump-container'>");
        sb.AppendLine($"<div class='dump-header'>{title}</div>");
        sb.AppendLine("<table class='dump-table'>");
        
        // Header
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th class='row-num'>#</th>");
        sb.AppendLine("<th>Phase</th>");
        sb.AppendLine("<th>Count</th>");
        sb.AppendLine("<th>Min Duration</th>");
        sb.AppendLine("<th>Max Duration</th>");
        sb.AppendLine("<th>Endpoint Count</th>");
        sb.AppendLine("<th>Action</th>");
        sb.AppendLine("</tr></thead>");
        
        
        // Body
        sb.AppendLine("<tbody>");
        int rowNum = 0;
        foreach (var phase in phases)
        {
            rowNum++;
            var phaseId = GetSafeId($"phase-{transportEvent}-{phase.Phase}");
            sb.AppendLine($"<tr class='{(rowNum % 2 == 0 ? "even" : "odd")} clickable-row' onclick=\"showGroup('{phaseId}')\">");
            sb.AppendLine($"<td class='row-num'>{rowNum}</td>");
            sb.AppendLine($"<td><span class='string'>{System.Web.HttpUtility.HtmlEncode(phase.Phase)}</span></td>");
            sb.AppendLine($"<td><span class='number'>{phase.Count:N0}</span></td>");
            sb.AppendLine($"<td><span class='number'>{phase.MinDuration:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{phase.MaxDuration:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{phase.EndpointCount:N0}</span></td>");
            sb.AppendLine($"<td><button class='btn-view' onclick=\"event.stopPropagation(); showGroup('{phaseId}')\">📄 View Entries</button></td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");
        
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");
        
        return sb.ToString();
    }

    private string DumpTransportEventSummaryTable(List<TransportEventGroup> groups)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<div class='dump-container'>");
        sb.AppendLine("<div class='dump-header'>Transport Event Summary</div>");
        sb.AppendLine("<table class='dump-table'>");
        
        // Header
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th class='row-num'>#</th>");
        sb.AppendLine("<th>Event</th>");
        sb.AppendLine("<th>Count</th>");
        sb.AppendLine("<th>Min</th>");
        sb.AppendLine("<th>P50</th>");
        sb.AppendLine("<th>P75</th>");
        sb.AppendLine("<th>P90</th>");
        sb.AppendLine("<th>P95</th>");
        sb.AppendLine("<th>Max</th>");
        sb.AppendLine("<th>Action</th>");
        sb.AppendLine("</tr></thead>");
        
        // Body
        sb.AppendLine("<tbody>");
        int rowNum = 0;
        foreach (var group in groups)
        {
            rowNum++;
            var groupId = GetSafeId($"transport-{group.Status}");
            sb.AppendLine($"<tr class='{(rowNum % 2 == 0 ? "even" : "odd")} clickable-row' onclick=\"showGroup('{groupId}')\">");
            sb.AppendLine($"<td class='row-num'>{rowNum}</td>");
            sb.AppendLine($"<td><span class='enum'>{group.Status}</span></td>");
            sb.AppendLine($"<td><span class='number'>{group.Count:N0}</span></td>");
            sb.AppendLine($"<td><span class='number'>{group.Min:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{group.P50:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{group.P75:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{group.P90:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{group.P95:F2}</span></td>");
            sb.AppendLine($"<td><span class='number'>{group.Max:F2}</span></td>");
            sb.AppendLine($"<td><button class='btn-view' onclick=\"event.stopPropagation(); showGroup('{groupId}')\">📄 View Entries</button></td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");
        
        
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");
        
        return sb.ToString();
    }

    private string CreatePercentileSection(GroupedResult group, string prefix, string percentileName, double percentileValue, List<GroupedEntry> entries)
    {
        if (!entries.Any()) return string.Empty;
        
        var sb = new StringBuilder();
        var percentileId = GetSafeId($"{prefix}-{group.Key}-{percentileName.ToLower()}");
        sb.AppendLine($"<div id='group-{percentileId}' class='section bucket-details' style='display:none;'>");
        sb.AppendLine($"<h2>📊 {percentileName} Entries for: {System.Web.HttpUtility.HtmlEncode(group.Key)}</h2>");
        sb.AppendLine($"<p class='percentile-info'>Showing entries around {percentileName} ({percentileValue:F2}ms) ± 5%</p>");
        sb.AppendLine($"<button class='btn-close' onclick=\"document.getElementById('group-{percentileId}').style.display='none'\">✕ Close</button>");
        sb.AppendLine(DumpTable($"Showing {entries.Count} entries at {percentileName}", entries, sortable: true, tableId: $"percentile-table-{percentileId}"));
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

/* Clickable rows in GroupBy tables */
.clickable-row {
    cursor: pointer;
    transition: background 0.2s;
}

.clickable-row:hover {
    background: #094771 !important;
}

/* Clickable percentile cells */
.clickable-cell {
    cursor: pointer;
    transition: background 0.2s;
}

.clickable-cell:hover {
    background: #1a5276 !important;
}

.percentile-link {
    color: #4fc3f7 !important;
    text-decoration: underline;
    text-decoration-style: dotted;
}

.percentile-link:hover {
    color: #81d4fa !important;
}

.percentile-info {
    color: #9cdcfe;
    font-style: italic;
    margin: 5px 0 15px 0;
    font-size: 13px;
}

.btn-view {
    background: #0e639c;
    color: white;
    border: none;
    padding: 4px 10px;
    border-radius: 4px;
    cursor: pointer;
    font-size: 12px;
    white-space: nowrap;
}

.btn-view:hover {
    background: #1177bb;
}

/* Clickable header for transport events */
.clickable-header {
    cursor: pointer;
    transition: color 0.2s;
}

.clickable-header:hover {
    color: #4fc3f7;
}

.click-hint {
    font-size: 12px;
    color: #6a9955;
    font-weight: normal;
    margin-left: 10px;
}

/* Collapsible section header */
.section-header.collapsible {
    display: flex;
    justify-content: space-between;
    align-items: center;
    cursor: pointer;
    padding: 5px 10px;
    margin: -20px -20px 15px -20px;
    background: linear-gradient(135deg, #2d3748, #1a202c);
    border-radius: 8px 8px 0 0;
    transition: background 0.2s;
}

.section-header.collapsible:hover {
    background: linear-gradient(135deg, #3d4758, #2a303c);
}

.section-header.collapsible h2 {
    margin: 0;
    font-size: 1.1em;
}

.collapse-icon {
    font-size: 14px;
    color: #4fc3f7;
    transition: transform 0.3s;
}

.collapse-icon.expanded {
    transform: rotate(90deg);
}

.section-content {
    animation: expandIn 0.3s ease-out;
}

@keyframes expandIn {
    from { opacity: 0; max-height: 0; }
    to { opacity: 1; max-height: 5000px; }
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

/* JSON button and modal */
.btn-json {
    background: #0e639c;
    color: white;
    border: none;
    padding: 4px 10px;
    border-radius: 4px;
    cursor: pointer;
    font-size: 12px;
    white-space: nowrap;
}

.btn-json:hover {
    background: #1177bb;
}

.json-content {
    display: none;
}

.modal {
    display: none;
    position: fixed;
    z-index: 1000;
    left: 0;
    top: 0;
    width: 100%;
    height: 100%;
    background-color: rgba(0,0,0,0.8);
    animation: fadeIn 0.2s;
}

@keyframes fadeIn {
    from { opacity: 0; }
    to { opacity: 1; }
}

.modal-content {
    background-color: #1e1e1e;
    margin: 5% auto;
    padding: 0;
    border: 1px solid #3e3e3e;
    border-radius: 8px;
    width: 90%;
    max-width: 1200px;
    max-height: 80vh;
    display: flex;
    flex-direction: column;
}

.modal-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 15px 20px;
    background: #2d2d2d;
    border-bottom: 1px solid #3e3e3e;
    border-radius: 8px 8px 0 0;
}

.modal-header h3 {
    margin: 0;
    color: #4fc3f7;
}

.modal-close {
    background: none;
    border: none;
    color: #808080;
    font-size: 28px;
    cursor: pointer;
    line-height: 1;
}

.modal-close:hover {
    color: #dc3545;
}

.modal-actions {
    padding: 10px 20px;
    background: #252526;
    border-bottom: 1px solid #3e3e3e;
    display: flex;
    gap: 10px;
}

.btn-copy, .btn-format {
    background: #0e639c;
    color: white;
    border: none;
    padding: 6px 12px;
    border-radius: 4px;
    cursor: pointer;
    font-size: 13px;
}

.btn-copy:hover, .btn-format:hover {
    background: #1177bb;
}

.json-display {
    margin: 0;
    padding: 20px;
    overflow: auto;
    flex: 1;
    background: #1e1e1e;
    color: #ce9178;
    font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
    font-size: 13px;
    line-height: 1.5;
    white-space: pre-wrap;
    word-wrap: break-word;
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

// Toggle collapsible section
function toggleSection(sectionId) {
    const content = document.getElementById(sectionId);
    const icon = document.getElementById(sectionId + '-icon');
    
    if (content.style.display === 'none') {
        content.style.display = 'block';
        icon.classList.add('expanded');
        icon.textContent = '▼';
    } else {
        content.style.display = 'none';
        icon.classList.remove('expanded');
        icon.textContent = '▶';
    }
}

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

// Show group details (for GroupBy sections)
function showGroup(groupId) {
    // Hide all bucket details first
    document.querySelectorAll('.bucket-details').forEach(el => {
        el.style.display = 'none';
    });
    
    // Show the selected group
    const groupEl = document.getElementById('group-' + groupId);
    if (groupEl) {
        groupEl.style.display = 'block';
        groupEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
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

// JSON Modal functions
let currentJsonContent = '';

function showJson(jsonId) {
    const jsonEl = document.getElementById(jsonId);
    if (!jsonEl) return;
    
    // Get the text content and trim whitespace
    currentJsonContent = jsonEl.textContent.trim();
    document.getElementById('jsonModalContent').textContent = currentJsonContent;
    document.getElementById('jsonModal').style.display = 'block';
    document.body.style.overflow = 'hidden';
}

function closeJsonModal(event) {
    if (event && event.target !== document.getElementById('jsonModal')) return;
    document.getElementById('jsonModal').style.display = 'none';
    document.body.style.overflow = 'auto';
}

function copyJsonContent() {
    navigator.clipboard.writeText(currentJsonContent).then(() => {
        const btn = document.querySelector('.btn-copy');
        const originalText = btn.innerText;
        btn.innerText = '✓ Copied!';
        btn.style.background = '#28a745';
        setTimeout(() => {
            btn.innerText = originalText;
            btn.style.background = '#0e639c';
        }, 2000);
    });
}

function formatJson() {
    try {
        // Trim whitespace and try to parse
        const trimmed = currentJsonContent.trim();
        const parsed = JSON.parse(trimmed);
        const formatted = JSON.stringify(parsed, null, 2);
        document.getElementById('jsonModalContent').innerText = formatted;
        currentJsonContent = formatted;
        
        // Update button to show success
        const btn = document.querySelector('.btn-format');
        const originalText = btn.innerText;
        btn.innerText = '✓ Formatted!';
        btn.style.background = '#28a745';
        setTimeout(() => {
            btn.innerText = originalText;
            btn.style.background = '#0e639c';
        }, 2000);
    } catch (e) {
        console.error('JSON parse error:', e);
        alert('Invalid JSON - cannot format\\n\\nError: ' + e.message);
    }
}

// Close modal with Escape key
document.addEventListener('keydown', function(e) {
    if (e.key === 'Escape') {
        closeJsonModal();
    }
});
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
