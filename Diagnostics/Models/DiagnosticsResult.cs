namespace Diagnostics.Models;

public class DiagnosticsResult
{
    public int TotalEntries { get; set; }
    public int ParsedEntries { get; set; }
    public int RepairedEntries { get; set; }
    public int HighLatencyEntries { get; set; }
    public List<OperationBucket> OperationBuckets { get; set; } = new();
    public List<NetworkInteraction> HighLatencyNetworkInteractions { get; set; } = new();
    public List<GroupedResult> ResourceTypeGroups { get; set; } = new();
    public List<GroupedResult> StatusCodeGroups { get; set; } = new();
    public List<GroupedResult> TransportExceptionGroups { get; set; } = new();
    public List<TransportEventGroup> TransportEventGroups { get; set; } = new();
    
    
    // Store all high latency diagnostics for drill-down
    public List<DiagnosticEntry> AllHighLatencyDiagnostics { get; set; } = new();
    
    // System metrics time plot
    public SystemMetricsTimePlot? SystemMetrics { get; set; }
    
    // Client configuration metrics over time
    public ClientConfigTimePlot? ClientConfigMetrics { get; set; }
}

// Client configuration metrics over time
public class ClientConfigTimePlot
{
    public int SampleCount { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    
    // Unique machine IDs observed
    public List<string> UniqueMachineIds { get; set; } = new();
    
    // Processor count statistics
    public MetricStatistics ProcessorCount { get; set; } = new();
    
    // Number of clients created statistics
    public MetricStatistics NumberOfClientsCreated { get; set; } = new();
    
    // Number of active clients statistics
    public MetricStatistics NumberOfActiveClients { get; set; } = new();
    
    // Raw data points for chart
    public List<ClientConfigSnapshot> Snapshots { get; set; } = new();
}

public class ClientConfigSnapshot
{
    public DateTime DateUtc { get; set; }
    public string? MachineId { get; set; }
    public string? ShortMachineId { get; set; }  // Last 8 chars for display
    public int ProcessorCount { get; set; }
    public int NumberOfClientsCreated { get; set; }
    public int NumberOfActiveClients { get; set; }
    public string? ConnectionMode { get; set; }
}

// System metrics statistics over time
public class SystemMetricsTimePlot
{
    public int SampleCount { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    
    // CPU statistics (percentage)
    public MetricStatistics Cpu { get; set; } = new();
    
    // Memory statistics (bytes)
    public MetricStatistics Memory { get; set; } = new();
    
    // Thread wait interval statistics (ms)
    public MetricStatistics ThreadWaitIntervalInMs { get; set; } = new();
    
    // TCP connection count statistics
    public MetricStatistics NumberOfOpenTcpConnections { get; set; } = new();
    
    // Raw data points for detailed view
    public List<SystemInfoSnapshot> Snapshots { get; set; } = new();
}

public class MetricStatistics
{
    public double Min { get; set; }
    public double Max { get; set; }
    public double Avg { get; set; }
    public double P90 { get; set; }
}

public class SystemInfoSnapshot
{
    public DateTime DateUtc { get; set; }
    public double Cpu { get; set; }
    public long Memory { get; set; }
    public double ThreadWaitIntervalInMs { get; set; }
    public int NumberOfOpenTcpConnections { get; set; }
    public bool IsThreadStarving { get; set; }
    public int AvailableThreads { get; set; }
    public int MinThreads { get; set; }
    public int MaxThreads { get; set; }
}

public class DiagnosticEntry
{
    public string? Name { get; set; }
    public string? StartTime { get; set; }
    public double Duration { get; set; }
    public int DirectCallCount { get; set; }
    public int GatewayCallCount { get; set; }
    public int TotalCallCount { get; set; }
    public string? RawJson { get; set; }
}

public class OperationBucket
{
    public string? Bucket { get; set; }
    public double Min { get; set; }
    public double P50 { get; set; }
    public double P75 { get; set; }
    public double P90 { get; set; }
    public double P95 { get; set; }
    public double Max { get; set; }
    public int MinNWCount { get; set; }
    public int MaxNWCount { get; set; }
    public int Count { get; set; }
}




public class NetworkInteraction
{
    public string? ResourceType { get; set; }
    public string? OperationType { get; set; }
    public string? StatusCode { get; set; }
    public string? SubStatusCode { get; set; }
    public double DurationInMs { get; set; }
    public double? Created { get; set; }
    public double? ChannelAcquisitionStarted { get; set; }
    public double? Pipelined { get; set; }
    public double? TransitTime { get; set; }
    public double? Received { get; set; }
    public double? Completed { get; set; }
    public string? BELatencyInMs { get; set; }
    public int? InflightRequests { get; set; }
    public int? OpenConnections { get; set; }
    public int? CallsPendingReceive { get; set; }
    public string? WaitForConnectionInit { get; set; }
    public TransportEvents? LastEvent { get; set; }
    public string? BottleneckEventName { get; set; }
    public double? BottleneckEventDuration { get; set; }
    public string? PartitionId { get; set; }
    public string? ReplicaId { get; set; }
    public string? TenantId { get; set; }
    public string? StorePhysicalAddress { get; set; }
    public string? TransportException { get; set; }
    public string? TransportExceptionMessage { get; set; }
    public string? TransportErrorCode { get; set; }
    public string? RawJson { get; set; }
}

public class GroupedResult
{
    public string? Key { get; set; }
    public int Count { get; set; }
    public double Min { get; set; }
    public double P50 { get; set; }
    public double P75 { get; set; }
    public double P90 { get; set; }
    public double P95 { get; set; }
    public double Max { get; set; }
    public List<GroupedEntry> Entries { get; set; } = new();
    
