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
        
        
        // System Metrics Time Plot
        if (result.SystemMetrics != null && result.SystemMetrics.Snapshots.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<h2>📈 System Metrics Time Plot</h2>");
            sb.AppendLine($"<p class='note'>Based on {result.SystemMetrics.SampleCount} samples from {result.SystemMetrics.StartTime:HH:mm:ss} to {result.SystemMetrics.EndTime:HH:mm:ss}. Click on chart points to see details.</p>");
            sb.AppendLine(DumpSystemMetricsTable(result.SystemMetrics));
            sb.AppendLine(DumpSystemMetricsChart(result.SystemMetrics));
            sb.AppendLine("</div>");
        }
        
        // Client Configuration Metrics Time Plot
        if (result.ClientConfigMetrics != null && result.ClientConfigMetrics.Snapshots.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<h2>🖥️ Client Configuration Time Plot</h2>");
            sb.AppendLine($"<p class='note'>Based on {result.ClientConfigMetrics.SampleCount} entries from {result.ClientConfigMetrics.StartTime:HH:mm:ss} to {result.ClientConfigMetrics.EndTime:HH:mm:ss}. {result.ClientConfigMetrics.UniqueMachineIds.Count} unique machine(s).</p>");
            sb.AppendLine(DumpClientConfigTable(result.ClientConfigMetrics));
            sb.AppendLine(DumpClientConfigChart(result.ClientConfigMetrics));
            sb.AppendLine("</div>");
        }
        
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
                sb.AppendLine($"<button class='btn-close' onclick=\"closeDrillDown('bucket-{bucketId}')\">✕ Close</button>");
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
            sb.AppendLine("<p class='note'>Click on a row to see all entries, or click on a percentile value to see entries in that range</p>");
            sb.AppendLine(DumpGroupedResultTable("Resource Type Groups", result.ResourceTypeGroups, "resourceType"));
            sb.AppendLine("</div>");
            
            // Hidden sections for each group's entries
            foreach (var group in result.ResourceTypeGroups)
            {
                var groupId = GetSafeId($"resourceType-{group.Key}");
                sb.AppendLine($"<div id='group-{groupId}' class='section bucket-details' style='display:none;'>");
                sb.AppendLine($"<h2>📋 Entries for: {System.Web.HttpUtility.HtmlEncode(group.Key)}</h2>");
                sb.AppendLine($"<button class='btn-close' onclick=\"closeDrillDown('group-{groupId}')\">✕ Close</button>");
                sb.AppendLine(DumpTable($"Showing {group.Entries.Count} of {group.Count} entries", group.Entries, sortable: true, tableId: $"group-table-{groupId}"));
                sb.AppendLine("</div>");
                
                // Hidden sections for percentile entries with proper ranges
                // P50: values ≤ P50
                sb.AppendLine(CreatePercentileSection(group, "resourceType", "P50", group.P50, null, group.EntriesAtP50));
                // P75: values > P50 and ≤ P75
                sb.AppendLine(CreatePercentileSection(group, "resourceType", "P75", group.P75, group.P50, group.EntriesAtP75));
                // P90: values > P75 and ≤ P90
                sb.AppendLine(CreatePercentileSection(group, "resourceType", "P90", group.P90, group.P75, group.EntriesAtP90));
                // P95: values > P90 and ≤ P95
                sb.AppendLine(CreatePercentileSection(group, "resourceType", "P95", group.P95, group.P90, group.EntriesAtP95));
            }
        }
        
        // Status Code Groups
        if (result.StatusCodeGroups.Any())
        {
            sb.AppendLine("<div class='section'>");
            sb.AppendLine("<h2>🔢 GroupBy {StatusCode → SubStatusCode}</h2>");
            sb.AppendLine("<p class='note'>Click on a row to see all entries, or click on a percentile value to see entries in that range</p>");
            sb.AppendLine(DumpGroupedResultTable("Status Code Groups", result.StatusCodeGroups, "statusCode"));
            sb.AppendLine("</div>");
            
            
            
            // Hidden sections for each group's entries
            foreach (var group in result.StatusCodeGroups)
            {
                var groupId = GetSafeId($"statusCode-{group.Key}");
                sb.AppendLine($"<div id='group-{groupId}' class='section bucket-details' style='display:none;'>");
                sb.AppendLine($"<h2>📋 Entries for: {System.Web.HttpUtility.HtmlEncode(group.Key)}</h2>");
                sb.AppendLine($"<button class='btn-close' onclick=\"closeDrillDown('group-{groupId}')\">✕ Close</button>");
                sb.AppendLine(DumpTable($"Showing {group.Entries.Count} of {group.Count} entries", group.Entries, sortable: true, tableId: $"group-table-{groupId}"));
                sb.AppendLine("</div>");
                
                // Hidden sections for percentile entries with proper ranges
                // P50: values ≤ P50
                sb.AppendLine(CreatePercentileSection(group, "statusCode", "P50", group.P50, null, group.EntriesAtP50));
                // P75: values > P50 and ≤ P75
                sb.AppendLine(CreatePercentileSection(group, "statusCode", "P75", group.P75, group.P50, group.EntriesAtP75));
                // P90: values > P75 and ≤ P90
                sb.AppendLine(CreatePercentileSection(group, "statusCode", "P90", group.P90, group.P75, group.EntriesAtP90));
                // P95: values > P90 and ≤ P95
                sb.AppendLine(CreatePercentileSection(group, "statusCode", "P95", group.P95, group.P90, group.EntriesAtP95));
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
                sb.AppendLine($"<button class='btn-close' onclick=\"closeDrillDown('group-{groupId}')\">✕ Close</button>");
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
                    sb.AppendLine($"<button class='btn-close' onclick=\"closeDrillDown('group-{phaseId}')\">✕ Close</button>");
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
                sb.AppendLine($"<button class='btn-close' onclick=\"closeDrillDown('group-{groupId}')\">✕ Close</button>");
                sb.AppendLine(DumpTable($"Showing {group.Entries.Count} of {group.Count} entries", group.Entries, sortable: true, tableId: $"group-table-{groupId}"));
                sb.AppendLine("</div>");
                
                // Hidden sections for percentile entries with proper ranges
                // P50: values ≤ P50
                sb.AppendLine(CreateTransportEventPercentileSection(group, "P50", group.P50, null, group.EntriesAtP50));
                // P75: values > P50 and ≤ P75
                sb.AppendLine(CreateTransportEventPercentileSection(group, "P75", group.P75, group.P50, group.EntriesAtP75));
                // P90: values > P75 and ≤ P90
                sb.AppendLine(CreateTransportEventPercentileSection(group, "P90", group.P90, group.P75, group.EntriesAtP90));
                // P95: values > P90 and ≤ P95
                sb.AppendLine(CreateTransportEventPercentileSection(group, "P95", group.P95, group.P90, group.EntriesAtP95));
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

    private string DumpSystemMetricsTable(SystemMetricsTimePlot metrics)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<div class='dump-container'>");
        sb.AppendLine("<div class='dump-header'>System Metrics Statistics</div>");
        sb.AppendLine("<table class='dump-table'>");
        
        // Header
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th>Metric</th>");
        sb.AppendLine("<th>Min</th>");
        sb.AppendLine("<th>Avg</th>");
        sb.AppendLine("<th>P90</th>");
        sb.AppendLine("<th>Max</th>");
        sb.AppendLine("</tr></thead>");
        
        // Body
        sb.AppendLine("<tbody>");
        
        // CPU row
        sb.AppendLine("<tr class='odd'>");
        sb.AppendLine("<td><span class='string'>CPU (%)</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.Cpu.Min:F2}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.Cpu.Avg:F2}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.Cpu.P90:F2}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.Cpu.Max:F2}</span></td>");
        sb.AppendLine("</tr>");
        
        // Memory row (convert to MB)
        sb.AppendLine("<tr class='even'>");
        sb.AppendLine("<td><span class='string'>Memory (MB)</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.Memory.Min / 1024 / 1024:F2}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.Memory.Avg / 1024 / 1024:F2}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.Memory.P90 / 1024 / 1024:F2}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.Memory.Max / 1024 / 1024:F2}</span></td>");
        sb.AppendLine("</tr>");
        
        // Thread Wait Interval row
        sb.AppendLine("<tr class='odd'>");
        sb.AppendLine("<td><span class='string'>Thread Wait Interval (ms)</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.ThreadWaitIntervalInMs.Min:F2}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.ThreadWaitIntervalInMs.Avg:F2}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.ThreadWaitIntervalInMs.P90:F2}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.ThreadWaitIntervalInMs.Max:F2}</span></td>");
        sb.AppendLine("</tr>");
        
        // TCP Connections row
        sb.AppendLine("<tr class='even'>");
        sb.AppendLine("<td><span class='string'>Open TCP Connections</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfOpenTcpConnections.Min:N0}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfOpenTcpConnections.Avg:N0}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfOpenTcpConnections.P90:N0}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfOpenTcpConnections.Max:N0}</span></td>");
        sb.AppendLine("</tr>");
        
        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");
        
        return sb.ToString();
    }

    private string DumpSystemMetricsChart(SystemMetricsTimePlot metrics)
    {
        var sb = new StringBuilder();
        
        // Metric selector with checkboxes for multi-select
        sb.AppendLine("<div class='chart-controls'>");
        sb.AppendLine("<span class='control-label'>Select Metrics:</span>");
        sb.AppendLine("<div class='metric-checkboxes'>");
        sb.AppendLine("<label class='metric-checkbox'><input type='checkbox' id='chkCpu' checked onchange='updateChartMulti()'><span class='metric-color' style='background:#4fc3f7'></span>CPU (%)</label>");
        sb.AppendLine("<label class='metric-checkbox'><input type='checkbox' id='chkMemory' onchange='updateChartMulti()'><span class='metric-color' style='background:#81c784'></span>Memory (MB)</label>");
        sb.AppendLine("<label class='metric-checkbox'><input type='checkbox' id='chkThreadWait' onchange='updateChartMulti()'><span class='metric-color' style='background:#ffb74d'></span>Thread Wait (ms)</label>");
        sb.AppendLine("<label class='metric-checkbox'><input type='checkbox' id='chkTcpConnections' onchange='updateChartMulti()'><span class='metric-color' style='background:#ba68c8'></span>TCP Connections</label>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='chart-buttons'>");
        sb.AppendLine("<button class='btn-chart' onclick='selectAllMetrics()'>Select All</button>");
        sb.AppendLine("<button class='btn-chart' onclick='clearAllMetrics()'>Clear All</button>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        
        // Chart container
        sb.AppendLine("<div class='chart-container'>");
        sb.AppendLine("<canvas id='metricsChart'></canvas>");
        sb.AppendLine("</div>");
        
        // Selected point details
        sb.AppendLine("<div id='pointDetails' class='point-details' style='display:none;'>");
        sb.AppendLine("<h4>📍 Selected Point Details</h4>");
        sb.AppendLine("<div id='pointDetailsContent'></div>");
        sb.AppendLine("</div>");
        
        // Embed the data as JSON
        sb.AppendLine("<script>");
        sb.AppendLine("const systemMetricsData = {");
        sb.AppendLine($"  labels: [{string.Join(",", metrics.Snapshots.Select(s => $"'{s.DateUtc:HH:mm:ss}'"))}],");
        sb.AppendLine($"  cpu: [{string.Join(",", metrics.Snapshots.Select(s => s.Cpu.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}],");
        sb.AppendLine($"  memory: [{string.Join(",", metrics.Snapshots.Select(s => (s.Memory / 1024.0 / 1024.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}],");
        sb.AppendLine($"  threadWait: [{string.Join(",", metrics.Snapshots.Select(s => s.ThreadWaitIntervalInMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}],");
        sb.AppendLine($"  tcpConnections: [{string.Join(",", metrics.Snapshots.Select(s => s.NumberOfOpenTcpConnections))}],");
        sb.AppendLine("  details: [");
        foreach (var (snapshot, i) in metrics.Snapshots.Select((s, i) => (s, i)))
        {
            var comma = i < metrics.Snapshots.Count - 1 ? "," : "";
            sb.AppendLine($"    {{ dateUtc: '{snapshot.DateUtc:yyyy-MM-dd HH:mm:ss.fff}', cpu: {snapshot.Cpu.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}, memory: {snapshot.Memory}, threadWait: {snapshot.ThreadWaitIntervalInMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}, tcpConnections: {snapshot.NumberOfOpenTcpConnections}, isThreadStarving: {snapshot.IsThreadStarving.ToString().ToLower()}, availableThreads: {snapshot.AvailableThreads} }}{comma}");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("};");
        sb.AppendLine("</script>");
        
        return sb.ToString();
    }

    private string DumpClientConfigTable(ClientConfigTimePlot metrics)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<div class='dump-container'>");
        sb.AppendLine("<div class='dump-header'>Client Configuration Statistics</div>");
        sb.AppendLine("<table class='dump-table'>");
        
        // Header
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th>Metric</th>");
        sb.AppendLine("<th>Min</th>");
        sb.AppendLine("<th>Avg</th>");
        sb.AppendLine("<th>P90</th>");
        sb.AppendLine("<th>Max</th>");
        sb.AppendLine("</tr></thead>");
        
        // Body
        sb.AppendLine("<tbody>");
        
        // Processor Count row
        sb.AppendLine("<tr class='odd'>");
        sb.AppendLine("<td><span class='string'>Processor Count</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.ProcessorCount.Min:N0}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.ProcessorCount.Avg:F1}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.ProcessorCount.P90:N0}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.ProcessorCount.Max:N0}</span></td>");
        sb.AppendLine("</tr>");
        
        // Clients Created row
        sb.AppendLine("<tr class='even'>");
        sb.AppendLine("<td><span class='string'>Clients Created</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfClientsCreated.Min:N0}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfClientsCreated.Avg:F1}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfClientsCreated.P90:N0}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfClientsCreated.Max:N0}</span></td>");
        sb.AppendLine("</tr>");
        
        // Active Clients row
        sb.AppendLine("<tr class='odd'>");
        sb.AppendLine("<td><span class='string'>Active Clients</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfActiveClients.Min:N0}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfActiveClients.Avg:F1}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfActiveClients.P90:N0}</span></td>");
        sb.AppendLine($"<td><span class='number'>{metrics.NumberOfActiveClients.Max:N0}</span></td>");
        sb.AppendLine("</tr>");
        
        // Unique Machines row
        sb.AppendLine("<tr class='even'>");
        sb.AppendLine("<td><span class='string'>Unique Machines</span></td>");
        sb.AppendLine($"<td colspan='4'><span class='number'>{metrics.UniqueMachineIds.Count}</span> machine(s)</td>");
        sb.AppendLine("</tr>");
        
        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");
        
        return sb.ToString();
    }

    private string DumpClientConfigChart(ClientConfigTimePlot metrics)
    {
        var sb = new StringBuilder();
        
        // Metric selector with checkboxes for multi-select
        sb.AppendLine("<div class='chart-controls'>");
        sb.AppendLine("<span class='control-label'>Select Metrics:</span>");
        sb.AppendLine("<div class='metric-checkboxes'>");
        sb.AppendLine("<label class='metric-checkbox'><input type='checkbox' id='chkProcessorCount' checked onchange='updateClientConfigChart()'><span class='metric-color' style='background:#ff7043'></span>Processor Count</label>");
        sb.AppendLine("<label class='metric-checkbox'><input type='checkbox' id='chkClientsCreated' onchange='updateClientConfigChart()'><span class='metric-color' style='background:#42a5f5'></span>Clients Created</label>");
        sb.AppendLine("<label class='metric-checkbox'><input type='checkbox' id='chkActiveClients' onchange='updateClientConfigChart()'><span class='metric-color' style='background:#66bb6a'></span>Active Clients</label>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        
        // Chart container
        sb.AppendLine("<div class='chart-container'>");
        sb.AppendLine("<canvas id='clientConfigChart'></canvas>");
        sb.AppendLine("</div>");
        
        // Machine ID legend
        if (metrics.UniqueMachineIds.Count > 1)
        {
            sb.AppendLine("<div class='machine-legend'>");
            sb.AppendLine("<span class='control-label'>Machines:</span>");
            foreach (var (machineId, index) in metrics.UniqueMachineIds.Select((m, i) => (m, i)))
            {
                var shortId = machineId.Length > 12 ? "..." + machineId.Substring(machineId.Length - 12) : machineId;
                sb.AppendLine($"<span class='machine-tag' title='{System.Web.HttpUtility.HtmlEncode(machineId)}'>{shortId}</span>");
            }
            sb.AppendLine("</div>");
        }
        
        // Selected point details
        sb.AppendLine("<div id='clientPointDetails' class='point-details' style='display:none;'>");
        sb.AppendLine("<h4>📍 Selected Point Details</h4>");
        sb.AppendLine("<div id='clientPointDetailsContent'></div>");
        sb.AppendLine("</div>");
        
        // Embed the data as JSON
        sb.AppendLine("<script>");
        sb.AppendLine("const clientConfigData = {");
        sb.AppendLine($"  labels: [{string.Join(",", metrics.Snapshots.Select(s => $"'{s.DateUtc:HH:mm:ss}'"))}],");
        sb.AppendLine($"  processorCount: [{string.Join(",", metrics.Snapshots.Select(s => s.ProcessorCount))}],");
        sb.AppendLine($"  clientsCreated: [{string.Join(",", metrics.Snapshots.Select(s => s.NumberOfClientsCreated))}],");
        sb.AppendLine($"  activeClients: [{string.Join(",", metrics.Snapshots.Select(s => s.NumberOfActiveClients))}],");
        sb.AppendLine($"  machineIds: [{string.Join(",", metrics.Snapshots.Select(s => $"'{s.ShortMachineId}'"))}],");
        sb.AppendLine("  details: [");
        foreach (var (snapshot, i) in metrics.Snapshots.Select((s, i) => (s, i)))
        {
            var comma = i < metrics.Snapshots.Count - 1 ? "," : "";
            sb.AppendLine($"    {{ dateUtc: '{snapshot.DateUtc:yyyy-MM-dd HH:mm:ss.fff}', machineId: '{snapshot.ShortMachineId}', fullMachineId: '{snapshot.MachineId}', processorCount: {snapshot.ProcessorCount}, clientsCreated: {snapshot.NumberOfClientsCreated}, activeClients: {snapshot.NumberOfActiveClients}, connectionMode: '{snapshot.ConnectionMode}' }}{comma}");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("};");
        sb.AppendLine("</script>");
        
        return sb.ToString();
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
            var p50Id = GetSafeId($"transport-{group.Status}-p50");
            var p75Id = GetSafeId($"transport-{group.Status}-p75");
            var p90Id = GetSafeId($"transport-{group.Status}-p90");
            var p95Id = GetSafeId($"transport-{group.Status}-p95");
            sb.AppendLine($"<tr class='{(rowNum % 2 == 0 ? "even" : "odd")} clickable-row' onclick=\"showGroup('{groupId}')\">");
            sb.AppendLine($"<td class='row-num'>{rowNum}</td>");
            sb.AppendLine($"<td><span class='enum'>{group.Status}</span></td>");
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

    private string CreatePercentileSection(GroupedResult group, string prefix, string percentileName, double percentileValue, double? lowerBound, List<GroupedEntry> entries)
    {
        if (!entries.Any()) return string.Empty;
        
        var sb = new StringBuilder();
        var percentileId = GetSafeId($"{prefix}-{group.Key}-{percentileName.ToLower()}");
        sb.AppendLine($"<div id='group-{percentileId}' class='section bucket-details' style='display:none;'>");
        sb.AppendLine($"<h2>📊 {percentileName} Entries for: {System.Web.HttpUtility.HtmlEncode(group.Key)}</h2>");
        
        // Show the range description
        string rangeDescription = lowerBound.HasValue 
            ? $"Showing entries where duration > {lowerBound.Value:F2}ms and ≤ {percentileValue:F2}ms"
            : $"Showing entries where duration ≤ {percentileValue:F2}ms";
        sb.AppendLine($"<p class='percentile-info'>{rangeDescription}</p>");
        
        sb.AppendLine($"<button class='btn-close' onclick=\"closeDrillDown('group-{percentileId}')\">✕ Close</button>");
        sb.AppendLine(DumpTable($"Showing {entries.Count} entries in {percentileName} range", entries, sortable: true, tableId: $"percentile-table-{percentileId}"));
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private string CreateTransportEventPercentileSection(TransportEventGroup group, string percentileName, double percentileValue, double? lowerBound, List<GroupedEntry> entries)
    {
        if (!entries.Any()) return string.Empty;
        
        var sb = new StringBuilder();
        var percentileId = GetSafeId($"transport-{group.Status}-{percentileName.ToLower()}");
        sb.AppendLine($"<div id='group-{percentileId}' class='section bucket-details' style='display:none;'>");
        sb.AppendLine($"<h2>📊 {percentileName} Entries for: {group.Status}</h2>");
        
        // Show the range description
        string rangeDescription = lowerBound.HasValue 
            ? $"Showing entries where duration > {lowerBound.Value:F2}ms and ≤ {percentileValue:F2}ms"
            : $"Showing entries where duration ≤ {percentileValue:F2}ms";
        sb.AppendLine($"<p class='percentile-info'>{rangeDescription}</p>");
        
        sb.AppendLine($"<button class='btn-close' onclick=\"closeDrillDown('group-{percentileId}')\">✕ Close</button>");
        sb.AppendLine(DumpTable($"Showing {entries.Count} entries in {percentileName} range", entries, sortable: true, tableId: $"percentile-table-{percentileId}"));
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

/* Chart controls and container */
.chart-controls {
    margin: 15px 0;
    padding: 10px;
    background: #2d2d30;
    border-radius: 6px;
}

.chart-controls label {
    color: #dcdcdc;
    margin-right: 10px;
}

.chart-controls select {
    background: #3c3c3c;
    color: #dcdcdc;
    border: 1px solid #4fc3f7;
    padding: 8px 12px;
    border-radius: 4px;
    font-size: 14px;
    cursor: pointer;
}

.chart-controls select:hover {
    background: #4a4a4a;
}

.control-label {
    color: #dcdcdc;
    font-weight: bold;
    margin-right: 15px;
}

.metric-checkboxes {
    display: flex;
    flex-wrap: wrap;
    gap: 15px;
    margin: 10px 0;
}

.metric-checkbox {
    display: flex;
    align-items: center;
    gap: 6px;
    color: #dcdcdc;
    cursor: pointer;
    padding: 6px 12px;
    background: #3c3c3c;
    border-radius: 4px;
    transition: background 0.2s;
}

.metric-checkbox:hover {
    background: #4a4a4a;
}

.metric-checkbox input[type='checkbox'] {
    width: 16px;
    height: 16px;
    cursor: pointer;
}

.metric-color {
    width: 12px;
    height: 12px;
    border-radius: 2px;
    display: inline-block;
}

.chart-buttons {
    display: flex;
    gap: 10px;
    margin-top: 10px;
}

.btn-chart {
    background: #3c3c3c;
    color: #dcdcdc;
    border: 1px solid #555;
    padding: 6px 12px;
    border-radius: 4px;
    cursor: pointer;
    font-size: 12px;
}

.btn-chart:hover {
    background: #4a4a4a;
    border-color: #4fc3f7;
}

.chart-container {
    background: #1e1e1e;
    border-radius: 8px;
    padding: 20px;
    margin: 15px 0;
    height: 400px;
}

.machine-legend {
    display: flex;
    flex-wrap: wrap;
    gap: 10px;
    align-items: center;
    margin: 10px 0;
    padding: 10px;
    background: #2d2d30;
    border-radius: 6px;
}

.machine-tag {
    background: #3c3c3c;
    color: #ff7043;
    padding: 4px 10px;
    border-radius: 4px;
    font-size: 12px;
    font-family: monospace;
}

.point-details {
    background: #2d2d30;
    border: 1px solid #4fc3f7;
    border-radius: 6px;
    padding: 15px;
    margin-top: 15px;
}

.point-details h4 {
    margin: 0 0 10px 0;
    color: #4fc3f7;
}

.point-details-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
    gap: 10px;
}

.point-detail-item {
    background: #1e1e1e;
    padding: 8px 12px;
    border-radius: 4px;
}

.point-detail-item .label {
    color: #9cdcfe;
    font-size: 12px;
}


.point-detail-item .value {
    color: #dcdcdc;
    font-size: 16px;
    font-weight: bold;
}

.point-detail-item .value.warning {
    color: #f48771;
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
// Store the scroll position before opening a drill-down
let savedScrollPosition = 0;

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
    // Save current scroll position
    savedScrollPosition = window.scrollY;
    
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
    // Save current scroll position
    savedScrollPosition = window.scrollY;
    
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

// Close drill-down and restore scroll position
function closeDrillDown(elementId) {
    const el = document.getElementById(elementId);
    if (el) {
        el.style.display = 'none';
    }
    // Restore scroll position
    window.scrollTo({ top: savedScrollPosition, behavior: 'smooth' });
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

// ===== System Metrics Chart =====
let metricsChart = null;

function initMetricsChart() {
    if (typeof systemMetricsData === 'undefined' || !document.getElementById('metricsChart')) return;
    
    const ctx = document.getElementById('metricsChart').getContext('2d');
    
    metricsChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: systemMetricsData.labels,
            datasets: []
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                intersect: false,
                mode: 'index'
            },
            plugins: {
                legend: {
                    labels: { color: '#dcdcdc' },
                    onClick: function(e, legendItem, legend) {
                        // Toggle checkbox when legend is clicked
                        const metricMap = { 'CPU (%)': 'chkCpu', 'Memory (MB)': 'chkMemory', 'Thread Wait (ms)': 'chkThreadWait', 'TCP Connections': 'chkTcpConnections' };
                        const checkboxId = metricMap[legendItem.text];
                        if (checkboxId) {
                            const checkbox = document.getElementById(checkboxId);
                            checkbox.checked = !checkbox.checked;
                            updateChartMulti();
                        }
                    }
                },
                tooltip: {
                    backgroundColor: '#2d2d30',
                    titleColor: '#4fc3f7',
                    bodyColor: '#dcdcdc',
                    borderColor: '#4fc3f7',
                    borderWidth: 1
                }
            },
            scales: {
                x: {
                    ticks: { color: '#9cdcfe' },
                    grid: { color: '#3e3e3e' },
                    title: { display: true, text: 'Time', color: '#dcdcdc' }
                },
                y: {
                    type: 'linear',
                    display: true,
                    position: 'left',
                    ticks: { color: '#4fc3f7' },
                    grid: { color: '#3e3e3e' },
                    title: { display: true, text: 'CPU (%)', color: '#4fc3f7' }
                },
                y1: {
                    type: 'linear',
                    display: false,
                    position: 'right',
                    ticks: { color: '#81c784' },
                    grid: { drawOnChartArea: false },
                    title: { display: true, text: 'Memory (MB)', color: '#81c784' }
                },
                y2: {
                    type: 'linear',
                    display: false,
                    position: 'right',
                    ticks: { color: '#ffb74d' },
                    grid: { drawOnChartArea: false },
                    title: { display: true, text: 'Thread Wait (ms)', color: '#ffb74d' }
                },
                y3: {
                    type: 'linear',
                    display: false,
                    position: 'right',
                    ticks: { color: '#ba68c8' },
                    grid: { drawOnChartArea: false },
                    title: { display: true, text: 'TCP Connections', color: '#ba68c8' }
                }
            },
            onClick: (event, elements) => {
                if (elements.length > 0) {
                    const index = elements[0].index;
                    showPointDetails(index);
                }
            }
        }
    });
    
    // Initialize with checked metrics
    updateChartMulti();
}


const metricConfigs = {
    cpu: { data: 'cpu', label: 'CPU (%)', color: '#4fc3f7', yAxisID: 'y' },
    memory: { data: 'memory', label: 'Memory (MB)', color: '#81c784', yAxisID: 'y1' },
    threadWait: { data: 'threadWait', label: 'Thread Wait (ms)', color: '#ffb74d', yAxisID: 'y2' },
    tcpConnections: { data: 'tcpConnections', label: 'TCP Connections', color: '#ba68c8', yAxisID: 'y3' }
};

function updateChartMulti() {
    if (!metricsChart) return;
    
    const datasets = [];
    const activeAxes = new Set();
    
    if (document.getElementById('chkCpu').checked) {
        datasets.push(createDataset('cpu'));
        activeAxes.add('y');
    }
    if (document.getElementById('chkMemory').checked) {
        datasets.push(createDataset('memory'));
        activeAxes.add('y1');
    }
    if (document.getElementById('chkThreadWait').checked) {
        datasets.push(createDataset('threadWait'));
        activeAxes.add('y2');
    }
    if (document.getElementById('chkTcpConnections').checked) {
        datasets.push(createDataset('tcpConnections'));
        activeAxes.add('y3');
    }
    
    metricsChart.data.datasets = datasets;
    
    // Update Y-axis visibility
    metricsChart.options.scales.y.display = activeAxes.has('y');
    metricsChart.options.scales.y1.display = activeAxes.has('y1');
    metricsChart.options.scales.y2.display = activeAxes.has('y2');
    metricsChart.options.scales.y3.display = activeAxes.has('y3');
    
    metricsChart.update();
}

function createDataset(metricKey) {
    const config = metricConfigs[metricKey];
    return {
        label: config.label,
        data: systemMetricsData[config.data],
        borderColor: config.color,
        backgroundColor: config.color + '1A',
        borderWidth: 2,
        pointRadius: 3,
        pointHoverRadius: 6,
        pointBackgroundColor: config.color,
        tension: 0.3,
        fill: false,
        yAxisID: config.yAxisID
    };
}

function selectAllMetrics() {
    document.getElementById('chkCpu').checked = true;
    document.getElementById('chkMemory').checked = true;
    document.getElementById('chkThreadWait').checked = true;
    document.getElementById('chkTcpConnections').checked = true;
    updateChartMulti();
}

function clearAllMetrics() {
    document.getElementById('chkCpu').checked = false;
    document.getElementById('chkMemory').checked = false;
    document.getElementById('chkThreadWait').checked = false;
    document.getElementById('chkTcpConnections').checked = false;
    updateChartMulti();
}

function showPointDetails(index) {
    const details = systemMetricsData.details[index];
    const detailsDiv = document.getElementById('pointDetails');
    const contentDiv = document.getElementById('pointDetailsContent');
    
    const memoryMB = (details.memory / 1024 / 1024).toFixed(2);
    const threadStarvingClass = details.isThreadStarving ? 'warning' : '';
    
    contentDiv.innerHTML = `
        <div class='point-details-grid'>
            <div class='point-detail-item'>
                <div class='label'>Time</div>
                <div class='value'>${details.dateUtc}</div>
            </div>
            <div class='point-detail-item'>
                <div class='label'>CPU</div>
                <div class='value'>${details.cpu.toFixed(2)}%</div>
            </div>
            <div class='point-detail-item'>
                <div class='label'>Memory</div>
                <div class='value'>${memoryMB} MB</div>
            </div>
            <div class='point-detail-item'>
                <div class='label'>Thread Wait Interval</div>
                <div class='value'>${details.threadWait.toFixed(2)} ms</div>
            </div>
            <div class='point-detail-item'>
                <div class='label'>Open TCP Connections</div>
                <div class='value'>${details.tcpConnections.toLocaleString()}</div>
            </div>
            <div class='point-detail-item'>
                <div class='label'>Thread Starving</div>
                <div class='value ${threadStarvingClass}'>${details.isThreadStarving ? 'Yes ⚠️' : 'No'}</div>
            </div>
            <div class='point-detail-item'>
                <div class='label'>Available Threads</div>
                <div class='value'>${details.availableThreads.toLocaleString()}</div>
            </div>
        </div>
    `;
    
    detailsDiv.style.display = 'block';
}

// ===== Client Configuration Chart =====
let clientConfigChart = null;

function initClientConfigChart() {
    if (typeof clientConfigData === 'undefined' || !document.getElementById('clientConfigChart')) return;
    
    const ctx = document.getElementById('clientConfigChart').getContext('2d');
    
    clientConfigChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: clientConfigData.labels,
            datasets: []
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                intersect: false,
                mode: 'index'
            },
            plugins: {
                legend: {
                    labels: { color: '#dcdcdc' }
                },
                tooltip: {
                    backgroundColor: '#2d2d30',
                    titleColor: '#ff7043',
                    bodyColor: '#dcdcdc',
                    borderColor: '#ff7043',
                    borderWidth: 1,
                    callbacks: {
                        afterBody: function(context) {
                            const index = context[0].dataIndex;
                            return 'Machine: ' + clientConfigData.machineIds[index];
                        }
                    }
                }
            },
            scales: {
                x: {
                    ticks: { color: '#9cdcfe' },
                    grid: { color: '#3e3e3e' },
                    title: { display: true, text: 'Time', color: '#dcdcdc' }
                },
                y: {
                    type: 'linear',
                    display: true,
                    position: 'left',
                    ticks: { color: '#ff7043' },
                    grid: { color: '#3e3e3e' },
                    title: { display: true, text: 'Processor Count', color: '#ff7043' }
                },
                y1: {
                    type: 'linear',
                    display: false,
                    position: 'right',
                    ticks: { color: '#42a5f5' },
                    grid: { drawOnChartArea: false },
                    title: { display: true, text: 'Clients Created', color: '#42a5f5' }
                },
                y2: {
                    type: 'linear',
                    display: false,
                    position: 'right',
                    ticks: { color: '#66bb6a' },
                    grid: { drawOnChartArea: false },
                    title: { display: true, text: 'Active Clients', color: '#66bb6a' }
                }
            },
            onClick: (event, elements) => {
                if (elements.length > 0) {
                    const index = elements[0].index;
                    showClientPointDetails(index);
                }
            }
        }
    });
    
    updateClientConfigChart();
}

