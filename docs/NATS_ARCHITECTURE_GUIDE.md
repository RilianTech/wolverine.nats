# NATS Architecture Guide for Wolverine Transport

This document summarizes key NATS architectural concepts and how they apply to building a Wolverine transport, based on analysis of the official NATS architecture and design documentation.

## Table of Contents
1. [Core NATS Concepts](#core-nats-concepts)
2. [JetStream Architecture](#jetstream-architecture)
3. [Security and Multi-Tenancy](#security-and-multi-tenancy)
4. [Implementation Recommendations](#implementation-recommendations)

## Core NATS Concepts

### Subject-Based Messaging
NATS uses a subject-based addressing model that maps well to Wolverine's endpoint concept:

- **Hierarchical Naming**: Use dots to create hierarchies (e.g., `orders.created.us-east`)
- **Wildcards**: 
  - `*` matches a single token
  - `>` matches one or more tokens
- **Case Sensitive**: Subjects are case-sensitive
- **Character Set**: ASCII alphanumeric plus `.`, `*`, and `>`

**Wolverine Mapping**:
```csharp
// Wolverine endpoint: /orders/created
// NATS subject: orders.created
public static string ToNatsSubject(string wolverineEndpoint)
{
    return wolverineEndpoint.Trim('/').Replace('/', '.');
}
```

### Queue Groups
Queue groups provide automatic load balancing across multiple subscribers:

- Only one member receives each message
- Automatic failover if a member disconnects
- No configuration needed on the server
- Essential for competing consumer pattern

**Wolverine Integration**:
```csharp
// For handlers that allow parallel processing
endpoint.QueueGroup = $"{endpoint.Subject}.workers";
```

### Request/Reply Pattern
NATS provides built-in request/reply support:

- Client creates unique inbox subject
- Includes inbox as reply-to in request
- Server publishes response to inbox
- Built-in timeout handling

**Benefits for Wolverine**:
- Native support for `InvokeAsync<T>` pattern
- No need for correlation ID management
- Automatic cleanup of reply subscriptions

### Connection Management
NATS connections are resilient by design:

- Automatic reconnection with exponential backoff
- Server discovery in clusters
- Subscription state maintained across reconnects
- Zero message loss during brief disconnections

## JetStream Architecture

### Streams
Streams provide persistent storage for messages:

```yaml
Stream Configuration:
  - Name: "WOLVERINE_EVENTS"
  - Subjects: ["wolverine.>"]
  - Retention: "WorkQueue"  # Delete after ack
  - Replicas: 3             # For HA
  - MaxAge: 7 days          # Message TTL
  - Discard: "Old"          # When full
```

### Consumer Types

#### Pull Consumers (Recommended)
Best for Wolverine's controlled message processing:

```csharp
var consumer = await js.CreateOrUpdateConsumerAsync(stream, new ConsumerConfig
{
    Durable = "wolverine-consumer",
    AckPolicy = ConsumerConfigAckPolicy.Explicit,
    AckWait = TimeSpan.FromSeconds(30),
    MaxDeliver = 5,  // Retry limit
    MaxAckPending = 100,  // Concurrent messages
    FilterSubject = "orders.>",
    IdleHeartbeat = TimeSpan.FromSeconds(5)
});
```

#### Ordered Consumers
For guaranteed order processing:
- Ephemeral (recreated on failure)
- Single client only
- Memory storage
- Auto-recovery on gaps

### Message Acknowledgment

JetStream provides sophisticated ack mechanisms:

```csharp
// Success
await msg.AckAsync();

// Retry immediately
await msg.NakAsync();

// Retry with delay
await msg.NakAsync(delay: TimeSpan.FromSeconds(30));

// Extend processing time
await msg.AckProgressAsync();

// Give up (dead letter)
await msg.AckTerminateAsync();
```

### Deduplication
Built-in message deduplication:

```csharp
headers["Nats-Msg-Id"] = envelope.Id.ToString();
// Duplicate publishes within window return success
// Server tracks IDs for configurable window (default 2 min)
```

## Security and Multi-Tenancy

### Account System
NATS accounts provide complete isolation:

```
Operator (Root)
├── Account A (Tenant 1)
│   ├── User 1
│   └── User 2
└── Account B (Tenant 2)
    ├── User 3
    └── User 4
```

- Each account has isolated subject namespace
- No cross-account communication by default
- Can selectively export/import subjects

### JWT Authentication
Hierarchical JWT model:

1. **Operator JWT**: Root of trust
2. **Account JWT**: Defines account limits and permissions
3. **User JWT**: Individual connection credentials

```csharp
// Per-tenant connection
var opts = new NatsOpts
{
    AuthOpts = new NatsAuthOpts
    {
        JWT = tenantUserJwt,
        Seed = tenantUserSeed
    }
};
```

### Subject Permissions
Fine-grained publish/subscribe control:

```json
{
  "pub": {
    "allow": ["orders.>", "inventory.>"],
    "deny": ["orders.sensitive.>"]
  },
  "sub": {
    "allow": ["orders.>", "notifications.>"]
  }
}
```

### Multi-Tenancy Patterns

#### Pattern 1: Account per Tenant
```csharp
public class TenantNatsTransport
{
    private readonly Dictionary<string, NatsConnection> _connections;
    
    public NatsConnection GetConnection(string tenantId)
    {
        return _connections.GetOrAdd(tenantId, CreateTenantConnection);
    }
}
```

#### Pattern 2: Subject-Based Isolation
```csharp
// Single connection, tenant in subject
var subject = $"tenant.{tenantId}.{endpoint}";
```

## Implementation Recommendations

### 1. Dual Mode Support
Implement both Core NATS and JetStream:

```csharp
public class NatsEndpoint : Endpoint
{
    public bool UseJetStream { get; set; }
    
    protected override bool supportsMode(EndpointMode mode)
    {
        return mode switch
        {
            EndpointMode.Inline => true,  // Core NATS
            EndpointMode.BufferedInMemory => true,  // Core NATS
            EndpointMode.Durable => UseJetStream,  // JetStream only
            _ => false
        };
    }
}
```

### 2. Stream Design
One stream per message type or domain:

```yaml
Streams:
  - WOLVERINE_COMMANDS: ["commands.>"]
  - WOLVERINE_EVENTS: ["events.>"]
  - WOLVERINE_DLQ: ["dlq.>"]
```

### 3. Consumer Strategy
- Use pull consumers for better flow control
- One consumer per endpoint
- Durable for production, ephemeral for development
- Configure based on handler requirements

### 4. Error Handling
Leverage JetStream's retry mechanisms:

```csharp
try
{
    await ProcessMessage(msg);
    await msg.AckAsync();
}
catch (TransientException)
{
    // Retry with backoff
    await msg.NakAsync(delay: TimeSpan.FromSeconds(Math.Pow(2, msg.Redelivered)));
}
catch (PermanentException)
{
    // Send to dead letter
    await msg.AckTerminateAsync();
    await PublishToDeadLetter(msg);
}
```

### 5. Performance Optimization
- Use pull consumers with batching
- Enable message deduplication
- Configure appropriate stream limits
- Use memory storage for transient data
- Leverage zero-copy message handling

### 6. Monitoring
Subscribe to JetStream advisories:

```csharp
// Consumer events
await connection.SubscribeAsync("$JS.EVENT.ADVISORY.CONSUMER.>", 
    msg => LogConsumerEvent(msg));

// Stream events  
await connection.SubscribeAsync("$JS.EVENT.ADVISORY.STREAM.>",
    msg => LogStreamEvent(msg));
```

## Best Practices

1. **Subject Design**: Use hierarchical subjects that map to your domain
2. **Stream Retention**: Use WorkQueue for inbox patterns, Limits for event sourcing
3. **Consumer Names**: Use descriptive, stable names for monitoring
4. **Error Recovery**: Implement exponential backoff with jitter
5. **Connection Pooling**: One connection per process, not per tenant
6. **Monitoring**: Track consumer lag and redelivery rates
7. **Testing**: Use memory storage for unit tests

This architecture guide provides the foundation for building a robust, scalable, and multi-tenant capable Wolverine transport on NATS.