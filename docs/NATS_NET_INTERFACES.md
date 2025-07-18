# NATS.Net Client Library Guide

This document provides a comprehensive guide to the NATS.Net client library architecture and usage patterns.

## Core Connection Interfaces

### Interface Hierarchy
```csharp
public interface INatsClient
{
    // Basic operations
    ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default);
    ValueTask PublishAsync(string subject, NatsMsg<T> msg, CancellationToken cancellationToken = default);
    IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, ...);
    ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(...);
}

public interface INatsConnection : INatsClient, IAsyncDisposable
{
    // Extended operations and events
    NatsOpts Opts { get; }
    NatsConnectionState ConnectionState { get; }
    ValueTask ConnectAsync();
    event AsyncEventHandler<NatsEventArgs>? ConnectionDisconnected;
    event AsyncEventHandler<NatsEventArgs>? ConnectionOpened;
    event AsyncEventHandler<NatsEventArgs>? ReconnectFailed;
}
```

### When to Use Which
- **Use `INatsClient`** for:
  - Dependency injection interfaces
  - Basic pub/sub operations
  - When you don't need connection lifecycle management

- **Use `INatsConnection`** for:
  - Advanced connection management
  - Monitoring connection state
  - Handling connection events

- **Use `NatsConnection`** (concrete) for:
  - Creating new connections
  - When you need access to all features
  - JetStream context creation

### Connection Creation
```csharp
// Basic connection
var connection = new NatsConnection();
await connection.ConnectAsync();

// With options
var opts = NatsOpts.Default with
{
    Url = "nats://localhost:4222",
    Name = "my-app",
    ConnectTimeout = TimeSpan.FromSeconds(10),
    AuthOpts = new NatsAuthOpts { Username = "user", Password = "pass" }
};
var connection = new NatsConnection(opts);
```

## Messaging Patterns

### Core NATS Pub/Sub
```csharp
// Publishing
await connection.PublishAsync("subject", data);
await connection.PublishAsync("subject", data, headers: new NatsHeaders { ["key"] = "value" });

// Subscribing (high-level API)
await foreach (var msg in connection.SubscribeAsync<string>("subject"))
{
    Console.WriteLine($"Received: {msg.Data}");
}

// Subscribing (core API for more control)
var sub = await connection.SubscribeCoreAsync<string>("subject");
await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken))
{
    // Process message
}
```

### Request/Reply Pattern
```csharp
// Single request
var reply = await connection.RequestAsync<Request, Response>("service.method", request);

// Multiple replies
await foreach (var reply in connection.RequestManyAsync<Request, Response>("service.method", request))
{
    // Process each reply
}
```

### Queue Groups
```csharp
// Subscribe with queue group for load balancing
await foreach (var msg in connection.SubscribeAsync<string>("subject", queueGroup: "workers"))
{
    // Only one member of the queue group receives each message
}
```

### Headers Support
```csharp
var headers = new NatsHeaders
{
    ["Content-Type"] = "application/json",
    ["X-Request-ID"] = Guid.NewGuid().ToString()
};

await connection.PublishAsync("subject", data, headers: headers);
```

### Message Structure
```csharp
public readonly record struct NatsMsg<T>
{
    public string Subject { get; }
    public T? Data { get; }
    public NatsHeaders? Headers { get; }
    public string? ReplyTo { get; }
    public NatsConnection Connection { get; }
}
```

## JetStream Interfaces

### Creating JetStream Context
```csharp
// Using extension method (requires: using NATS.Net;)
var js = connection.CreateJetStreamContext();

// With options
var jsOpts = new NatsJSOpts(natsOpts, domain: "my-domain");
var js = new NatsJSContext(connection, jsOpts);
```

### JetStream Context Interface
```csharp
public interface INatsJSContext
{
    // Stream management
    ValueTask<INatsJSStream> CreateStreamAsync(StreamConfig config, CancellationToken cancellationToken = default);
    ValueTask<INatsJSStream> GetStreamAsync(string stream, CancellationToken cancellationToken = default);
    
    // Publishing
    ValueTask<PubAckResponse> PublishAsync<T>(string subject, T data, ...);
    
    // Consumer management
    ValueTask<INatsJSConsumer> CreateOrUpdateConsumerAsync(string stream, ConsumerConfig config, ...);
    ValueTask<INatsJSConsumer> GetConsumerAsync(string stream, string consumer, ...);
}
```

