# NATS + Wolverine Architecture Guide

## Core Architecture Principles

### 1. Message Flow Architecture

```
Wolverine Handler → Envelope → NatsEnvelopeMapper → NATS Message → Network
                                                                       ↓
Wolverine Handler ← Envelope ← NatsEnvelopeMapper ← NATS Message ← Network
```

The integration preserves Wolverine's high-level abstractions while leveraging NATS's efficient wire protocol.

### 2. Transport Integration Model

```csharp
// Wolverine's transport abstraction
public class NatsTransport : BrokerTransport<NatsEndpoint>
{
    // NATS connection lifecycle
    private NatsConnection _connection;
    private INatsJSContext _jetStreamContext;
}

// Endpoint represents a NATS subject
public class NatsEndpoint : Endpoint, IBrokerEndpoint
{
    public string Subject { get; set; }        // NATS subject
    public string? QueueGroup { get; set; }    // Load balancing group
    public string? StreamName { get; set; }    // JetStream stream
}
```

## Subject Routing Architecture

### Subject Hierarchy
NATS subjects use dot-notation to create hierarchical namespaces:

```
orders.us-west.created      # Specific event
orders.us-west.*           # All US West order events  
orders.*.created           # Created events all regions
orders.>                   # All order events
```

### Wolverine Endpoint Mapping
```csharp
// Publish to specific subject
opts.PublishMessage<OrderCreated>()
    .ToNatsSubject("orders.{Region}.created");

// Listen with wildcards
opts.ListenToNatsSubject("orders.*.created")
    .UseQueueGroup("order-processors");
```

### Subject Normalization
Wolverine normalizes subjects for consistency:
- `/` → `.` (slash to dot conversion)
- Automatic trimming
- Case preservation

## Consumer Patterns

### 1. Core NATS (At-Most-Once)
Best for ephemeral data where loss is acceptable:

```csharp
opts.ListenToNatsSubject("telemetry.>")
    .ProcessInline()         // Fast, no persistence
    .CircuitBreaker();       // Protect downstream
```

**Characteristics:**
- No acknowledgment required
- Memory-only delivery
- Microsecond latency
- Fire-and-forget semantics

### 2. JetStream (At-Least-Once)
For business-critical data requiring guarantees:

```csharp
opts.ListenToNatsSubject("orders.>")
    .UseJetStream("ORDERS")  // Durable stream
    .MaximumParallelMessages(10)
    .ConfigureDeadLetterQueue(3, "orders.dlq");
```

**Characteristics:**
- Explicit acknowledgment
- Message replay capability
- Ordered delivery available
- Configurable retention

### 3. Queue Groups (Competing Consumers)
Automatic load balancing across instances:

```csharp
// All instances with same queue group share messages
opts.ListenToNatsSubject("work.items")
    .UseQueueGroup("workers")
    .BufferedInMemory();
```

**Characteristics:**
- Automatic failover
- Fair distribution
- No coordinator needed
- Scales horizontally

## JetStream Architecture

### Stream Organization
Streams store messages by subject pattern:

```yaml
Stream: ORDERS
Subjects: orders.>
Retention: Limits (keep last 1M messages)
Storage: File
Replicas: 3
```

### Consumer Types

**Durable Consumers** - Survive restarts
```csharp
opts.ListenToNatsSubject("orders.>")
    .UseJetStream("ORDERS", "order-processor")  // Named consumer
    .Durable();
```

**Ephemeral Consumers** - Cleaned up on disconnect
```csharp
opts.ListenToNatsSubject("orders.>")
    .UseJetStream("ORDERS")  // Anonymous consumer
    .ProcessInline();
```

### Acknowledgment Patterns
```csharp
// In NatsListener
await msg.AckAsync();           // Success - remove from stream
await msg.NakAsync(delay);      // Retry with backoff
await msg.AckTerminateAsync();  // Give up - move to DLQ
await msg.AckProgressAsync();   // Still working - extend timeout
```