const clientMetricConfigs = {
    processorCount: { data: 'processorCount', label: 'Processor Count', color: '#ff7043', yAxisID: 'y' },
    clientsCreated: { data: 'clientsCreated', label: 'Clients Created', color: '#42a5f5', yAxisID: 'y1' },
    activeClients: { data: 'activeClients', label: 'Active Clients', color: '#66bb6a', yAxisID: 'y2' }
};

function updateClientConfigChart() {
    if (!clientConfigChart) return;
    
    const datasets = [];
    const activeAxes = new Set();
    
    if (document.getElementById('chkProcessorCount').checked) {
        datasets.push(createClientDataset('processorCount'));
        activeAxes.add('y');
    }
    if (document.getElementById('chkClientsCreated').checked) {
        datasets.push(createClientDataset('clientsCreated'));
        activeAxes.add('y1');
    }
    if (document.getElementById('chkActiveClients').checked) {
        datasets.push(createClientDataset('activeClients'));
        activeAxes.add('y2');
    }
    
    clientConfigChart.data.datasets = datasets;
    
    clientConfigChart.options.scales.y.display = activeAxes.has('y');
    clientConfigChart.options.scales.y1.display = activeAxes.has('y1');
    clientConfigChart.options.scales.y2.display = activeAxes.has('y2');
    
    clientConfigChart.update();
}

