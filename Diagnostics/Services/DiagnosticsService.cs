using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Diagnostics.Models;

namespace Diagnostics.Services;

public class DiagnosticsService
{
    private static readonly JsonSerializerOptions LenientOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public DiagnosticsResult AnalyzeDiagnostics(string fileContent, int latencyThreshold = 600)
    {
        var result = new DiagnosticsResult();
        var lines = fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        result.TotalEntries = lines.Length;

        // Parse each line and keep track of original JSON
        var diagnosticsList = new List<(CosmosDiagnostics Diag, string RawJson)>();
        int repairedCount = 0;

        foreach (var line in lines.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            var diag = TryDeserializePartial<CosmosDiagnostics>(line);
            if (diag != null)
            {
                diagnosticsList.Add((diag, line.Trim()));
                if (line.Contains("...") || !line.TrimEnd().EndsWith('}'))
                {
                    repairedCount++;
                }
            }
        }

        result.ParsedEntries = diagnosticsList.Count;
        result.RepairedEntries = repairedCount;

        var diagnostics = diagnosticsList.ToArray();

        // High latency diagnostics
        var highLatencyDiags = diagnostics.Where(e => e.Diag.Duration > latencyThreshold).ToList();
        result.HighLatencyEntries = highLatencyDiags.Count;

        // Store all high latency diagnostics for drill-down
        result.AllHighLatencyDiagnostics = highLatencyDiags
            .Select(e => new DiagnosticEntry
            {
                Name = e.Diag.Name,
                StartTime = e.Diag.StartTime,
                Duration = e.Diag.Duration,
                DirectCallCount = e.Diag.Summary?.GetDirectCallCount() ?? 0,
                GatewayCallCount = e.Diag.Summary?.GetGatewayCallCount() ?? 0,
                TotalCallCount = e.Diag.Summary?.GetTotalCallCount() ?? 0,
                RawJson = e.RawJson
            })
            .OrderByDescending(e => e.Duration)
            .ToList();

        // Operation buckets
        result.OperationBuckets = highLatencyDiags
            .Where(e => e.Diag.Name != null)
            .GroupBy(e => e.Diag.Name)
            .Select(g => new OperationBucket
            {
                Bucket = g.Key,
                Min = g.Min(x => x.Diag.Duration),
                Max = g.Max(x => x.Diag.Duration),
                MinNWCount = g.Min(x => x.Diag.Summary?.GetDirectCallCount() ?? 0),
                MaxNWCount = g.Max(x => x.Diag.Summary?.GetDirectCallCount() ?? 0),
                Count = g.Count(),
            })
            .OrderByDescending(e => e.Count)
            .ToList();

        var highCountOpName = result.OperationBuckets.FirstOrDefault()?.Bucket;
        if (highCountOpName == null)
        {
            return result;
        }

        // Filter to target operation
        var targetDiagnostics = diagnostics.Where(e => e.Diag.Name == highCountOpName).ToArray();

        // Store response statistics with raw JSON
        var storeResponseStatistics = targetDiagnostics
            .Where(e => e.Diag.Recursive() != null)
            .SelectMany(e => e.Diag.Recursive().Select(r => new { Response = r, RawJson = e.RawJson }))
            .Select(e => new { Stats = e.Response.Data?.ClientSideRequestStats, e.RawJson })
            .Where(e => e.Stats?.StoreResponseStatistics != null)
            .SelectMany(e => e.Stats!.StoreResponseStatistics!.Select(s => new { Store = s, e.RawJson }))
            .Where(e => e.Store.StoreResult != null)
            .ToList();

        var nwInteractions = storeResponseStatistics
            .Where(e => e.Store.StoreResult?.StorePhysicalAddress != null)
            .Select(e => new NetworkInteraction
            {
                ResourceType = e.Store.ResourceType,
                OperationType = e.Store.OperationType,
                StatusCode = e.Store.StoreResult!.StatusCode,
                SubStatusCode = e.Store.StoreResult.SubStatusCode,
                DurationInMs = e.Store.DurationInMs,
                Created = e.Store.StoreResult.TransportRequestTimeline?.Created,
                ChannelAcquisitionStarted = e.Store.StoreResult.TransportRequestTimeline?.ChannelAcquisitionStarted,
                Pipelined = e.Store.StoreResult.TransportRequestTimeline?.Pipelined,
                TransitTime = e.Store.StoreResult.TransportRequestTimeline?.TransitTime,
                Received = e.Store.StoreResult.TransportRequestTimeline?.Received,
                Completed = e.Store.StoreResult.TransportRequestTimeline?.Completed,
                BELatencyInMs = e.Store.StoreResult.BELatencyInMs,
                InflightRequests = e.Store.StoreResult.TransportRequestTimeline?.serviceEndpointStats?.inflightRequests,
                OpenConnections = e.Store.StoreResult.TransportRequestTimeline?.serviceEndpointStats?.openConnections,
                CallsPendingReceive = e.Store.StoreResult.TransportRequestTimeline?.connectionStats?.callsPendingReceive,
                WaitForConnectionInit = e.Store.StoreResult.TransportRequestTimeline?.connectionStats?.waitforConnectionInit,
                LastEvent = e.Store.StoreResult.TransportRequestTimeline?.GetLastEvent(),
                BottleneckEventName = e.Store.StoreResult.TransportRequestTimeline?.GetBottleneckEvent()?.Name,
                BottleneckEventDuration = e.Store.StoreResult.TransportRequestTimeline?.GetBottleneckEvent()?.DurationInMs,
                PartitionId = e.Store.StoreResult.PartitionId,
                ReplicaId = e.Store.StoreResult.ReplicaId,
                TenantId = e.Store.StoreResult.TenantId,
                StorePhysicalAddress = e.Store.StoreResult.StorePhysicalAddress,
                RawJson = e.RawJson
            })
            .ToList();

        var highLatencyNWInteractions = nwInteractions
            .Where(e => e.DurationInMs > latencyThreshold)
            .OrderByDescending(e => e.DurationInMs)
            .ToList();

        result.HighLatencyNetworkInteractions = highLatencyNWInteractions.Take(100).ToList();

        // Group by Resource Type -> Operation Type
        result.ResourceTypeGroups = highLatencyNWInteractions
            .GroupBy(e => $"{e.ResourceType} -> {e.OperationType}")
            .Select(e => new GroupedResult 
            { 
                Key = e.Key, 
                Count = e.Count(),
                Entries = e.OrderByDescending(x => x.DurationInMs)
                    .Take(50)
                    .Select(x => new GroupedEntry
                    {
                        DurationInMs = x.DurationInMs,
                        StatusCode = x.StatusCode,
                        SubStatusCode = x.SubStatusCode,
                        ResourceType = x.ResourceType,
                        OperationType = x.OperationType,
                        RawJson = x.RawJson
                    })
                    .ToList()
            })
            .OrderByDescending(e => e.Count)
            .ToList();

        // Group by Status Code -> Sub Status Code
        result.StatusCodeGroups = highLatencyNWInteractions
            .GroupBy(e => $"{e.StatusCode} -> {e.SubStatusCode}")
            .Select(e => new GroupedResult 
            { 
                Key = e.Key, 
                Count = e.Count(),
                Entries = e.OrderByDescending(x => x.DurationInMs)
                    .Take(50)
                    .Select(x => new GroupedEntry
                    {
                        DurationInMs = x.DurationInMs,
                        StatusCode = x.StatusCode,
                        SubStatusCode = x.SubStatusCode,
                        ResourceType = x.ResourceType,
                        OperationType = x.OperationType,
                        RawJson = x.RawJson
                    })
                    .ToList()
            })
            .OrderByDescending(e => e.Count)
            .ToList();

        // Group by Transport Event
        result.TransportEventGroups = highLatencyNWInteractions
            .GroupBy(e => e.LastEvent ?? TransportEvents.Unknown)
            .Select(e => new TransportEventGroup
            {
                Status = e.Key,
                Count = e.Count(),
                Entries = e.OrderByDescending(x => x.DurationInMs)
                    .Take(50)
                    .Select(x => new GroupedEntry
                    {
                        DurationInMs = x.DurationInMs,
                        StatusCode = x.StatusCode,
                        SubStatusCode = x.SubStatusCode,
                        ResourceType = x.ResourceType,
                        OperationType = x.OperationType,
                        RawJson = x.RawJson
                    })
                    .ToList(),
                PhaseDetails = e.GroupBy(x => x.BottleneckEventName)
                    .Select(g => new PhaseDetail
                    {
                        Phase = g.Key,
                        Count = g.Count(),
                        MinDuration = g.Min(y => y.DurationInMs),
                        MaxDuration = g.Max(y => y.DurationInMs),
                        MinStartTime = null, // Simplified for now
                        MaxStartTime = null,
                        EndpointCount = g.Where(y => y.StorePhysicalAddress != null)
                            .Select(y => new Uri(y.StorePhysicalAddress!).Authority)
                            .Distinct().Count(),
                        Top10Endpoints = g.Where(y => y.StorePhysicalAddress != null)
                            .GroupBy(y => new Uri(y.StorePhysicalAddress!).Authority)
                            .Select(y => new EndpointCount { Endpoint = y.Key, Count = y.Count() })
                            .OrderByDescending(y => y.Count)
                            .Take(10)
                            .ToList()
                    })
                    .ToList()
            })
            .OrderByDescending(e => e.Count)
            .ToList();

        return result;
    }

