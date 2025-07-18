# Wolverine Transport Implementation Guide

This document provides a comprehensive guide to implementing a transport for the Wolverine messaging framework.

## Core Transport Interfaces

### ITransport
The main interface that all transports must implement.

```csharp
public interface ITransport : IDisposable
{
    string Protocol { get; }
    string Name { get; }
    
    ValueTask InitializeAsync(IWolverineRuntime runtime);
    
    Endpoint ReplyEndpoint();
    IEnumerable<Endpoint> Endpoints();
    Endpoint GetOrCreateEndpoint(Uri uri);
    Endpoint GetOrCreateEndpoint(string endpointName);
    
    ValueTask<bool> TryBuildResponseSenderAsync(IMessagingRoot root, IChannelCallback callback);
}
```

### TransportBase<TEndpoint>
Abstract base class that provides common transport functionality.

```csharp
public abstract class TransportBase<TEndpoint> : ITransport where TEndpoint : Endpoint
{
    protected TransportBase(string protocol, string name)
    
    // Must override these methods:
    protected abstract IEnumerable<TEndpoint> endpoints();
    protected abstract TEndpoint findEndpointByUri(Uri uri);
    
    // Optional overrides:
    public virtual ValueTask InitializeAsync(IWolverineRuntime runtime)
    protected virtual void connectEndpoint(TEndpoint endpoint)
}
```

### Endpoint
Base class for all transport endpoints.

```csharp
public abstract class Endpoint : IDisposable
{
    // Constructor
    protected Endpoint(Uri uri, EndpointRole role)
    
    // Must implement:
    protected abstract bool supportsMode(EndpointMode mode);
    protected abstract ISender CreateSender(IWolverineRuntime runtime);
    
    // Optional overrides:
    public virtual async ValueTask<IListener> BuildListenerAsync(IWolverineRuntime runtime, IReceiver receiver)
    public virtual void Compile(IWolverineRuntime runtime)
    
    // Important properties:
    public Uri Uri { get; }
    public EndpointMode Mode { get; set; } // Durable, BufferedInMemory, or Inline
    public string EndpointName { get; set; }
    public bool IsListener { get; set; }
    public bool IsUsedForReplies { get; set; }
}
```

### IListener
Interface for receiving messages.

```csharp
public interface IListener : IAsyncDisposable, IDisposable
{
    Uri Address { get; }
    ValueTask CompleteAsync(Envelope envelope);
    ValueTask DeferAsync(Envelope envelope);
}
```

### ISender
Interface for sending messages.

```csharp
public interface ISender
{
    bool SupportsNativeScheduledSend { get; }
    Uri Destination { get; }
    
    Task<bool> PingAsync(); // Enables circuit breaker functionality
    ValueTask SendAsync(Envelope envelope);
}
```

### IReceiver
Interface provided by Wolverine to handle received messages.

```csharp
public interface IReceiver
{
    ValueTask ReceivedAsync(IListener listener, Envelope envelope);
}
```

## Message/Envelope System

### Envelope Class
The core message container in Wolverine.

```csharp
public class Envelope
{
    // Constructor
    public Envelope(object message)
    public Envelope(byte[] data)
    
    // Public settable properties:
    public Guid Id { get; set; }
    public string? CorrelationId { get; set; }
    public string? MessageType { get; set; }
    public Uri? Destination { get; set; }
    public DeliveryMode DeliveryMode { get; set; }
    public string? ContentType { get; set; }
    public byte[]? Data { get; set; }
    public object? Message { get; set; }
    public int Attempts { get; set; }
    public string? TenantId { get; set; }
    public Dictionary<string, string> Headers { get; }
    
    // Properties with internal setters (cannot be set directly):
    public Uri? ReplyUri { get; internal set; }
    public Guid ConversationId { get; internal set; }
    public string? Source { get; internal set; }
    public DateTimeOffset SentAt { get; internal set; }
    public string? ParentId { get; internal set; }
    
    // Factory methods:
    public static Envelope ForPing(Uri destination)
}
```

**Important**: Many Envelope properties have internal setters. You must use reflection or headers to preserve these values when mapping between transport messages and envelopes.

### IEnvelopeMapper
Interface for mapping between transport messages and Wolverine envelopes.

```csharp
public interface IEnvelopeMapper
{
    void MapEnvelopeToOutgoing(Envelope envelope, object outgoing);
    void MapIncomingToEnvelope(Envelope envelope, object incoming);
    IEnumerable<string> AllHeaders();
}
```

### EnvelopeMapper Base Class
Provides common mapping functionality.

```csharp
public abstract class EnvelopeMapper<TIncoming, TOutgoing> : IEnvelopeMapper
{
    // Override these methods:
    protected abstract void writeOutgoingHeader(TOutgoing outgoing, string key, string value);
    protected abstract bool tryReadIncomingHeader(TIncoming incoming, string key, out string? value);
}
```

## Configuration/Runtime Interfaces

