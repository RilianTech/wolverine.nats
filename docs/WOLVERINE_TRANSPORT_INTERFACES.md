# Wolverine Transport Implementation Guide

This document provides a comprehensive guide to implementing a custom transport for the Wolverine messaging framework. It covers all the key interfaces, abstract classes, and extension points that a transport implementer needs to understand.

## Table of Contents

1. [Core Transport Interfaces](#core-transport-interfaces)
2. [Message/Envelope System](#messageenvelope-system)
3. [Configuration/Runtime Interfaces](#configurationruntime-interfaces)
4. [Advanced Features](#advanced-features)
5. [Testing Support](#testing-support)
6. [Implementation Patterns](#implementation-patterns)

## Core Transport Interfaces

### ITransport

The primary interface that all transports must implement:

```csharp
public interface ITransport
{
    string Protocol { get; }  // e.g., "nats", "rabbitmq", "kafka"
    string Name { get; }      // Diagnostic name for this transport type
    
    Endpoint? ReplyEndpoint();
    Endpoint GetOrCreateEndpoint(Uri uri);
    Endpoint? TryGetEndpoint(Uri uri);
    IEnumerable<Endpoint> Endpoints();
    
    ValueTask InitializeAsync(IWolverineRuntime runtime);
    bool TryBuildStatefulResource(IWolverineRuntime runtime, out IStatefulResource? resource);
}
```

**Key Points:**
- `Protocol` must match the URI scheme you'll use (e.g., `nats://server/subject`)
- `GetOrCreateEndpoint` is called when Wolverine needs to send/receive from a URI
- `InitializeAsync` is called during application startup
- `TryBuildStatefulResource` is for transports that need special resource management

### TransportBase<TEndpoint>

Most transports should inherit from this abstract base class:

```csharp
public abstract class TransportBase<TEndpoint> : ITransport where TEndpoint : Endpoint
{
    protected abstract IEnumerable<TEndpoint> endpoints();
    protected abstract TEndpoint findEndpointByUri(Uri uri);
}
```

### Endpoint

The base class for all endpoints (both sending and listening):

```csharp
public abstract class Endpoint
{
    // Core properties
    public Uri Uri { get; }
    public EndpointMode Mode { get; set; }  // Durable, BufferedInMemory, or Inline
    public bool IsListener { get; set; }
    public string EndpointName { get; set; }
    
    // Serialization
    public IMessageSerializer? DefaultSerializer { get; set; }
    
    // Message routing
    public Type? MessageType { get; set; }  // Optional default message type
    
    // Abstract methods to implement
    public abstract ValueTask<IListener> BuildListenerAsync(IWolverineRuntime runtime, IReceiver receiver);
    protected abstract ISender CreateSender(IWolverineRuntime runtime);
    
    // Optional overrides
    public virtual ValueTask InitializeAsync(ILogger logger) { }
    public virtual bool TryBuildDeadLetterSender(IWolverineRuntime runtime, out ISender? sender) { }
}
```

**EndpointMode Options:**
- `Durable`: Messages are persisted to the message store before processing
- `BufferedInMemory`: Messages are buffered in memory queues
- `Inline`: Messages are processed immediately on the transport thread

### IListener

Interface for receiving messages:

```csharp
public interface IListener : IChannelCallback, IAsyncDisposable
{
    Uri Address { get; }
    ValueTask StopAsync();
}

public interface IChannelCallback
{
    ValueTask CompleteAsync(Envelope envelope);  // Acknowledge successful processing
    ValueTask DeferAsync(Envelope envelope);     // Negative acknowledgment/requeue
}
```

### ISender

Interface for sending messages:

```csharp
public interface ISender
{
    bool SupportsNativeScheduledSend { get; }
    Uri Destination { get; }
    Task<bool> PingAsync();  // Used for circuit breaker functionality
    ValueTask SendAsync(Envelope envelope);
}
```

### ISenderProtocol (Optional)

For transports that support batched sending:

```csharp
public interface ISenderProtocol
{
    Task SendBatchAsync(ISenderCallback callback, OutgoingMessageBatch batch);
}
```

## Message/Envelope System

### Envelope

The `Envelope` class is the core message wrapper in Wolverine. Key properties with their access levels:

```csharp
public class Envelope
{
    // Publicly settable
    public Guid Id { get; set; }
    public string? MessageType { get; set; }
    public string? ContentType { get; set; }
    public byte[]? Data { get; set; }
    public object? Message { get; set; }
    public Uri? Destination { get; set; }
    public Dictionary<string, string?> Headers { get; internal set; }
    public string? TenantId { get; set; }
    public string? GroupId { get; set; }
    public string? PartitionKey { get; set; }
    
    // Internal setters - cannot be set directly!
    public Uri? ReplyUri { get; internal set; }
    public string? Source { get; internal set; }
    public DateTimeOffset SentAt { get; internal set; }
    public Guid ConversationId { get; internal set; }
    public string? ParentId { get; internal set; }
    
    // Scheduling
    public DateTimeOffset? ScheduledTime { get; set; }
    public DateTimeOffset? DeliverBy { get; set; }
}
```

**Important**: Properties with `internal set` cannot be modified directly. You must use headers or other mechanisms to preserve these values.

### IEnvelopeMapper

For mapping between Wolverine envelopes and transport-specific message formats:

```csharp
public interface IEnvelopeMapper<TIncoming, TOutgoing> : IOutgoingMapper<TOutgoing>, IIncomingMapper<TIncoming>
{
}

public interface IOutgoingMapper<TOutgoing>
{
    void MapEnvelopeToOutgoing(Envelope envelope, TOutgoing outgoing);
}

public interface IIncomingMapper<TIncoming>
{
    void MapIncomingToEnvelope(Envelope envelope, TIncoming incoming);
    IEnumerable<string> AllHeaders();
}
```

### EnvelopeMapper Base Class

Wolverine provides a powerful base class for envelope mapping:

```csharp
public abstract class EnvelopeMapper<TIncoming, TOutgoing> : IEnvelopeMapper<TIncoming, TOutgoing>
{
    protected abstract void writeOutgoingHeader(TOutgoing outgoing, string key, string value);
    protected abstract bool tryReadIncomingHeader(TIncoming incoming, string key, out string? value);
}
```

This base class automatically handles standard Wolverine headers and provides helper methods for type conversions.

### IMessageSerializer

Interface for message serialization:

```csharp
public interface IMessageSerializer
{
    string ContentType { get; }
    byte[] Write(Envelope envelope);
    object ReadFromData(Type messageType, Envelope envelope);
    object ReadFromData(byte[] data);
    byte[] WriteMessage(object message);
}
```

## Configuration/Runtime Interfaces

### IWolverineRuntime

The main runtime interface available during transport operations:

```csharp
public interface IWolverineRuntime
{
    WolverineOptions Options { get; }
    IMessageStore Storage { get; }
    IEndpointCollection Endpoints { get; }
    ILoggerFactory LoggerFactory { get; }
    CancellationToken Cancellation { get; }
    DurabilitySettings DurabilitySettings { get; }
    
    // Message routing
    IMessageRouter RoutingFor(Type messageType);
    
    // For scheduled messages
    void ScheduleLocalExecutionInMemory(DateTimeOffset executionTime, Envelope envelope);
}
```

### Transport Registration Pattern

Transports are typically registered using extension methods on `WolverineOptions`:

```csharp
public static class NatsTransportExtensions
{
    public static NatsTransportExpression UseNats(this WolverineOptions options, string connectionString)
    {
        var transport = options.Transports.GetOrCreate<NatsTransport>();
        transport.ConnectionString = connectionString;
        return new NatsTransportExpression(transport, options);
    }
}
```

### Configuration Classes

Create configuration classes for fluent API:

```csharp
public class NatsListenerConfiguration : ListenerConfiguration<NatsListener, NatsEndpoint>
{
    public NatsListenerConfiguration(NatsEndpoint endpoint) : base(endpoint)
    {
    }
    
    // Add NATS-specific configuration methods
    public NatsListenerConfiguration WithQueueGroup(string queueGroup)
    {
        endpoint.QueueGroup = queueGroup;
        return this;
    }
}
```

## Advanced Features

### IBrokerEndpoint

For endpoints that need setup/teardown operations:

```csharp
public interface IBrokerEndpoint
{
    Uri Uri { get; }
    ValueTask<bool> CheckAsync();         // Health check
    ValueTask TeardownAsync(ILogger logger);
    ValueTask SetupAsync(ILogger logger);
}
```

### ITransportConfiguresRuntime

For transports that need to configure the runtime before message storage is set up:

```csharp
public interface ITransportConfiguresRuntime
{
    ValueTask ConfigureAsync(IWolverineRuntime runtime);
}
```

### Dead Letter Queue Support

Implement in your Endpoint:

```csharp
public override bool TryBuildDeadLetterSender(IWolverineRuntime runtime, out ISender? deadLetterSender)
{
    // Create a sender to your dead letter destination
    deadLetterSender = new NatsSender(/* dead letter subject */);
    return true;
}
```

### Native Scheduled Send

If your transport supports delayed delivery:

```csharp
public class NatsSender : ISender
{
    public bool SupportsNativeScheduledSend => true;  // Set to true
    
    public async ValueTask SendAsync(Envelope envelope)
    {
        if (envelope.ScheduledTime.HasValue)
        {
            // Use transport's native scheduling
        }
        else
        {
            // Send immediately
        }
    }
}
```

### Multi-Tenancy Support

Use the `TenantId` property on Envelope and endpoint configuration:

```csharp
public class NatsEndpoint : Endpoint
{
    public override string? TenantId { get; set; }  // Tag all messages from this endpoint
}
```

## Testing Support

### TransportCompliance Base

Wolverine provides a base class for transport compliance testing:

```csharp
public abstract class TransportComplianceFixture : IDisposable, IAsyncDisposable
{
    public IHost Sender { get; private set; }
    public IHost Receiver { get; private set; }
    public Uri OutboundAddress { get; protected set; }
    
    // Override to configure your transport
    protected abstract Task ConfigureTransportAsync(WolverineOptions options);
}
```

### Required Compliance Tests

Your transport should pass these test scenarios:
1. Basic send/receive
2. Request/reply patterns
3. Scheduled messages (if supported)
4. Error handling and dead letter queues
5. Serialization with different content types
6. Connection failures and circuit breakers

## Implementation Patterns

### 1. Basic Transport Structure

```csharp
public class NatsTransport : TransportBase<NatsEndpoint>
{
    private readonly List<NatsEndpoint> _endpoints = new();
    
    public NatsTransport() : base("nats", "NATS") { }
    
    protected override IEnumerable<NatsEndpoint> endpoints() => _endpoints;
    
    protected override NatsEndpoint findEndpointByUri(Uri uri)
    {
        var endpoint = _endpoints.FirstOrDefault(x => x.Uri == uri);
        if (endpoint == null)
        {
            endpoint = new NatsEndpoint(uri);
            _endpoints.Add(endpoint);
        }
        return endpoint;
    }
}
```

### 2. Endpoint Implementation

```csharp
public class NatsEndpoint : Endpoint, IBrokerEndpoint
{
    public override async ValueTask<IListener> BuildListenerAsync(IWolverineRuntime runtime, IReceiver receiver)
    {
        var listener = new NatsListener(this, runtime, receiver);
        await listener.StartAsync();
        return listener;
    }
    
    protected override ISender CreateSender(IWolverineRuntime runtime)
    {
        return new NatsSender(this, runtime);
    }
}
```

### 3. Listener Pattern

```csharp
public class NatsListener : IListener
{
    private readonly IReceiver _receiver;
    
    public async ValueTask CompleteAsync(Envelope envelope)
    {
        // Acknowledge the message
    }
    
    public async ValueTask DeferAsync(Envelope envelope)
    {
        // Negative acknowledge/requeue
    }
    
    private async Task ProcessMessageAsync(NatsMessage message)
    {
        var envelope = new Envelope();
        _envelopeMapper.MapIncomingToEnvelope(envelope, message);
        
        try
        {
            await _receiver.ReceivedAsync(this, envelope);
        }
        catch (Exception ex)
        {
            // Handle exceptions appropriately
        }
    }
}
```

### 4. Sender Pattern

```csharp
public class NatsSender : ISender
{
    public async ValueTask SendAsync(Envelope envelope)
    {
        var natsMessage = new NatsMessage();
        _envelopeMapper.MapEnvelopeToOutgoing(envelope, natsMessage);
        
        await _natsConnection.PublishAsync(envelope.Destination.ToString(), natsMessage);
    }
    
    public async Task<bool> PingAsync()
    {
        // Implement health check
        return await _natsConnection.PingAsync();
    }
}
```

## Common Gotchas

1. **Envelope Property Access**: Many Envelope properties have internal setters. Use headers or the envelope mapper to preserve values like ConversationId, ReplyUri, etc.

2. **Thread Safety**: Ensure your listener and sender implementations are thread-safe, especially when using BufferedInMemory or Durable modes.

3. **Serialization**: Always respect the endpoint's DefaultSerializer and ContentType settings.

4. **Circuit Breakers**: Implement PingAsync() properly for automatic circuit breaker support.

5. **Cancellation**: Always respect the CancellationToken from IWolverineRuntime.Cancellation.

6. **Resource Cleanup**: Implement IAsyncDisposable properly in listeners and clean up connections.

7. **Error Handling**: Let exceptions bubble up to Wolverine's error handling unless you're implementing specific transport-level retry logic.

## Example: Minimal NATS Transport

See the companion implementation guide for a complete example of implementing a NATS transport with both Core NATS and JetStream support.