    #region JSON Repair Logic

    public static T? TryDeserializePartial<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        // First try direct deserialization
        try
        {
            return JsonSerializer.Deserialize<T>(json, LenientOptions);
        }
        catch (JsonException)
        {
            // Continue to repair
        }

        // Try with progressively more aggressive truncation
        string currentJson = json;

        for (int attempt = 0; attempt < 20; attempt++)
        {
            string? repairedJson = RepairAndCloseJson(currentJson);
            if (string.IsNullOrWhiteSpace(repairedJson))
                return null;

            try
            {
                return JsonSerializer.Deserialize<T>(repairedJson, LenientOptions);
            }
            catch (JsonException ex)
            {
                long? errorPos = ex.BytePositionInLine;

                if (errorPos.HasValue && errorPos.Value > 0 && errorPos.Value < currentJson.Length)
                {
                    currentJson = currentJson[..(int)errorPos.Value];
                    continue;
                }

                int truncateAt = -1;
                for (int i = currentJson.Length - 1; i >= 0; i--)
                {
                    char c = currentJson[i];
                    if (c == ',' || c == '}' || c == ']')
                    {
                        truncateAt = i;
                        break;
                    }
                }

                if (truncateAt > 0)
                {
                    currentJson = currentJson[truncateAt] == ','
                        ? currentJson[..truncateAt]
                        : currentJson[..(truncateAt + 1)];
                    continue;
                }

                return null;
            }
        }