### IWolverineRuntime
The main runtime interface provided to transports.

```csharp
public interface IWolverineRuntime
{
    IServiceProvider Services { get; }
    WolverineOptions Options { get; }
    ILoggerFactory LoggerFactory { get; }
    CancellationToken Cancellation { get; }
    IMessageSerializer Serializer { get; }
    IHandlerPipeline Pipeline { get; }
}
```

### Transport Registration Pattern
Extension methods on WolverineOptions:

```csharp
public static WolverineOptions UseNats(this WolverineOptions options, 
    string connectionString = "nats://localhost:4222")
{
    var transport = new NatsTransport();
    options.AddTransport(transport);
    
    return options.ConfigureNats(c => c.ConnectionString = connectionString);
}

public static WolverineOptions ConfigureNats(this WolverineOptions options, 
    Action<NatsTransportConfiguration> configure)
{
    var transport = options.GetOrCreateTransport<NatsTransport>();
    configure(transport.Configuration);
    return options;
}
```

## Advanced Features

### IBrokerEndpoint
For endpoints that need setup/teardown operations.

```csharp
public interface IBrokerEndpoint
{
    ValueTask<bool> CheckAsync();
    ValueTask SetupAsync(ILogger logger);
    ValueTask TeardownAsync(ILogger logger);
    ValueTask<bool> PurgeAsync(ILogger logger);
}
```

### ISenderProtocol
For batch sending support.

```csharp
public interface ISenderProtocol : ISender
{
    ValueTask SendAsync(Envelope[] envelopes);
}
```

### Dead Letter Queue Support
Implement `IDeadLetterQueueEndpoint`:

```csharp
public interface IDeadLetterQueueEndpoint
{
    ValueTask MoveToErrorsAsync(Envelope envelope, Exception exception);
}
```

### Native Scheduled Send
Set `SupportsNativeScheduledSend = true` on ISender and handle `envelope.ScheduledFor`.

## Testing Support

### TransportComplianceFixture
Base class for transport compliance testing:

```csharp
public abstract class TransportComplianceFixture : IAsyncLifetime
{
    protected abstract ValueTask<IWolverineRuntime> buildRuntime();
    
    // Required test scenarios:
    // - Send and receive
    // - Request/Reply
    // - Multiple receivers (queue groups)
    // - Large messages
    // - Headers preservation
    // - Error handling
    // - Native scheduled send (if supported)
}
```

## Implementation Patterns

### Transport Implementation Example
```csharp
public class NatsTransport : TransportBase<NatsEndpoint>
{
    private readonly LightweightCache<string, NatsEndpoint> _endpoints = new();
    
    public NatsTransport() : base("nats", "NATS Transport") { }
    
    protected override IEnumerable<NatsEndpoint> endpoints() => _endpoints;
    
    protected override NatsEndpoint findEndpointByUri(Uri uri)
    {
        var subject = ExtractSubjectFromUri(uri);
        return _endpoints[subject];
    }
    
    public override async ValueTask InitializeAsync(IWolverineRuntime runtime)
    {
        // Initialize connection
        // Set up reply endpoint
        // Compile all endpoints
    }
}
```

### Endpoint Implementation Example
```csharp
public class NatsEndpoint : Endpoint
{
    public NatsEndpoint(string subject, NatsTransport transport, EndpointRole role) 
        : base(new Uri($"nats://{subject}"), role) { }
    
    protected override bool supportsMode(EndpointMode mode)
    {
        return mode == EndpointMode.BufferedInMemory || mode == EndpointMode.Inline;
    }
    
    protected override ISender CreateSender(IWolverineRuntime runtime)
    {
        return new NatsSender(this, transport.Connection, runtime);
    }
    
    public override async ValueTask<IListener> BuildListenerAsync(
        IWolverineRuntime runtime, IReceiver receiver)
    {
        var listener = new NatsListener(this, transport.Connection, receiver, runtime);
        await listener.StartAsync();
        return listener;
    }
}
```

## Common Gotchas

1. **Envelope Properties**: Many properties have internal setters. Use reflection or factory methods.
2. **Disposal**: Implement both IDisposable and IAsyncDisposable properly.
3. **Cancellation**: Always respect the runtime's cancellation token.
4. **Circuit Breaker**: Implement ISender.PingAsync() for automatic circuit breaker support.
5. **Thread Safety**: Transports may be accessed from multiple threads.
6. **Error Handling**: Use the retry mechanisms in Wolverine rather than implementing your own.

## Integration Points

1. **Serialization**: Use `runtime.Serializer` for message serialization.
2. **Logging**: Use `runtime.LoggerFactory` for creating loggers.
3. **DI Container**: Access services via `runtime.Services`.
4. **Options**: Access configuration via `runtime.Options`.
5. **Pipeline**: For advanced scenarios, use `runtime.Pipeline`.

This guide should provide everything needed to implement a fully-featured Wolverine transport that integrates properly with all framework features.