### JetStream Messages
```csharp
public readonly record struct NatsJSMsg<T> : INatsJSMsg<T>
{
    // All properties from NatsMsg<T> plus:
    public NatsJSMsgMetadata? Metadata { get; }
    
    // Acknowledgment methods
    public ValueTask AckAsync(AckOpts? opts = default, CancellationToken cancellationToken = default);
    public ValueTask NakAsync(AckOpts? opts = default, TimeSpan delay = default, CancellationToken cancellationToken = default);
    public ValueTask AckProgressAsync(AckOpts? opts = default, CancellationToken cancellationToken = default);
    public ValueTask AckTerminateAsync(AckOpts? opts = default, CancellationToken cancellationToken = default);
}
```

### Consumer Usage
```csharp
// Create consumer
var consumer = await js.CreateOrUpdateConsumerAsync("STREAM", new ConsumerConfig
{
    DurableName = "my-consumer",
    FilterSubject = "orders.*",
    AckPolicy = ConsumerConfigAckPolicy.Explicit,
    MaxDeliver = 3,
    AckWait = TimeSpan.FromSeconds(30)
});

// Consume messages
await foreach (var msg in consumer.ConsumeAsync<Order>())
{
    try
    {
        // Process message
        await ProcessOrder(msg.Data);
        await msg.AckAsync();
    }
    catch (Exception ex)
    {
        // Negative acknowledgment
        await msg.NakAsync(delay: TimeSpan.FromSeconds(5));
    }
}
```

## Serialization

### Serializer Interface
```csharp
public interface INatsSerialize<T>
{
    void Serialize(IBufferWriter<byte> buffer, T value);
}

public interface INatsDeserialize<T>
{
    T? Deserialize(in ReadOnlySequence<byte> buffer);
}

public interface INatsSerializer<T> : INatsSerialize<T>, INatsDeserialize<T> { }
```

### Built-in Serializers
1. **NatsRawSerializer** - For byte arrays
2. **NatsUtf8PrimitivesSerializer** - For primitives as UTF8
3. **NatsJsonContextSerializer** - For JSON serialization with System.Text.Json

### Custom Serialization
```csharp
public class ProtobufSerializer<T> : INatsSerializer<T>
{
    public void Serialize(IBufferWriter<byte> buffer, T value)
    {
        // Protobuf serialization
    }
    
    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        // Protobuf deserialization
    }
}

// Register custom serializer
var opts = NatsOpts.Default with
{
    SerializerRegistry = new NatsSerializerRegistry()
        .Add(new ProtobufSerializer<MyMessage>())
};
```

### Memory Management
```csharp
// Zero-copy scenarios with NatsMemoryOwner
await foreach (var msg in sub.Msgs.ReadAllAsync())
{
    using (msg.Data) // Dispose returns memory to pool
    {
        // Process data
        var span = msg.Data.Memory.Span;
    }
}
```

## Subscription Management

### High-Level Subscription API
```csharp
// Simple subscription with auto-cleanup
await foreach (var msg in connection.SubscribeAsync<T>("subject", cancellationToken: ct))
{
    // Process messages
    // Subscription is automatically disposed when loop exits
}
```

### Core-Level Subscription API
```csharp
// More control over subscription
var sub = await connection.SubscribeCoreAsync<T>("subject", queueGroup: "workers", opts: new NatsSubOpts
{
    ChannelCapacity = 1000, // Backpressure control
    ThrowIfNoSpace = false  // Drop messages if channel full
});

// Access the channel directly
await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken))
{
    // Process message
}

// Manual unsubscribe
await sub.UnsubscribeAsync();
```

### Subscription Lifecycle
- Subscriptions are automatically cleaned up when disposed
- Use cancellation tokens for graceful shutdown
- Channels handle backpressure automatically
- Messages are delivered in order per subscription

## Advanced Features

### Authentication Options
```csharp
// Username/Password
var opts = NatsOpts.Default with
{
    AuthOpts = new NatsAuthOpts { Username = "user", Password = "pass" }
};

// Token
var opts = NatsOpts.Default with
{
    AuthOpts = new NatsAuthOpts { Token = "my-token" }
};

// NKey
var opts = NatsOpts.Default with
{
    AuthOpts = new NatsAuthOpts { NKey = "path/to/nkey" }
};

// JWT with NKey seed
var opts = NatsOpts.Default with
{
    AuthOpts = new NatsAuthOpts { Jwt = "jwt-token", Seed = "nkey-seed" }
};

// Credentials file (combines JWT and NKey)
var opts = NatsOpts.Default with
{
    AuthOpts = new NatsAuthOpts { CredsFile = "path/to/creds" }
};
```