    // Percentile-specific entries for drill-down
    public List<GroupedEntry> EntriesAtP50 { get; set; } = new();
    public List<GroupedEntry> EntriesAtP75 { get; set; } = new();
    public List<GroupedEntry> EntriesAtP90 { get; set; } = new();
    public List<GroupedEntry> EntriesAtP95 { get; set; } = new();
}

public class GroupedEntry
{
    public double DurationInMs { get; set; }
    public string? StatusCode { get; set; }
    public string? SubStatusCode { get; set; }
    public string? ResourceType { get; set; }
    public string? OperationType { get; set; }
    public string? TransportErrorCode { get; set; }
    public string? RawJson { get; set; }
}

public class TransportEventGroup
{
    public TransportEvents Status { get; set; }
    public int Count { get; set; }
    public double Min { get; set; }
    public double P50 { get; set; }
    public double P75 { get; set; }
    public double P90 { get; set; }
    public double P95 { get; set; }
    public double Max { get; set; }
    public List<PhaseDetail> PhaseDetails { get; set; } = new();
    public List<GroupedEntry> Entries { get; set; } = new();
    
    // Percentile-specific entries for drill-down
    public List<GroupedEntry> EntriesAtP50 { get; set; } = new();
    public List<GroupedEntry> EntriesAtP75 { get; set; } = new();
    public List<GroupedEntry> EntriesAtP90 { get; set; } = new();
    public List<GroupedEntry> EntriesAtP95 { get; set; } = new();
}

public class PhaseDetail
{
    public string? Phase { get; set; }
    public int Count { get; set; }
    public double MinDuration { get; set; }
    public double MaxDuration { get; set; }
    public DateTime? MinStartTime { get; set; }
    public DateTime? MaxStartTime { get; set; }
    public int EndpointCount { get; set; }
    public List<EndpointCount> Top10Endpoints { get; set; } = new();
    public List<GroupedEntry> Entries { get; set; } = new();
}

public class EndpointCount
{
    public string? Endpoint { get; set; }
    public int Count { get; set; }
}
