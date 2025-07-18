# NATS.Net API Findings and Integration Guide

## NATS.Net Client Library Analysis

### 1. Interface Hierarchy and Usage

#### Core Interfaces
- **`INatsClient`** - Base interface for NATS client operations
- **`INatsConnection`** - Extends `INatsClient` with additional connection-specific features
- **`NatsConnection`** - The concrete implementation class
- **`NatsClient`** - A simplified wrapper class in `NATS.Client.Simplified` namespace

#### Creating Connections
```csharp
// Direct connection creation (recommended for Wolverine)
await using var connection = new NatsConnection(options);

// Or using the simplified client
var client = new NatsClient(url: "nats://localhost:4222");
```

### 2. JetStream Context Creation

The `CreateJetStreamContext()` method is an **extension method** that requires the `NATS.Net` namespace:

```csharp
using NATS.Net; // REQUIRED for extension methods

// Extension methods available on both INatsClient and INatsConnection
var js = connection.CreateJetStreamContext();
// or with options
var js = connection.CreateJetStreamContext(new NatsJSOpts { ... });
// or direct instantiation
var js = new NatsJSContext(connection);
```

### 3. JetStream Message Acknowledgment

The `NatsJSMsg<T>` struct has these acknowledgment methods:

```csharp
public ValueTask AckAsync(AckOpts? opts = default, CancellationToken cancellationToken = default)
public ValueTask NakAsync(AckOpts? opts = default, TimeSpan delay = default, CancellationToken cancellationToken = default)
public ValueTask AckProgressAsync(AckOpts? opts = default, CancellationToken cancellationToken = default)
public ValueTask AckTerminateAsync(AckOpts? opts = default, CancellationToken cancellationToken = default)
```

### 4. Key API Corrections Needed

1. **Use `NatsConnection` instead of `INatsConnection` for the field type**
   - The concrete class provides all necessary functionality
   - Can still accept `INatsConnection` in constructors for testability

2. **Add `using NATS.Net;` to access JetStream extension methods**

3. **Message types use structs for performance**
   - `NatsMsg<T>` for core NATS
   - `NatsJSMsg<T>` for JetStream

## Rilian.Messaging.Nats Reusable Components

### 1. Subject Builder Pattern

Excellent builder pattern for constructing NATS subjects:

```csharp
public abstract class SubjectBuilderBase
{
    protected readonly List<string> _segments = new();
    
    protected T AddSegment(string segment)
    {
        if (!string.IsNullOrWhiteSpace(segment))
            _segments.Add(segment);
        return (T)this;
    }
    
    public T AddWildcard() => AddSegment("*");
    public T AddMultiWildcard() => AddSegment(">");
    
    public string Build() => string.Join(".", _segments);
}
```

**Adaptation for Wolverine:**
```csharp
public class WolverineSubjectBuilder : SubjectBuilderBase<WolverineSubjectBuilder>
{
    public WolverineSubjectBuilder AddEndpoint(string endpoint) 
        => AddSegment(SanitizeEndpointName(endpoint));
    
    public WolverineSubjectBuilder AddMessageType(Type messageType)
        => AddSegment(SanitizeMessageType(messageType.Name));
    
    private static string SanitizeEndpointName(string name)
        => name.Replace('/', '.').ToLowerInvariant();
    
    private static string SanitizeMessageType(string typeName)
        => Regex.Replace(typeName, @"[\\.>*\s]", "-").ToLowerInvariant();
}
```

### 2. Stream Configuration Builder

Fluent API for JetStream stream configuration:

```csharp
public class StreamConfigBuilderBase
{
    protected StreamConfig _config = new();
    
    public T WithName(string name)
    {
        _config.Name = name;
        return (T)this;
    }
    
    public T WithSubjects(params string[] subjects)
    {
        _config.Subjects = subjects;
        return (T)this;
    }
    
    public T WithRetention(StreamConfigRetention retention)
    {
        _config.Retention = retention;
        return (T)this;
    }
    
    // Default sensible values
    public T WithDefaultRetention()
    {
        _config.Retention = StreamConfigRetention.Limits;
        _config.MaxAge = TimeSpan.FromDays(7);
        _config.MaxBytes = 1_073_741_824; // 1GB
        return (T)this;
    }
}
```

### 3. Consumer Configuration Builder

Comprehensive consumer configuration with sensible defaults:

```csharp
public class ConsumerConfigBuilder
{
    public ConsumerConfigBuilder WithAckPolicy(ConsumerConfigAckPolicy policy)
    public ConsumerConfigBuilder WithAckWait(TimeSpan wait)
    public ConsumerConfigBuilder WithMaxDeliver(int maxDeliver)
    public ConsumerConfigBuilder WithBackoff(params TimeSpan[] backoff)
    public ConsumerConfigBuilder WithRateLimit(ulong msgsPerSecond)
}
```

### 4. Request-Reply Pattern with Retry

Clean implementation of request-reply with retry logic:

```csharp
public async Task<TResponse> RequestAsync<TRequest, TResponse>(
    string subject,
    TRequest request,
    int maxRetries = 3,
    TimeSpan? timeout = null,
    CancellationToken cancellationToken = default)
{
    var attempt = 0;
    while (attempt < maxRetries)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            
            var response = await connection.RequestAsync<TRequest, TResponse>(
                subject, request, cancellationToken: cts.Token);
                
            return response.Data;
        }
        catch (OperationCanceledException) when (attempt < maxRetries - 1)
        {
            attempt++;
            await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
        }
    }
    throw new TimeoutException($"Request to {subject} timed out after {maxRetries} attempts");
}
```