### TLS Configuration
```csharp
var opts = NatsOpts.Default with
{
    TlsOpts = new NatsTlsOpts
    {
        Mode = TlsMode.Require,
        CertFile = "client-cert.pem",
        KeyFile = "client-key.pem",
        CaFile = "ca.pem",
        InsecureSkipVerify = false
    }
};
```

### Connection Events
```csharp
connection.ConnectionDisconnected += async (sender, args) =>
{
    logger.LogWarning("Disconnected: {Reason}", args.Message);
};

connection.ConnectionOpened += async (sender, args) =>
{
    logger.LogInformation("Connected to {Url}", args.Message);
};

connection.ReconnectFailed += async (sender, args) =>
{
    logger.LogError("Reconnect failed: {Error}", args.Error);
};
```

### Reconnection Strategy
```csharp
var opts = NatsOpts.Default with
{
    MaxReconnectRetry = 10,
    ReconnectWait = TimeSpan.FromSeconds(2),
    ReconnectJitter = TimeSpan.FromMilliseconds(100),
    ConnectTimeout = TimeSpan.FromSeconds(10)
};
```

## Best Practices

### Connection Management
- **Single connection per process** - Connections are thread-safe and should be shared
- **No connection pooling needed** - NATS protocol multiplexes on a single connection
- **Always dispose connections** - Use `await using` or explicit disposal

### Performance Considerations
- **Use value types** - All message types are structs to avoid allocations
- **Memory pooling** - Use `NatsMemoryOwner<T>` for zero-copy scenarios
- **Batch operations** - Publish multiple messages before flushing
- **Channel capacity** - Configure based on expected throughput

### Error Handling
```csharp
// Subscription error handling
await foreach (var msg in connection.SubscribeAsync<T>("subject"))
{
    try
    {
        await ProcessMessage(msg);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing message");
        // Subscription continues
    }
}

// Connection error handling
try
{
    await connection.ConnectAsync();
}
catch (NatsException ex)
{
    logger.LogError(ex, "Failed to connect: {Error}", ex.Message);
}
```

### Thread Safety
- All public methods on `NatsConnection` are thread-safe
- Subscriptions can be created and disposed from any thread
- Message handlers may be called concurrently
- Use appropriate synchronization in your handlers

### Memory Management
```csharp
// Return memory to pool immediately after use
await foreach (var msg in sub.Msgs.ReadAllAsync())
{
    using (msg.Data)
    {
        ProcessData(msg.Data.Memory.Span);
    } // Memory returned to pool here
}

// Or copy data if needed beyond the scope
byte[] copy = msg.Data.Memory.ToArray();
```

## Common Patterns

### Service Pattern
```csharp
public class OrderService
{
    private readonly INatsConnection _connection;
    
    public async Task StartAsync(CancellationToken ct)
    {
        await foreach (var msg in _connection.SubscribeAsync<OrderRequest>(
            "orders.process", queueGroup: "order-processors", cancellationToken: ct))
        {
            try
            {
                var response = await ProcessOrder(msg.Data);
                if (msg.ReplyTo != null)
                {
                    await _connection.PublishAsync(msg.ReplyTo, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process order");
            }
        }
    }
}
```

### Batch Publishing
```csharp
// Publish multiple messages efficiently
for (int i = 0; i < 1000; i++)
{
    await connection.PublishAsync($"sensor.{i}", new SensorData { Value = i });
}
// Connection automatically batches for efficiency
```

### Wildcard Subscriptions
```csharp
// Subscribe to multiple subjects
await foreach (var msg in connection.SubscribeAsync<string>("orders.*"))
{
    // Receives orders.new, orders.update, orders.delete, etc.
}

await foreach (var msg in connection.SubscribeAsync<string>("sensors.>"))
{
    // Receives sensors.temp, sensors.pressure.high, etc.
}
```

## Performance Tips

1. **Reuse connections** - Create once, use everywhere
2. **Configure channel capacity** - Based on expected message rate
3. **Use appropriate serialization** - Raw bytes for performance, JSON for flexibility
4. **Leverage queue groups** - For automatic load balancing
5. **Monitor connection events** - For operational visibility
6. **Set reasonable timeouts** - Avoid hanging operations
7. **Use cancellation tokens** - For graceful shutdown
8. **Dispose memory owners** - Return buffers to pool promptly

This guide provides the foundation for building robust, high-performance messaging systems with NATS.Net.