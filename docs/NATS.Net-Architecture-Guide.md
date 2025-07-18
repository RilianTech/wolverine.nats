# NATS.Net Client Library Architecture Guide

## Table of Contents
1. [Design Philosophy](#design-philosophy)
2. [Core Connection Interfaces](#core-connection-interfaces)
3. [Messaging Patterns](#messaging-patterns)
4. [JetStream Interfaces](#jetstream-interfaces)
5. [Serialization](#serialization)
6. [Subscription Management](#subscription-management)
7. [Advanced Features](#advanced-features)
8. [Best Practices](#best-practices)
9. [Performance Considerations](#performance-considerations)
10. [Common Patterns and Idioms](#common-patterns-and-idioms)

## Design Philosophy

NATS.Net is a modern, high-performance .NET client for NATS designed with the following principles:

- **Performance First**: Heavy use of value types (structs), memory pooling, and zero-allocation patterns
- **Async/Await Native**: Built from the ground up for modern async C# patterns
- **Type Safety**: Strong typing with generics for message payloads
- **Minimal Allocations**: Uses `ReadOnlySequence<byte>`, `IBufferWriter<byte>`, and memory pooling
- **Modular Design**: Core NATS and JetStream are separate packages

## Core Connection Interfaces

### Interface Hierarchy

```
INatsClient (base interface)
    └── INatsConnection (extends INatsClient with events and advanced features)
            └── NatsConnection (concrete implementation)
```

### INatsClient vs INatsConnection

**INatsClient** - The minimal interface for basic NATS operations:
- Publishing messages
- Subscribing to subjects
- Request/Reply pattern
- Connection management

**INatsConnection** - Extended interface with:
- Event handlers (disconnected, reconnected, etc.)
- Server info access
- Subscription manager access
- Lower-level control (`SubscribeCoreAsync`, `PublishAsync` with `NatsMsg<T>`)

### When to Use Which

- Use `INatsClient` for dependency injection and basic messaging needs
- Use `INatsConnection` when you need:
  - Event handling
  - Advanced subscription management
  - Custom message handling patterns
  - Access to server information

### Connection Lifecycle

```csharp
// Basic connection
await using var nats = new NatsConnection();

// With options
var opts = NatsOpts.Default with 
{
    Url = "nats://localhost:4222",
    Name = "MyService",
    AuthOpts = NatsAuthOpts.Default with { Token = "secret" }
};
await using var nats = new NatsConnection(opts);

// Explicit connection
await nats.ConnectAsync();
```

## Messaging Patterns

### Core NATS Pub/Sub

**Publishing**:
```csharp
// Simple publish
await nats.PublishAsync("subject", data);

// With headers
var headers = new NatsHeaders { ["X-Trace-Id"] = "123" };
await nats.PublishAsync("subject", data, headers);

// Empty message (signal)
await nats.PublishAsync("subject");

// Using NatsMsg<T> for full control
var msg = new NatsMsg<T>
{
    Subject = "subject",
    Data = data,
    Headers = headers,
    ReplyTo = replySubject
};
await nats.PublishAsync(msg);
```

**Subscribing**:
```csharp
// Simple subscription (async enumerable)
await foreach (var msg in nats.SubscribeAsync<string>("subject"))
{
    Console.WriteLine($"Received: {msg.Data}");
}

// With queue group (load balancing)
await foreach (var msg in nats.SubscribeAsync<Order>("orders.*", queueGroup: "workers"))
{
    await ProcessOrder(msg.Data);
}

// Lower-level control with INatsSub<T>
var sub = await nats.SubscribeCoreAsync<Data>("subject");
await foreach (var msg in sub.Msgs.ReadAllAsync())
{
    // Process message
}
await sub.UnsubscribeAsync();
```

### Request/Reply Pattern

```csharp
// Simple request/reply
var reply = await nats.RequestAsync<Request, Response>("service.action", request);

// Multiple replies
await foreach (var reply in nats.RequestManyAsync<Request, Response>("service.query", request))
{
    // Process each reply
}
```

### Headers Support

NATS.Net provides a full-featured headers implementation:

```csharp
var headers = new NatsHeaders
{
    ["X-Request-ID"] = "123",
    ["X-Trace-ID"] = "abc",
    ["Content-Type"] = "application/json"
};

// Headers are preserved through request/reply
await nats.PublishAsync("subject", data, headers);
```

### Message Structure

The `NatsMsg<T>` struct is the core message type:

```csharp
public readonly record struct NatsMsg<T>
{
    public string Subject { get; init; }
    public string? ReplyTo { get; init; }
    public int Size { get; init; }
    public NatsHeaders? Headers { get; init; }
    public T? Data { get; init; }
    public INatsConnection? Connection { get; init; }
    public NatsMsgFlags Flags { get; init; }
}
```

Key features:
- Value type (struct) for performance
- Immutable with init-only properties
- Flags for empty messages and no-responders
- Connection reference for reply functionality

## JetStream Interfaces

### Creating JetStream Context

```csharp
// From INatsClient/INatsConnection
var js = nats.CreateJetStreamContext();

// With options
var jsOpts = new NatsJSOpts { Domain = "production" };
var js = nats.CreateJetStreamContext(jsOpts);
```

### Stream Management

```csharp
// Create or update stream
var stream = await js.CreateOrUpdateStreamAsync(new StreamConfig
{
    Name = "ORDERS",
    Subjects = new[] { "orders.*" },
    Retention = StreamConfigRetention.Limits,
    MaxAge = TimeSpan.FromDays(7)
});

// Get existing stream
var stream = await js.GetStreamAsync("ORDERS");

// List streams
await foreach (var stream in js.ListStreamsAsync())
{
    Console.WriteLine(stream.Info.Config.Name);
}
```

### Consumer Patterns

**Push vs Pull Consumers**: NATS.Net primarily uses pull consumers for better control:

```csharp
// Create consumer
var consumer = await js.CreateOrUpdateConsumerAsync("ORDERS", new ConsumerConfig
{
    Name = "processor",
    DurableName = "processor",
    AckPolicy = ConsumerConfigAckPolicy.Explicit
});

// Consume messages (continuous)
await foreach (var msg in consumer.ConsumeAsync<Order>())
{
    await ProcessOrder(msg.Data);
    await msg.AckAsync();
}

// Fetch batch
await foreach (var msg in consumer.FetchAsync<Order>(opts: new NatsJSFetchOpts { MaxMsgs = 100 }))
{
    await ProcessOrder(msg.Data);
    await msg.AckAsync();
}

// Next message
var msg = await consumer.NextAsync<Order>();
if (msg != null)
{
    await ProcessOrder(msg.Data);
    await msg.AckAsync();
}
```

### JetStream Message (NatsJSMsg<T>)

JetStream messages extend core messages with metadata and acknowledgment:

```csharp
public readonly struct NatsJSMsg<T>
{
    // All NatsMsg<T> properties plus:
    public NatsJSMsgMetadata? Metadata { get; }
    
    // Acknowledgment methods
    public ValueTask AckAsync();
    public ValueTask NakAsync(TimeSpan delay = default);
    public ValueTask AckProgressAsync();
    public ValueTask AckTerminateAsync();
}
```

### Publishing to JetStream

```csharp
// Publish with acknowledgment
var ack = await js.PublishAsync("orders.new", order);
Console.WriteLine($"Stored at sequence: {ack.Sequence}");

// Publish with deduplication
var headers = new NatsHeaders { ["Nats-Msg-Id"] = orderId };
var ack = await js.PublishAsync("orders.new", order, headers: headers);
```

## Serialization

### Serializer Hierarchy

NATS.Net uses a chain-of-responsibility pattern for serialization:

```csharp
INatsSerializer<T> : INatsSerialize<T>, INatsDeserialize<T>
```

### Built-in Serializers

1. **NatsRawSerializer<T>** - For binary data:
   - `byte[]`
   - `Memory<byte>`
   - `ReadOnlyMemory<byte>`
   - `ReadOnlySequence<byte>`
   - `IMemoryOwner<byte>`

2. **NatsUtf8PrimitivesSerializer<T>** - For primitives as UTF-8:
   - `string`
   - All numeric types
   - `DateTime`, `DateTimeOffset`
   - `Guid`, `TimeSpan`
   - `bool`

3. **NatsJsonContextSerializer<T>** - For JSON with source generators:
   ```csharp
   [JsonSerializable(typeof(Order))]
   partial class AppJsonContext : JsonSerializerContext { }
   
   var opts = NatsOpts.Default with
   {
       SerializerRegistry = new NatsJsonContextSerializerRegistry(AppJsonContext.Default)
   };
   ```

### Custom Serialization

```csharp
public class ProtobufSerializer<T> : INatsSerializer<T>
{
    public void Serialize(IBufferWriter<byte> writer, T value)
    {
        // Serialize to writer
    }
    
    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        // Deserialize from buffer
    }
    
    public INatsSerializer<T> CombineWith(INatsSerializer<T> next) => 
        new ProtobufSerializer<T> { _next = next };
}
```

### Memory Efficiency

NATS.Net provides `NatsMemoryOwner<T>` for zero-copy scenarios:

```csharp
await foreach (var msg in sub.SubscribeAsync<NatsMemoryOwner<byte>>())
{
    using (msg.Data) // Important: dispose to return to pool
    {
        ProcessBytes(msg.Data.Memory);
    }
}
```

## Subscription Management

### Subscription Types

1. **High-level** (`SubscribeAsync`): Returns `IAsyncEnumerable<NatsMsg<T>>`
2. **Core-level** (`SubscribeCoreAsync`): Returns `INatsSub<T>` for more control

### Channel Options

Control subscription buffering and backpressure:

```csharp
var opts = new NatsSubOpts
{
    ChannelOpts = new NatsSubChannelOpts
    {
        Capacity = 1000,
        FullMode = BoundedChannelFullMode.Wait // or DropNewest, DropOldest
    }
};
```

### Lifecycle Management

```csharp
// Auto-dispose with async enumerable
await foreach (var msg in nats.SubscribeAsync<T>("subject").WithCancellation(cts.Token))
{
    // Subscription auto-unsubscribes when loop exits
}

// Manual control
var sub = await nats.SubscribeCoreAsync<T>("subject");
try
{
    await foreach (var msg in sub.Msgs.ReadAllAsync(cts.Token))
    {
        // Process
    }
}
finally
{
    await sub.UnsubscribeAsync();
}
```

## Advanced Features

### Authentication Options

```csharp
// Username/Password
var opts = NatsOpts.Default with
{
    AuthOpts = NatsAuthOpts.Default with 
    { 
        Username = "user", 
        Password = "pass" 
    }
};

// Token
var opts = NatsOpts.Default with
{
    AuthOpts = NatsAuthOpts.Default with { Token = "secret" }
};

// NKey
var opts = NatsOpts.Default with
{
    AuthOpts = NatsAuthOpts.Default with { NKey = "seed" }
};

// JWT with NKey
var opts = NatsOpts.Default with
{
    AuthOpts = NatsAuthOpts.Default with 
    { 
        Jwt = "jwt-token",
        Seed = "nkey-seed" 
    }
};

// Credentials file
var opts = NatsOpts.Default with
{
    AuthOpts = NatsAuthOpts.Default with { CredsFile = "path/to/creds" }
};
```

### TLS Configuration

```csharp
var opts = NatsOpts.Default with
{
    TlsOpts = NatsTlsOpts.Default with
    {
        Mode = TlsMode.Require,
        CaFile = "/path/to/ca.crt",
        CertFile = "/path/to/client.crt",
        KeyFile = "/path/to/client.key"
    }
};
```

### Connection Events

```csharp
nats.ConnectionDisconnected += async (sender, args) =>
{
    Log.Warning("Disconnected: {Reason}", args.Message);
};

nats.ConnectionOpened += async (sender, args) =>
{
    Log.Information("Connected to {Url}", args.Url);
};

nats.ReconnectFailed += async (sender, args) =>
{
    Log.Error("Reconnect failed: {Error}", args.Message);
};
```

### Reconnection Strategy

```csharp
var opts = NatsOpts.Default with
{
    MaxReconnectRetry = 10,
    ReconnectWaitMin = TimeSpan.FromSeconds(2),
    ReconnectWaitMax = TimeSpan.FromSeconds(10),
    ReconnectJitter = TimeSpan.FromMilliseconds(100)
};
```

## Best Practices

### 1. Connection Management
- Use a single connection per process when possible
- Connections are thread-safe and can be shared
- Use connection pooling for special scenarios only

### 2. Error Handling
```csharp
try
{
    await nats.PublishAsync("subject", data);
}
catch (NatsException ex) when (ex.IsNoResponders())
{
    // Handle no responders
}
catch (NatsException ex)
{
    // Handle other NATS errors
}
```

### 3. Cancellation Tokens
Always propagate cancellation tokens:
```csharp
await nats.PublishAsync("subject", data, cancellationToken: ct);
await foreach (var msg in nats.SubscribeAsync<T>("subject", cancellationToken: ct))
{
    // Process
}
```

### 4. Message Validation
```csharp
await foreach (var msg in subscription)
{
    msg.EnsureSuccess(); // Throws if deserialization failed
    if (msg.Headers?.Code == 503)
    {
        // Handle no responders
        continue;
    }
    // Process message
}
```

### 5. Resource Disposal
```csharp
// Connections
await using var nats = new NatsConnection();

// Memory owners
await foreach (var msg in sub.SubscribeAsync<NatsMemoryOwner<byte>>())
{
    using (msg.Data) // Always dispose
    {
        // Use msg.Data.Memory
    }
}
```

## Performance Considerations

### 1. Use Value Types
- `NatsMsg<T>` is a struct - avoid boxing
- Use `INatsMsg<T>` interface only when necessary (creates boxing)

### 2. Serialization Choice
- Use `NatsMemoryOwner<byte>` for zero-copy scenarios
- Prefer `JsonSerializerContext` for JSON (source-generated)
- Avoid reflection-based serializers in hot paths

### 3. Channel Capacity
```csharp
// High-throughput scenarios
var opts = new NatsSubOpts
{
    ChannelOpts = new NatsSubChannelOpts
    {
        Capacity = 10000,
        FullMode = BoundedChannelFullMode.DropOldest
    }
};
```

### 4. Batching
```csharp
// Fetch in batches for JetStream
await foreach (var msg in consumer.FetchAsync<T>(opts: new() { MaxMsgs = 1000 }))
{
    // Process batch
}
```

### 5. Memory Pooling
The library uses memory pooling internally. For custom scenarios:
```csharp
var bufferWriter = new NatsPooledBufferWriter<byte>();
try
{
    // Use buffer
}
finally
{
    bufferWriter.Dispose(); // Return to pool
}
```

## Common Patterns and Idioms

### 1. Service Pattern
```csharp
public class OrderService
{
    private readonly INatsConnection _nats;
    
    public async Task StartAsync(CancellationToken ct)
    {
        await foreach (var msg in _nats.SubscribeAsync<OrderRequest>(
            "orders.>", queueGroup: "order-service", cancellationToken: ct))
        {
            try
            {
                var response = await ProcessOrder(msg.Data);
                await msg.ReplyAsync(response);
            }
            catch (Exception ex)
            {
                await msg.ReplyAsync(new ErrorResponse { Error = ex.Message });
            }
        }
    }
}
```

### 2. Event Sourcing with JetStream
```csharp
public class EventStore
{
    private readonly INatsJSContext _js;
    
    public async Task<ulong> AppendAsync(string stream, Event evt)
    {
        var ack = await _js.PublishAsync($"{stream}.{evt.Type}", evt);
        return ack.Sequence;
    }
    
    public async IAsyncEnumerable<Event> ReadAsync(string stream, ulong fromSeq = 0)
    {
        var consumer = await _js.GetConsumerAsync(stream, "reader");
        await foreach (var msg in consumer.FetchAsync<Event>(
            opts: new() { StartSequence = fromSeq }))
        {
            yield return msg.Data;
            await msg.AckAsync();
        }
    }
}
```

### 3. Scatter-Gather Pattern
```csharp
public async Task<List<Quote>> GetQuotesAsync(QuoteRequest request, TimeSpan timeout)
{
    var quotes = new List<Quote>();
    using var cts = new CancellationTokenSource(timeout);
    
    await foreach (var msg in _nats.RequestManyAsync<QuoteRequest, Quote>(
        "quotes.request", request, replyOpts: new() { }, cancellationToken: cts.Token))
    {
        quotes.Add(msg.Data);
    }
    
    return quotes;
}
```

### 4. Work Queue with JetStream
```csharp
public class WorkQueue
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        var consumer = await _js.CreateOrUpdateConsumerAsync("WORK", new ConsumerConfig
        {
            DurableName = "workers",
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            MaxDeliver = 3,
            AckWait = TimeSpan.FromMinutes(5)
        });
        
        await foreach (var msg in consumer.ConsumeAsync<WorkItem>(cancellationToken: ct))
        {
            try
            {
                await ProcessWorkItem(msg.Data);
                await msg.AckAsync();
            }
            catch (Exception ex)
            {
                // NAK with delay for retry
                await msg.NakAsync(delay: TimeSpan.FromSeconds(30));
            }
        }
    }
}
```

## Key Differences from Other NATS Clients

1. **Async-First**: No synchronous APIs
2. **Value Types**: Heavy use of structs for performance
3. **Memory Efficient**: Built-in memory pooling and zero-copy support
4. **Type Safe**: Generic message types with pluggable serialization
5. **Modern C#**: Uses latest language features (async enumerables, records, etc.)

## Gotchas and Non-Obvious Behaviors

1. **Message Buffering**: Subscriptions buffer messages in channels. Set appropriate capacity.
2. **Disposal**: Always dispose `NatsMemoryOwner<T>` messages
3. **Boxing**: Using `INatsMsg<T>` interface causes boxing of the struct
4. **JetStream Timeouts**: JetStream operations have built-in timeouts
5. **Headers**: Some headers have special meaning (e.g., `Nats-Msg-Id` for deduplication)

## Summary

NATS.Net is designed for high-performance, modern .NET applications. Key takeaways:

- Choose the right interface level (INatsClient vs INatsConnection)
- Understand the difference between Core NATS (at-most-once) and JetStream (at-least-once)
- Use appropriate serialization for your use case
- Leverage async enumerables for elegant subscription handling
- Always consider memory efficiency and use pooling where appropriate
- Follow established patterns for common scenarios

The library's design philosophy emphasizes performance, type safety, and modern C# idioms while providing a clean API that scales from simple pub/sub to complex streaming scenarios.