        return null;
    }

    private static string? RepairAndCloseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var sb = new StringBuilder(json.TrimEnd());

        // Remove trailing "..." truncation marker
        while (sb.Length >= 3 && sb.ToString().EndsWith("..."))
        {
            sb.Length -= 3;
        }

        // Remove trailing incomplete tokens
        while (sb.Length > 0)
        {
            char last = sb[sb.Length - 1];
            if (last == ',' || last == ':' || last == '.' || char.IsWhiteSpace(last))
                sb.Length--;
            else
                break;
        }

        // Check for unclosed string
        string current = sb.ToString();
        if (CountUnescapedQuotes(current) % 2 == 1)
        {
            int lastQuote = current.LastIndexOf('"');
            if (lastQuote > 0)
            {
                sb.Length = lastQuote;
            }
        }

        // Trim again after quote removal
        current = sb.ToString().TrimEnd();
        sb.Clear();
        sb.Append(current);

        while (sb.Length > 0)
        {
            char last = sb[sb.Length - 1];
            if (last == ',' || last == ':' || char.IsWhiteSpace(last))
                sb.Length--;
            else
                break;
        }

        // Check for incomplete property name
        current = sb.ToString();
        if (current.Length > 0 && current[^1] == '"')
        {
            int closeQuote = current.Length - 1;
            int openQuote = FindMatchingOpenQuote(current, closeQuote);

            if (openQuote > 0)
            {
                string before = current[..openQuote].TrimEnd();
                if (before.Length > 0 && (before[^1] == '{' || before[^1] == ',' || before[^1] == '['))
                {
                    sb.Length = openQuote;
                    while (sb.Length > 0)
                    {
                        char last = sb[sb.Length - 1];
                        if (last == ',' || char.IsWhiteSpace(last))
                            sb.Length--;
                        else
                            break;
                    }
                }
            }
        }

        // Track and close unclosed structures
        var unclosedStructures = new Stack<char>();
        bool inString = false;
        bool escaped = false;

        foreach (char c in sb.ToString())
        {
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (!inString)
            {
                if (c == '{') unclosedStructures.Push('{');
                else if (c == '}' && unclosedStructures.Count > 0 && unclosedStructures.Peek() == '{')
                    unclosedStructures.Pop();
                else if (c == '[') unclosedStructures.Push('[');
                else if (c == ']' && unclosedStructures.Count > 0 && unclosedStructures.Peek() == '[')
                    unclosedStructures.Pop();
            }
        }

        while (unclosedStructures.Count > 0)
        {
            char open = unclosedStructures.Pop();
            sb.Append(open == '{' ? '}' : ']');
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static int FindMatchingOpenQuote(string s, int closeQuoteIndex)
    {
        for (int i = closeQuoteIndex - 1; i >= 0; i--)
        {
            if (s[i] == '"')
            {
                int backslashCount = 0;
                for (int j = i - 1; j >= 0 && s[j] == '\\'; j--)
                    backslashCount++;

                if (backslashCount % 2 == 0)
                    return i;
            }
        }
        return -1;
    }

    private static int CountUnescapedQuotes(string s)
    {
        int count = 0;
        bool escaped = false;
        foreach (char c in s)
        {
            if (escaped) { escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (c == '"') count++;
        }
        return count;
    }

    #endregion
}
