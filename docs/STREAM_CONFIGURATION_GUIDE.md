# NATS JetStream Stream Configuration Guide

This guide explains how to configure NATS JetStream streams in Wolverine.Nats, including the new configuration helpers that make stream setup easier and help prevent common configuration errors.

## Overview

NATS JetStream streams are persistent, replicated data stores that retain messages according to configured policies. Wolverine.Nats provides several ways to configure streams:

1. **Automatic Stream Creation** - Let Wolverine create streams automatically based on usage
2. **Pre-defined Stream Configuration** - Define streams upfront with specific settings
3. **Configuration Helpers** - Use fluent methods for common stream patterns

## Stream Configuration Helpers

### Basic Stream Definition

Use the `DefineStream` method to configure a stream with full control:

```csharp
opts.UseNats("nats://localhost:4222")
    .DefineStream("ORDERS", stream =>
    {
        stream.WithSubjects("orders.>", "payment.>", "inventory.>")
              .WithLimits(
                  maxMessages: 1_000_000,
                  maxBytes: 1024L * 1024 * 1024, // 1GB
                  maxAge: TimeSpan.FromDays(30)
              )
              .WithReplicas(3); // For production HA
    });
```

### Work Queue Streams

For work queue patterns where messages should be retained only while there are active consumers:

```csharp
opts.UseNats("nats://localhost:4222")
    .DefineWorkQueueStream("TASKS", "tasks.>", "jobs.>");
```

This creates a stream with:
- Retention policy set to "interest" (keeps messages only while consumers exist)
- Specified subjects for task/job processing

### Log Streams

For event sourcing or audit log scenarios where messages should be retained for a specific time:

```csharp
opts.UseNats("nats://localhost:4222")
    .DefineLogStream("AUDIT_LOG", 
        retention: TimeSpan.FromDays(90),
        subjects: "audit.>", "security.>");
```

### High Availability Streams

For production environments requiring replication:

```csharp
opts.UseNats("nats://localhost:4222")
    .DefineReplicatedStream("CRITICAL_EVENTS", 
        replicas: 3,
        subjects: "payments.>", "orders.>");
```

## Subject Conflicts

NATS does not allow multiple streams to listen to overlapping subjects. When provisioning streams, NATS will return an error if you try to create a stream with subjects that overlap with an existing stream:

```csharp
// This will fail if another stream already listens to "orders.>"
opts.UseNats("nats://localhost:4222")
    .DefineStream("NEW_ORDERS", stream =>
    {
        stream.WithSubjects("orders.>"); // Error if ORDERS stream exists
    });
```

NATS enforces subject uniqueness including:
- Exact subject matches
- Wildcard conflicts (`*` and `>`)
- Overlapping subject hierarchies

## Complete Example

Here's a complete example showing stream configuration with endpoint setup:

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseNats("nats://localhost:4222")
        // Define streams before configuring endpoints
        .DefineStream("ORDERS", stream =>
        {
            stream.WithSubjects("orders.>", "payment.>", "inventory.>")
                  .WithLimits(maxMessages: 1_000_000)
                  .WithReplicas(1);
        })
        .DefineStream("NOTIFICATIONS", stream =>
        {
            stream.WithSubjects("notify.>")
                  .AsWorkQueue(); // Retention by interest
        })
        // Configure default behavior for all endpoints
        .ConfigureListeners(listener =>
        {
            listener.UseQueueGroup("my-service");
        })
        .ConfigureSenders(sender =>
        {
            sender.UseJetStream(); // Will auto-select appropriate stream
        });

    // Configure specific endpoints
    opts.ListenToNatsSubject("orders.created")
        .UseJetStream("ORDERS", "order-processor");
        
    opts.PublishMessage<OrderCreated>()
        .ToNatsSubject("orders.created");
});
```

## Stream Configuration Options

The `StreamConfiguration` class supports all NATS JetStream stream options:

```csharp
stream.WithSubjects("subject1", "subject2")      // Required: subjects to capture
      .WithLimits(                               // Retention limits
          maxMessages: 1_000_000,
          maxBytes: 1024L * 1024 * 1024,
          maxAge: TimeSpan.FromDays(30)
      )
      .WithReplicas(3)                           // Number of replicas
      .AsWorkQueue()                             // Retention by interest
      .Storage = StreamConfigurationStorageType.Memory;  // Storage type
```

### Available Properties

- `Name` - Stream name (set automatically)
- `Subjects` - List of subjects the stream captures
- `Retention` - Limits, Interest, or WorkQueue
- `Storage` - File or Memory
- `MaxMessages` - Maximum number of messages
- `MaxBytes` - Maximum total size in bytes
- `MaxAge` - Maximum age of messages
- `MaxMessagesPerSubject` - Per-subject message limit
- `DiscardPolicy` - Old or New when limits reached
- `Replicas` - Number of replicas (1-5)
- `AllowRollup` - Enable message rollup
- `AllowDirect` - Enable direct get API
- `DenyDelete` - Prevent message deletion
- `DenyPurge` - Prevent stream purge

## Auto-Provisioning

By default, Wolverine.Nats will automatically create configured streams on startup:

```csharp
opts.UseNats("nats://localhost:4222", config =>
{
    config.AutoProvision = true; // Default
});
```

To disable auto-provisioning:

```csharp
config.AutoProvision = false;
```

## Best Practices

1. **Define Streams Upfront** - Use configuration helpers to define streams before configuring endpoints
2. **Use Meaningful Stream Names** - Use uppercase names that describe the domain (e.g., ORDERS, PAYMENTS)
3. **Plan Subject Hierarchies** - Design subjects to avoid conflicts between streams
4. **Set Appropriate Limits** - Configure retention based on your data requirements
5. **Use Replication in Production** - Set replicas to 3 or 5 for high availability
6. **Monitor Stream Health** - Check stream stats and consumer lag regularly

## Migration from Manual Stream Creation

If you previously created streams manually, you can migrate to configuration helpers:

### Before:
```bash
nats stream add ORDERS --subjects "orders.>" --max-msgs=1000000
```

### After:
```csharp
opts.UseNats("nats://localhost:4222")
    .DefineStream("ORDERS", stream =>
    {
        stream.WithSubjects("orders.>")
              .WithLimits(maxMessages: 1_000_000);
    });
```

The configuration helpers ensure:
- Streams are created consistently across environments
- Subject conflicts are detected early
- Configuration is version-controlled with your code
- Team members don't need to know NATS CLI commands