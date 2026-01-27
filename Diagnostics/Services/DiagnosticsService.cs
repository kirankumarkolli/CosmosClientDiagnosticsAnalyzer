using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

    // Regex to extract error code from TransportException
    private static readonly Regex ErrorCodeRegex = new(@"error code:\s*(\w+(?:\s*\[0x[0-9A-Fa-f]+\])?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses TransportException to extract the message (up to "Time:") and error code
    /// </summary>
    private static (string? Message, string? ErrorCode) ParseTransportException(string? exception)
    {
        if (string.IsNullOrEmpty(exception))
            return (null, null);

        // Extract message up to "(Time:" or just the first part
        string message = exception;
        var timeIndex = exception.IndexOf("(Time:", StringComparison.OrdinalIgnoreCase);
        if (timeIndex > 0)
        {
            message = exception.Substring(0, timeIndex).Trim();
        }

        // Extract error code using regex
        string? errorCode = null;
        var match = ErrorCodeRegex.Match(exception);
        if (match.Success)
        {
            errorCode = match.Groups[1].Value;
        }

        return (message, errorCode);
    }

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

        // Operation buckets with percentiles
        result.OperationBuckets = highLatencyDiags
            .Where(e => e.Diag.Name != null)
            .GroupBy(e => e.Diag.Name)
            .Select(g => {
                var durations = g.Select(x => x.Diag.Duration).OrderBy(x => x).ToList();
                return new OperationBucket
                {
                    Bucket = g.Key,
                    Min = durations.First(),
                    P50 = GetPercentile(durations, 50),
                    P75 = GetPercentile(durations, 75),
                    P90 = GetPercentile(durations, 90),
                    P95 = GetPercentile(durations, 95),
                    Max = durations.Last(),
                    MinNWCount = g.Min(x => x.Diag.Summary?.GetDirectCallCount() ?? 0),
                    MaxNWCount = g.Max(x => x.Diag.Summary?.GetDirectCallCount() ?? 0),
                    Count = g.Count(),
                };
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
            .Select(e => {
                var (exceptionMessage, errorCode) = ParseTransportException(e.Store.StoreResult!.TransportException);
                return new NetworkInteraction
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
                    TransportException = e.Store.StoreResult.TransportException,
                    TransportExceptionMessage = exceptionMessage,
                    TransportErrorCode = errorCode,
                    RawJson = e.RawJson
                };
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
            .Select(e => {
                var durations = e.Select(x => x.DurationInMs).OrderBy(x => x).ToList();
                var items = e.ToList();
                var p50 = GetPercentile(durations, 50);
                var p75 = GetPercentile(durations, 75);
                var p90 = GetPercentile(durations, 90);
                var p95 = GetPercentile(durations, 95);
                return new GroupedResult 
                { 
                    Key = e.Key, 
                    Count = e.Count(),
                    Min = durations.First(),
                    P50 = p50,
                    P75 = p75,
                    P90 = p90,
                    P95 = p95,
                    Max = durations.Last(),
                    Entries = items.OrderByDescending(x => x.DurationInMs)
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
                    // P50: values ? P50
                    EntriesAtP50 = GetEntriesInPercentileRange(items, null, p50),
                    // P75: values > P50 and ? P75
                    EntriesAtP75 = GetEntriesInPercentileRange(items, p50, p75),
                    // P90: values > P75 and ? P90
                    EntriesAtP90 = GetEntriesInPercentileRange(items, p75, p90),
                    // P95: values > P90 and ? P95
                    EntriesAtP95 = GetEntriesInPercentileRange(items, p90, p95)
                };
            })
            .OrderByDescending(e => e.Count)
            .ToList();

        // Group by Status Code -> Sub Status Code
        result.StatusCodeGroups = highLatencyNWInteractions
            .GroupBy(e => $"{e.StatusCode} -> {e.SubStatusCode}")
            .Select(e => {
                var durations = e.Select(x => x.DurationInMs).OrderBy(x => x).ToList();
                var items = e.ToList();
                var p50 = GetPercentile(durations, 50);
                var p75 = GetPercentile(durations, 75);
                var p90 = GetPercentile(durations, 90);
                var p95 = GetPercentile(durations, 95);
                return new GroupedResult 
                { 
                    Key = e.Key, 
                    Count = e.Count(),
                    Min = durations.First(),
                    P50 = p50,
                    P75 = p75,
                    P90 = p90,
                    P95 = p95,
                    Max = durations.Last(),
                    Entries = items.OrderByDescending(x => x.DurationInMs)
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
                    // P50: values ? P50
                    EntriesAtP50 = GetEntriesInPercentileRange(items, null, p50),
                    // P75: values > P50 and ? P75
                    EntriesAtP75 = GetEntriesInPercentileRange(items, p50, p75),
                    // P90: values > P75 and ? P90
                    EntriesAtP90 = GetEntriesInPercentileRange(items, p75, p90),
                    // P95: values > P90 and ? P95
                    EntriesAtP95 = GetEntriesInPercentileRange(items, p90, p95)
                };
            })
            .OrderByDescending(e => e.Count)
            .ToList();

        // Group by Transport Exception (using parsed message without timestamp)
        result.TransportExceptionGroups = highLatencyNWInteractions
            .Where(e => !string.IsNullOrEmpty(e.TransportExceptionMessage))
            .GroupBy(e => e.TransportExceptionMessage ?? "Unknown")
            .Select(e => {
                var durations = e.Select(x => x.DurationInMs).OrderBy(x => x).ToList();
                var items = e.ToList();
                var p50 = GetPercentile(durations, 50);
                var p75 = GetPercentile(durations, 75);
                var p90 = GetPercentile(durations, 90);
                var p95 = GetPercentile(durations, 95);
                return new GroupedResult 
                { 
                    Key = e.Key, 
                    Count = e.Count(),
                    Min = durations.First(),
                    P50 = p50,
                    P75 = p75,
                    P90 = p90,
                    P95 = p95,
                    Max = durations.Last(),
                    Entries = items.OrderByDescending(x => x.DurationInMs)
                        .Take(50)
                        .Select(x => new GroupedEntry
                        {
                            DurationInMs = x.DurationInMs,
                            StatusCode = x.StatusCode,
                            SubStatusCode = x.SubStatusCode,
                            ResourceType = x.ResourceType,
                            OperationType = x.OperationType,
                            TransportErrorCode = x.TransportErrorCode,
                            RawJson = x.RawJson
                        })
                        .ToList(),
                    // P50: values ? P50
                    EntriesAtP50 = GetEntriesInPercentileRange(items, null, p50),
                    // P75: values > P50 and ? P75
                    EntriesAtP75 = GetEntriesInPercentileRange(items, p50, p75),
                    // P90: values > P75 and ? P90
                    EntriesAtP90 = GetEntriesInPercentileRange(items, p75, p90),
                    // P95: values > P90 and ? P95
                    EntriesAtP95 = GetEntriesInPercentileRange(items, p90, p95)
                };
            })
            .OrderByDescending(e => e.Count)
            .ToList();


        // Group by Transport Event
        result.TransportEventGroups = highLatencyNWInteractions
            .GroupBy(e => e.LastEvent ?? TransportEvents.Unknown)
            .Select(e => {
                var durations = e.Select(x => x.DurationInMs).OrderBy(x => x).ToList();
                var items = e.ToList();
                var p50 = GetPercentile(durations, 50);
                var p75 = GetPercentile(durations, 75);
                var p90 = GetPercentile(durations, 90);
                var p95 = GetPercentile(durations, 95);
                return new TransportEventGroup
                {
                    Status = e.Key,
                    Count = e.Count(),
                    Min = durations.First(),
                    P50 = p50,
                    P75 = p75,
                    P90 = p90,
                    P95 = p95,
                    Max = durations.Last(),
                    Entries = items.OrderByDescending(x => x.DurationInMs)
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
                    // P50: values ? P50
                    EntriesAtP50 = GetEntriesInPercentileRange(items, null, p50),
                    // P75: values > P50 and ? P75
                    EntriesAtP75 = GetEntriesInPercentileRange(items, p50, p75),
                    // P90: values > P75 and ? P90
                    EntriesAtP90 = GetEntriesInPercentileRange(items, p75, p90),
                    // P95: values > P90 and ? P95
                    EntriesAtP95 = GetEntriesInPercentileRange(items, p90, p95),
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
                            .ToList(),
                        Entries = g.OrderByDescending(y => y.DurationInMs)
                            .Take(50)
                            .Select(y => new GroupedEntry
                            {
                                DurationInMs = y.DurationInMs,
                                StatusCode = y.StatusCode,
                                SubStatusCode = y.SubStatusCode,
                                ResourceType = y.ResourceType,
                                OperationType = y.OperationType,
                                RawJson = y.RawJson
                            })
                            .ToList()
                    })
                    .ToList()
                };
            })
            .OrderByDescending(e => e.Count)
            .ToList();

        // Parse System Info metrics from all diagnostics
        result.SystemMetrics = ParseSystemMetrics(diagnostics);
        
        // Parse Client Configuration metrics from all diagnostics
        result.ClientConfigMetrics = ParseClientConfigMetrics(diagnostics);

        return result;
    }

    /// <summary>
    /// Parse and aggregate client configuration metrics from all diagnostics entries
    /// </summary>
    private ClientConfigTimePlot? ParseClientConfigMetrics((CosmosDiagnostics Diag, string RawJson)[] diagnostics)
    {
        var allSnapshots = new List<ClientConfigSnapshot>();

        foreach (var (diag, _) in diagnostics)
        {
            var clientConfig = diag.Data?.ClientConfiguration;
            if (clientConfig == null) continue;
            
            // Parse the start time from the diagnostics
            DateTime dateUtc = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(diag.StartDateTime) && DateTime.TryParse(diag.StartDateTime, out var parsed1))
            {
                dateUtc = parsed1;
            }
            else if (!string.IsNullOrEmpty(diag.StartTime) && DateTime.TryParse(diag.StartTime, out var parsed2))
            {
                dateUtc = parsed2;
            }
            
            // Extract short machine ID (last 8 chars of the GUID part)
            var machineId = clientConfig.MachineId ?? "unknown";
            var shortMachineId = machineId.Length > 8 ? machineId.Substring(machineId.Length - 8) : machineId;
            
            allSnapshots.Add(new ClientConfigSnapshot
            {
                DateUtc = dateUtc,
                MachineId = machineId,
                ShortMachineId = shortMachineId,
                ProcessorCount = clientConfig.ProcessorCount,
                NumberOfClientsCreated = clientConfig.NumberOfClientsCreated,
                NumberOfActiveClients = clientConfig.NumberOfActiveClients,
                ConnectionMode = clientConfig.ConnectionMode
            });
        }

        if (!allSnapshots.Any())
            return null;

        // Order by time
        var orderedSnapshots = allSnapshots
            .OrderBy(s => s.DateUtc)
            .ToList();

        var result = new ClientConfigTimePlot
        {
            SampleCount = orderedSnapshots.Count,
            StartTime = orderedSnapshots.First().DateUtc,
            EndTime = orderedSnapshots.Last().DateUtc,
            UniqueMachineIds = orderedSnapshots.Select(s => s.MachineId!).Distinct().ToList(),
            Snapshots = orderedSnapshots.Take(500).ToList() // Keep top 500 for display
        };

        // Calculate ProcessorCount statistics
        var processorValues = orderedSnapshots.Select(s => (double)s.ProcessorCount).OrderBy(v => v).ToList();
        result.ProcessorCount = CalculateMetricStatistics(processorValues);

        // Calculate NumberOfClientsCreated statistics
        var clientsCreatedValues = orderedSnapshots.Select(s => (double)s.NumberOfClientsCreated).OrderBy(v => v).ToList();
        result.NumberOfClientsCreated = CalculateMetricStatistics(clientsCreatedValues);

        // Calculate NumberOfActiveClients statistics
        var activeClientsValues = orderedSnapshots.Select(s => (double)s.NumberOfActiveClients).OrderBy(v => v).ToList();
        result.NumberOfActiveClients = CalculateMetricStatistics(activeClientsValues);

        return result;
    }

    /// <summary>
    /// Calculate all statistics for a metric including percentiles
    /// </summary>
    private MetricStatistics CalculateMetricStatistics(List<double> sortedValues)
    {
        if (sortedValues == null || !sortedValues.Any())
            return new MetricStatistics();
            
        return new MetricStatistics
        {
            Min = sortedValues.First(),
            P50 = GetPercentile(sortedValues, 50),
            P75 = GetPercentile(sortedValues, 75),
            P90 = GetPercentile(sortedValues, 90),
            P95 = GetPercentile(sortedValues, 95),
            Max = sortedValues.Last(),
            Avg = sortedValues.Average()
        };
    }

    /// <summary>
    /// Parse and aggregate system metrics from all diagnostics entries
    /// </summary>
    private SystemMetricsTimePlot? ParseSystemMetrics((CosmosDiagnostics Diag, string RawJson)[] diagnostics)
    {
        var allSnapshots = new List<SystemInfoSnapshot>();

        foreach (var (diag, _) in diagnostics)
        {
            // Get SystemInfo from children data (ChildData["System Info"].systemHistory)
            var systemHistoryFromChildData = diag.Recursive()
                .Where(c => c.Data?.SystemInfo?.SystemHistory != null)
                .SelectMany(c => c.Data!.SystemInfo!.SystemHistory!);

            foreach (var info in systemHistoryFromChildData)
            {
                AddSystemInfoSnapshot(allSnapshots, info);
            }
            
            // Get SystemInfo from ClientSideRequestStats (ClientSideRequestStats.SystemInfo.systemHistory)
            var systemHistoryFromClientStats = diag.Recursive()
                .Where(c => c.Data?.ClientSideRequestStats?.SystemInfo?.SystemHistory != null)
                .SelectMany(c => c.Data!.ClientSideRequestStats!.SystemInfo!.SystemHistory!);

            foreach (var info in systemHistoryFromClientStats)
            {
                AddSystemInfoSnapshot(allSnapshots, info);
            }
        }

        if (!allSnapshots.Any())
            return null;

        // Order by time and deduplicate by timestamp
        var orderedSnapshots = allSnapshots
            .GroupBy(s => s.DateUtc)
            .Select(g => g.First())
            .OrderBy(s => s.DateUtc)
            .ToList();

        var result = new SystemMetricsTimePlot
        {
            SampleCount = orderedSnapshots.Count,
            StartTime = orderedSnapshots.First().DateUtc,
            EndTime = orderedSnapshots.Last().DateUtc,
            Snapshots = orderedSnapshots.Take(100).ToList() // Keep top 100 for display
        };

        // Calculate CPU statistics
        var cpuValues = orderedSnapshots.Select(s => s.Cpu).OrderBy(v => v).ToList();
        result.Cpu = CalculateMetricStatistics(cpuValues);

        // Calculate Memory statistics
        var memoryValues = orderedSnapshots.Select(s => (double)s.Memory).OrderBy(v => v).ToList();
        result.Memory = CalculateMetricStatistics(memoryValues);

        // Calculate ThreadWaitIntervalInMs statistics
        var threadWaitValues = orderedSnapshots.Select(s => s.ThreadWaitIntervalInMs).OrderBy(v => v).ToList();
        result.ThreadWaitIntervalInMs = CalculateMetricStatistics(threadWaitValues);

        // Calculate TCP connection statistics
        var tcpValues = orderedSnapshots.Select(s => (double)s.NumberOfOpenTcpConnections).OrderBy(v => v).ToList();
        result.NumberOfOpenTcpConnections = CalculateMetricStatistics(tcpValues);

        return result;
    }


    /// <summary>
    /// Helper to add system info to snapshot list
    /// </summary>
    private static void AddSystemInfoSnapshot(List<SystemInfoSnapshot> snapshots, SystemInfoEntry info)
    {
        snapshots.Add(new SystemInfoSnapshot
        {
            DateUtc = info.DateUtc,
            Cpu = info.Cpu,
            Memory = (long)info.Memory,
            ThreadWaitIntervalInMs = info.ThreadInfo?.ThreadWaitIntervalInMs ?? 0,
            NumberOfOpenTcpConnections = info.NumberOfOpenTcpConnection,
            IsThreadStarving = info.ThreadInfo?.IsThreadStarving?.Equals("True", StringComparison.OrdinalIgnoreCase) ?? false,
            AvailableThreads = info.ThreadInfo?.AvailableThreads ?? 0,
            MinThreads = info.ThreadInfo?.MinThreads ?? 0,
            MaxThreads = info.ThreadInfo?.MaxThreads ?? 0
        });
    }

    /// <summary>
    /// Calculate percentile from a sorted list of values
    /// </summary>
    private static double GetPercentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues == null || sortedValues.Count == 0)
            return 0;

        if (sortedValues.Count == 1)
            return sortedValues[0];

        double index = (percentile / 100.0) * (sortedValues.Count - 1);
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);


        if (lower == upper)
            return sortedValues[lower];

        // Linear interpolation
        double weight = index - lower;
        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }




    /// <summary>
    /// Get entries within a percentile range (exclusive lower bound, inclusive upper bound)
    /// P50: values ? P50 (lowerBound = null)
    /// P75: values > P50 and ? P75
    /// P90: values > P75 and ? P90
    /// P95: values > P90 and ? P95
    /// </summary>
    private static List<GroupedEntry> GetEntriesInPercentileRange(IEnumerable<NetworkInteraction> items, double? lowerBound, double upperBound, int maxEntries = 50)
    {
        var query = items.AsEnumerable();
        
        if (lowerBound.HasValue)
        {
            query = query.Where(x => x.DurationInMs > lowerBound.Value);
        }
        
        query = query.Where(x => x.DurationInMs <= upperBound);

        return query
            .OrderByDescending(x => x.DurationInMs)
            .Take(maxEntries)
            .Select(x => new GroupedEntry
            {
                DurationInMs = x.DurationInMs,
                StatusCode = x.StatusCode,
                SubStatusCode = x.SubStatusCode,
                ResourceType = x.ResourceType,
                OperationType = x.OperationType,
                TransportErrorCode = x.TransportErrorCode,
                RawJson = x.RawJson
            })
            .ToList();
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