### 5. Message Wrapper Pattern

Consistent metadata handling across messages:

```csharp
public class MessageWrapper<T>
{
    public T Data { get; set; }
    public EventMetadata Metadata { get; set; }
}

public class EventMetadata
{
    public string EventId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Source { get; set; }
    public Dictionary<string, string> Headers { get; set; }
}
```

### 6. Naming Utilities

Subject and topic sanitization utilities:

```csharp
public static class NatsNamingUtilities
{
    // Convert Wolverine endpoint names to NATS subjects
    public static string NormalizeEndpointToSubject(string endpoint)
        => endpoint.Replace('/', '.').ToLowerInvariant();
    
    // Sanitize message type names for use in subjects
    public static string SanitizeMessageType(string typeName)
        => Regex.Replace(typeName, @"[\\.>*\s]", "-").ToLowerInvariant();
    
    // Validate subject patterns
    public static bool IsValidSubjectPattern(string subject)
        => !string.IsNullOrWhiteSpace(subject) && 
           !subject.Contains(" ") &&
           !subject.StartsWith(".") &&
           !subject.EndsWith(".");
}
```

## Integration Recommendations for Wolverine.Nats

### 1. Fix API Usage
```csharp
// In NatsTransport.cs
private NatsConnection? _connection; // Use concrete type
private INatsJSContext? _jetStreamContext;

public override async ValueTask InitializeAsync(IWolverineRuntime runtime)
{
    // ...
    _connection = new NatsConnection(natsOpts);
    await _connection.ConnectAsync();
    
    // Add using NATS.Net; at top of file
    _jetStreamContext = _connection.CreateJetStreamContext();
}
```

### 2. Handle Envelope Properties

Since Wolverine's Envelope properties have internal setters, we need to use factory methods or reflection:

```csharp
// Option 1: Use Envelope.ForPing() as a factory
public static Envelope CreateEnvelope(NatsMsg<byte[]> natsMsg)
{
    var envelope = Envelope.ForPing(runtime.Options.ServiceName);
    
    // Use reflection for internal setters
    var envelopeType = typeof(Envelope);
    envelopeType.GetProperty("Subject")?.SetValue(envelope, natsMsg.Subject);
    
    // Parse headers
    if (natsMsg.Headers != null)
    {
        if (natsMsg.Headers.TryGetValue("MessageId", out var messageId))
            envelopeType.GetProperty("Id")?.SetValue(envelope, Guid.Parse(messageId));
    }
    
    return envelope;
}

// Option 2: Create extension method in Wolverine namespace
public static class EnvelopeExtensions
{
    public static Envelope ToEnvelope(this NatsMsg<byte[]> natsMsg, IWolverineRuntime runtime)
    {
        // Implementation using reflection or internal knowledge
    }
}
```

### 3. Adopt Builder Patterns

Create Wolverine-specific builders that leverage the patterns from Rilian:

```csharp
public static class NatsConfigurationExtensions
{
    public static NatsListenerConfiguration ListenToSubject(this NatsTransport transport, string subject)
    {
        var builder = new WolverineSubjectBuilder()
            .AddSegment(subject);
            
        return transport.ListenTo(builder.Build());
    }
    
    public static NatsListenerConfiguration ListenToMessageType<T>(this NatsTransport transport)
    {
        var builder = new WolverineSubjectBuilder()
            .AddMessageType(typeof(T));
            
        return transport.ListenTo(builder.Build());
    }
}
```

### 4. Implement Retry Patterns

Use Wolverine's existing RetryBlock or adapt Rilian's pattern:

```csharp
public class NatsRetryPolicy
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    public double BackoffMultiplier { get; set; } = 2.0;
    
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        for (int i = 0; i < MaxAttempts; i++)
        {
            try
            {
                return await operation();
            }
            catch (Exception) when (i < MaxAttempts - 1)
            {
                var delay = TimeSpan.FromMilliseconds(InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, i));
                await Task.Delay(delay, ct);
            }
        }
        throw new InvalidOperationException($"Operation failed after {MaxAttempts} attempts");
    }
}
```

### 5. Create Shared Core Library Structure

Consider creating a `Wolverine.Nats.Core` library with:

```
Wolverine.Nats.Core/
├── Builders/
│   ├── SubjectBuilder.cs
│   ├── StreamConfigBuilder.cs
│   └── ConsumerConfigBuilder.cs
├── Utilities/
│   ├── NatsNamingUtilities.cs
│   └── EnvelopeMapper.cs
├── Patterns/
│   ├── RetryPolicy.cs
│   └── ConnectionProvider.cs
└── Configuration/
    └── NatsOptionsBuilder.cs
```

## Next Steps

1. **Fix immediate build issues** by correcting API usage
2. **Create envelope mapping strategy** using factory methods or reflection
3. **Extract reusable components** into shared utilities
4. **Implement proper error handling** with retry patterns
5. **Add comprehensive tests** using patterns from both libraries
6. **Document configuration examples** for common scenarios