function createClientDataset(metricKey) {
    const config = clientMetricConfigs[metricKey];
    return {
        label: config.label,
        data: clientConfigData[config.data],
        borderColor: config.color,
        backgroundColor: config.color + '1A',
        borderWidth: 2,
        pointRadius: 3,
        pointHoverRadius: 6,
        pointBackgroundColor: config.color,
        tension: 0.3,
        fill: false,
        yAxisID: config.yAxisID
    };
}

function showClientPointDetails(index) {
    const details = clientConfigData.details[index];
    const detailsDiv = document.getElementById('clientPointDetails');
    const contentDiv = document.getElementById('clientPointDetailsContent');
    
    contentDiv.innerHTML = `
        <div class='point-details-grid'>
            <div class='point-detail-item'>
                <div class='label'>Time</div>
                <div class='value'>${details.dateUtc}</div>
            </div>
            <div class='point-detail-item'>
                <div class='label'>Machine ID</div>
                <div class='value' style='font-family:monospace;font-size:12px'>${details.fullMachineId}</div>
            </div>
            <div class='point-detail-item'>
                <div class='label'>Processor Count</div>
                <div class='value'>${details.processorCount}</div>
            </div>
            <div class='point-detail-item'>
                <div class='label'>Clients Created</div>
                <div class='value'>${details.clientsCreated}</div>
            </div>
            <div class='point-detail-item'>
                <div class='label'>Active Clients</div>
                <div class='value'>${details.activeClients}</div>
            </div>
            <div class='point-detail-item'>
                <div class='label'>Connection Mode</div>
                <div class='value'>${details.connectionMode}</div>
            </div>
        </div>
    `;
    
    detailsDiv.style.display = 'block';
}

// Initialize chart when page loads
document.addEventListener('DOMContentLoaded', function() {
    initMetricsChart();
    initClientConfigChart();
});
</script>
<script src='https://cdn.jsdelivr.net/npm/chart.js'></script>";
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
