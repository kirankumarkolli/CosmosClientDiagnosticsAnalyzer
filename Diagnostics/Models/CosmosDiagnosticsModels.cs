using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Diagnostics.Models;

public class CosmosDiagnostics
{
    public DiagSummary? Summary { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("start time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("duration in milliseconds")]
    public double Duration { get; set; }

    [JsonPropertyName("children")]
    public ChildrenSpan[]? Children { get; set; }

    public IEnumerable<ChildrenSpan> Recursive()
    {
        if (this.Children == null) return Array.Empty<ChildrenSpan>();
        return this.Children.SelectMany(e => e.Recursive());
    }
}

public class DiagSummary
{
    public ExpandoObject? DirectCalls { get; set; }
    public ExpandoObject? GatewayCalls { get; set; }

    public int GetDirectCallCount()
    {
        if (this.DirectCalls == null) return 0;
        return this.DirectCalls.Select(y => ((JsonElement)(y.Value!)).GetInt32()).Sum();
    }

    public int GetGatewayCallCount()
    {
        if (this.GatewayCalls == null) return 0;
        return this.GatewayCalls.Select(y => ((JsonElement)(y.Value!)).GetInt32()).Sum();
    }

    public int GetTotalCallCount()
    {
        return this.GetDirectCallCount() + this.GetGatewayCallCount();
    }
}

public class ChildrenSpan
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("start time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("duration in milliseconds")]
    public double DurationInMs { get; set; }

    [JsonPropertyName("children")]
    public ChildrenSpan[]? Children { get; set; }

    [JsonPropertyName("data")]
    public ChildData? Data { get; set; }

    public IEnumerable<ChildrenSpan> Recursive()
    {
        yield return this;
        if (this.Children != null)
            foreach (var e in this.Children.SelectMany(e => e.Recursive()))
                yield return e;
    }
}

public class ChildData
{
    [JsonPropertyName("Client Side Request Stats")]
    public ClientSideRequestStats? ClientSideRequestStats { get; set; }

    [JsonPropertyName("Query Metrics")]
    public string? QueryMetrics { get; set; }
    
    [JsonPropertyName("System Info")]
    public SystemInfo[]? SystemInfo { get; set; }
}

public class ClientSideRequestStats
{
    [JsonPropertyName("AddressResolutionStatistics")]
    public AddressResolutionStatistics[]? AddressResolutionStatistics { get; set; }

    [JsonPropertyName("StoreResponseStatistics")]
    public StoreResponseStatistics[]? StoreResponseStatistics { get; set; }
    
    [JsonPropertyName("SystemInfo")]
    public SystemInfo[]? SystemInfo { get; set; }
    
    [JsonPropertyName("systemInfo")]
    public SystemInfo[]? SystemInfoLower { get; set; }
}

// System info snapshot from diagnostics JSON
public class SystemInfo
{
    [JsonPropertyName("dateUtc")]
    public DateTime DateUtc { get; set; }
    
    [JsonPropertyName("cpu")]
    public double Cpu { get; set; }
    
    [JsonPropertyName("memory")]
    public long Memory { get; set; }
    
    [JsonPropertyName("threadInfo")]
    public ThreadInfo? ThreadInfo { get; set; }
    
    [JsonPropertyName("numberOfOpenTcpConnection")]
    public int NumberOfOpenTcpConnection { get; set; }
}

public class ThreadInfo
{
    [JsonPropertyName("isThreadStarving")]
    public string? IsThreadStarving { get; set; }
    
    [JsonPropertyName("threadWaitIntervalInMs")]
    public double ThreadWaitIntervalInMs { get; set; }
    
    [JsonPropertyName("availableThreads")]
    public int AvailableThreads { get; set; }
    
    [JsonPropertyName("minThreads")]
    public int MinThreads { get; set; }
    
    [JsonPropertyName("maxThreads")]
    public int MaxThreads { get; set; }
}


public class AddressResolutionStatistics
{
    [JsonPropertyName("StartTimeUTC")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("EndTimeUTC")]
    public DateTime EndTimeUTC { get; set; }

    [JsonPropertyName("TargetEndpoint")]
    public string? TargetEndpoint { get; set; }
}

public class StoreResponseStatistics
{
    public string? ResourceType { get; set; }
    public string? OperationType { get; set; }

    [JsonPropertyName("DurationInMs")]
    public double DurationInMs { get; set; }

    [JsonPropertyName("StoreResult")]
    public StoreResult? StoreResult { get; set; }
}

public class StoreResult
{
    [JsonPropertyName("StatusCode")]
    public string? StatusCode { get; set; }

    [JsonPropertyName("SubStatusCode")]
    public string? SubStatusCode { get; set; }

    [JsonPropertyName("TransportException")]
    public string? TransportException { get; set; }

    [JsonPropertyName("StorePhysicalAddress")]
    public string? StorePhysicalAddress { get; set; }

    [JsonPropertyName("BELatencyInMs")]
    public string? BELatencyInMs { get; set; }

    [JsonPropertyName("transportRequestTimeline")]
    public TransportRequestTimeline? TransportRequestTimeline { get; set; }

    public string? PartitionId => StorePhysicalAddress != null ? new Uri(StorePhysicalAddress).PathAndQuery.Split('/')[6] : null;
    public string? ReplicaId => StorePhysicalAddress != null ? new Uri(StorePhysicalAddress).PathAndQuery.Split('/')[8] : null;
    public string? TenantId => StorePhysicalAddress != null ? new Uri(StorePhysicalAddress).Host : null;
}

public class EndpointStats
{
    public int inflightRequests { get; set; }
    public int openConnections { get; set; }
}

public class ConnectionStats
{
    public string? waitforConnectionInit { get; set; }
    public int callsPendingReceive { get; set; }
    public DateTime lastSendAttempt { get; set; }
    public DateTime lastSend { get; set; }
    public DateTime lastReceive { get; set; }
}

public class TransportRequestTimeline
{
    [JsonPropertyName("requestTimeline")]
    public EventTime[]? RequestTimeline { get; set; }

    public EndpointStats? serviceEndpointStats { get; set; }
    public ConnectionStats? connectionStats { get; set; }

    public double? Created => RequestTimeline?.FirstOrDefault(e => e.Name == "Created")?.DurationInMs;
    public double? ChannelAcquisitionStarted => RequestTimeline?.FirstOrDefault(e => e.Name == "ChannelAcquisitionStarted")?.DurationInMs;
    public double? Pipelined => RequestTimeline?.FirstOrDefault(e => e.Name == "Pipelined")?.DurationInMs;
    public double? Received => RequestTimeline?.FirstOrDefault(e => e.Name == "Received")?.DurationInMs;
    public double? TransitTime => RequestTimeline?.FirstOrDefault(e => e.Name == "Transit Time")?.DurationInMs;
    public double? Completed => RequestTimeline?.FirstOrDefault(e => e.Name == "Completed")?.DurationInMs;

    public TransportEvents GetLastEvent()
    {
        if (RequestTimeline?.FirstOrDefault(e => e.Name == "Completed") != null) return TransportEvents.Completed;
        if (RequestTimeline?.FirstOrDefault(e => e.Name == "Received") != null) return TransportEvents.Received;
        if (RequestTimeline?.FirstOrDefault(e => e.Name == "Transit Time") != null) return TransportEvents.TransitTime;
        if (RequestTimeline?.FirstOrDefault(e => e.Name == "Pipelined") != null) return TransportEvents.Pipelined;
        if (RequestTimeline?.FirstOrDefault(e => e.Name == "ChannelAcquisitionStarted") != null) return TransportEvents.ChannelAcquisitionStarted;
        if (RequestTimeline?.FirstOrDefault(e => e.Name == "Created") != null) return TransportEvents.Created;
        return TransportEvents.Unknown;
    }

    public EventTime? GetBottleneckEvent() => RequestTimeline?.MaxBy(e => e.DurationInMs);
}

public enum TransportEvents
{
    Created,
    ChannelAcquisitionStarted,
    Pipelined,
    TransitTime,
    Received,
    Completed,
    Unknown
}

public class EventTime
{
    [JsonPropertyName("event")]
    public string? Name { get; set; }

    [JsonPropertyName("startTimeUtc")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("durationInMs")]
    public double DurationInMs { get; set; }
}
