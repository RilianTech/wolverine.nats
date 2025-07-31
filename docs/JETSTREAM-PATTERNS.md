# JetStream Patterns for Wolverine

## Understanding JetStream

JetStream is NATS's persistence layer that adds:
- Message durability and replay
- Exactly-once processing semantics
- Consumer groups with acknowledgments
- Stream processing capabilities

Think of it as Kafka's durability with RabbitMQ's simplicity.

## Core Concepts

### Streams vs Consumers

**Stream**: Stores messages matching subject patterns
```yaml
Stream: ORDERS
Subjects: orders.>
Storage: File
Retention: Limits
Max Messages: 10,000,000
Max Age: 7 days
```

**Consumer**: Reads messages from a stream
```yaml
Consumer: order-processor
Stream: ORDERS
Filter: orders.*.created
Ack Policy: Explicit
Max Deliver: 3
```

### Retention Policies

#### Limits (Event Streaming)
Keep messages based on count/size/age limits.

#### WorkQueue (Task Processing)  
Delete messages after acknowledgment.

For detailed JetStream configuration, see [Configuration Guide](./CONFIGURATION.md#jetstream-configuration).

## Implementation Patterns

### 1. Event Sourcing Pattern

Store all domain events for replay:

```csharp
// Configure event stream
opts.UseNats("nats://localhost:4222")
    .UseJetStream(js => {
        js.Retention = "limits";
        js.MaxAge = TimeSpan.FromDays(365);
        js.DuplicateWindow = TimeSpan.FromMinutes(5);
    });

// Publish events with deduplication
public class OrderSaga : Saga<OrderSagaData>
{
    public async Task Handle(OrderPlaced order, IMessageContext context)
    {
        // Message ID prevents duplicates
        await context.PublishAsync(new OrderEvent
        {
            OrderId = order.Id,
            EventType = "OrderPlaced",
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}

// Replay events from beginning
opts.ListenToNatsSubject("orders.events.>")
    .UseJetStream("EVENTS", opts => {
        opts.DeliverPolicy = DeliverPolicy.All;
        opts.StartTime = DateTime.UtcNow.AddDays(-7);
    });
```

### 2. Work Queue Pattern

Distributed task processing with guarantees:

```csharp
// Publisher
opts.PublishMessage<ProcessImage>()
    .ToNatsSubject("work.images.resize")
    .UseJetStream("WORK_QUEUE");

// Workers with queue group
opts.ListenToNatsSubject("work.images.resize")
    .UseJetStream("WORK_QUEUE", "image-processors")
    .UseQueueGroup("workers")
    .MaximumParallelMessages(4)
    .ConfigureDeadLetterQueue(3, "work.images.failed");

public class ImageProcessor
{
    public async Task Handle(ProcessImage cmd, IMessageContext context)
    {
        try
        {
            await ResizeImage(cmd.ImageUrl);
            // Auto-acknowledged on success
        }
        catch (TemporaryException)
        {
            // Retry with backoff
            throw new RetryException(TimeSpan.FromSeconds(30));
        }
        catch (PermanentException)
        {
            // Move to DLQ
            await context.MoveToDeadLetterQueueAsync();
        }
    }
}
```

### 3. Competing Consumers with Ordering

Process messages in order per partition key:

```csharp
// Publish with partition key
opts.PublishMessage<OrderUpdate>()
    .ToNatsSubject("orders.updates.{CustomerId}");

// Ordered processing per customer
opts.ListenToNatsSubject("orders.updates.*")
    .UseJetStream("ORDERS", config => {
        config.DeliverPolicy = DeliverPolicy.All;
        config.AckPolicy = AckPolicy.Explicit;
    })
    .Sequential()  // One message at a time
    .GroupBy(msg => msg.CustomerId);  // Per customer ordering
```

### 4. Broadcast with Replay

Each service gets all messages:

```csharp
// Service A - Real-time analytics
opts.ListenToNatsSubject("events.>")
    .UseJetStream("EVENTS", "analytics-service")
    .ProcessInline();

// Service B - Batch processing 
opts.ListenToNatsSubject("events.>")
    .UseJetStream("EVENTS", "batch-processor")
    .StartFromBeginning()
    .BufferedInMemory(1000);

// Service C - New service can replay history
opts.ListenToNatsSubject("events.>")
    .UseJetStream("EVENTS", "new-service")
    .ReplayFromTime(DateTime.UtcNow.AddDays(-30));
```

### 5. Scheduled Message Pattern

Using JetStream for delayed delivery:

```csharp
// Schedule a message
public async Task ScheduleReminder(Reminder reminder, TimeSpan delay)
{
    var scheduledTime = DateTimeOffset.UtcNow.Add(delay);
    
    await bus.PublishAsync(reminder, options => {
        options.ToNatsSubject($"scheduled.{scheduledTime:yyyyMMddHHmmss}");
        options.UseJetStream("SCHEDULED");
    });
}

// Process scheduled messages
opts.ListenToNatsSubject("scheduled.*")
    .UseJetStream("SCHEDULED", config => {
        config.DeliverPolicy = DeliverPolicy.ByStartTime;
        config.OptStartTime = DateTime.UtcNow;
    })
    .Sequential();
```

### 6. Message Replay for Testing

Replay production messages in test environment:

```csharp
// Capture production traffic
opts.ListenToNatsSubject("production.>")
    .UseJetStream("AUDIT", config => {
        config.Retention = RetentionPolicy.Limits;
        config.MaxAge = TimeSpan.FromDays(7);
    });

// Replay in test
if (environment.IsTest())
{
    opts.ListenToNatsSubject("production.>")
        .UseJetStream("AUDIT", "test-replay")
        .ReplayAtRate(messagesPerSecond: 100)
        .TransformSubject(s => s.Replace("production", "test"));
}
```

## Advanced JetStream Features

### Consumer Configurations

```csharp
// Pull consumer with batching
opts.ListenToNatsSubject("bulk.process")
    .UseJetStream("BULK", config => {
        config.MaxAckPending = 1000;
        config.MaxBatch = 100;
        config.MaxExpires = TimeSpan.FromSeconds(30);
    })
    .BufferedInMemory(5000);

// Push consumer with rate limiting
opts.ListenToNatsSubject("api.calls")
    .UseJetStream("API", config => {
        config.RateLimit = 100;  // msgs/second
        config.DeliverSubject = "deliver.api";
        config.Heartbeat = TimeSpan.FromSeconds(30);
    });
```

### Stream Templates

Dynamic stream creation:

```yaml
Stream Template: CUSTOMER_STREAMS
Subject Pattern: customers.*.>
Stream Name: CUSTOMER_{{wildcard(1)}}
Max Messages: 100000
Max Age: 30d
```

### Message Deduplication

Prevent duplicate processing:

```csharp
public class OrderHandler
{
    public async Task Handle(CreateOrder cmd, IMessageContext context)
    {
        // Use deterministic ID
        var messageId = $"order-{cmd.OrderId}";
        
        await context.PublishAsync(new OrderCreated(cmd.OrderId), 
            opts => opts.WithMessageId(messageId));
    }
}
```

## Monitoring JetStream

For comprehensive JetStream monitoring and troubleshooting, see [Monitoring & Troubleshooting Guide](./MONITORING-TROUBLESHOOTING.md#jetstream-troubleshooting).

## Best Practices

### 1. Stream Design
- One stream per bounded context
- Use subject hierarchies for filtering
- Plan retention based on replay needs
- Consider geographic replication

### 2. Consumer Design
- Name durable consumers explicitly
- Use queue groups for scaling
- Set appropriate AckWait times
- Monitor consumer lag

### 3. Performance Optimization
- Batch acknowledgments when possible
- Use pull consumers for better control
- Configure MaxAckPending appropriately
- Consider memory vs file storage

### 4. Error Handling
- Set MaxDeliver based on retry strategy
- Use dead letter subjects
- Monitor redelivery counts
- Implement circuit breakers

JetStream transforms NATS from a simple pub/sub system into a complete streaming platform, giving Wolverine applications Kafka-like capabilities with operational simplicity.