## Request/Reply Architecture

### Native Pattern
NATS provides built-in request/reply without additional infrastructure:

```
1. Client sends to: orders.status.123
2. Client creates inbox: _INBOX.AbC123
3. Server receives with reply-to: _INBOX.AbC123  
4. Server publishes response to: _INBOX.AbC123
5. Client receives response
```

### Wolverine Integration
```csharp
// Seamless mapping to InvokeAsync
var response = await bus.InvokeAsync<OrderStatus>(
    new GetOrderStatus(orderId),
    timeout: TimeSpan.FromSeconds(5)
);
```

## Multi-Tenancy Architecture

### Subject-Based Isolation
```csharp
// Automatic tenant prefixing
opts.ConfigureTenantSubjectPrefix("tenant.{id}");

// Published to: tenant.acme.orders.created
await bus.PublishAsync(new OrderCreated());
```

### Account-Based Isolation (Enterprise)
Complete namespace separation:

```yaml
accounts:
  ACME:
    subjects: acme.>
    jetstream: enabled
    limits:
      connections: 100
      storage: 10GB
```

## Performance Architecture

### Zero-Allocation Design
- Message pooling with `NatsMemoryOwner<T>`
- Struct-based messages
- `ReadOnlySequence<byte>` for payloads
- Minimal heap pressure

### Connection Multiplexing
```
Single TCP Connection
├── Subscription 1  
├── Subscription 2
├── Publisher Channel
└── Request/Reply ops
```

### Backpressure Management
```csharp
// Bounded channels prevent overwhelming
opts.ListenToNatsSubject("high.volume")
    .BufferedInMemory(capacity: 1000)
    .MaximumParallelMessages(20);
```

## Error Handling & Resilience

### Connection Resilience
- Automatic reconnection with exponential backoff
- Subscription state preserved
- Message buffering during disconnects
- Server discovery in clusters

### Dead Letter Queue Pattern
```csharp
// After 3 failures, move to DLQ
opts.ListenToNatsSubject("orders.process")
    .UseJetStream()
    .ConfigureDeadLetterQueue(3, "orders.failures");

// Process DLQ separately
opts.ListenToNatsSubject("orders.failures")
    .UseJetStream()
    .Sequential();  // Process one at a time
```

## Monitoring & Observability

### Built-in Metrics
```bash
# HTTP endpoint with all metrics
curl http://localhost:8222/varz

# Connections info
curl http://localhost:8222/connz

# Routes between servers
curl http://localhost:8222/routez
```

### JetStream Monitoring
```bash
# Stream info
nats stream info ORDERS

# Consumer lag
nats consumer report
```

### Wolverine Integration
- Message counters per endpoint
- Processing duration metrics
- Error rate tracking
- Circuit breaker state

## Security Architecture

### Authentication Layers
1. **Connection**: Token, NKey, User/Pass, TLS client cert
2. **Authorization**: Subject-level publish/subscribe permissions  
3. **Isolation**: Account boundaries for multi-tenancy

### TLS Configuration
```csharp
opts.UseNats("nats://secure.example.com")
    .UseTls()
    .WithClientCertificate(cert, key);
```

## Best Practices

### Subject Design
- Use hierarchical naming: `domain.region.entity.event`
- Keep subjects lowercase
- Avoid special characters except `.` and `_`
- Plan for wildcards from the start

### Stream Design
- One stream per aggregate root or domain
- Consider retention carefully (limits vs workqueue)
- Use consumer filters to avoid reading unnecessary messages
- Plan replica count based on durability needs

### Performance Optimization
- Use queue groups for work distribution
- Configure appropriate buffer sizes
- Consider inline processing for low-latency needs
- Monitor consumer lag and scale accordingly

This architecture provides a foundation that scales from development to production, from single-node to global deployments, while maintaining simplicity and performance.