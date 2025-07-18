# NATS Transport Configuration Pattern

## Problem
The initial implementation attempted to use extension methods on `ListenerConfiguration` to add NATS-specific configuration options. However, this failed because:
- The `add` method in `DelayedEndpointConfiguration` is `protected`
- Extension methods cannot access protected members
- This resulted in CS0122 compilation errors

## Solution
Following the pattern used by other Wolverine transports (RabbitMQ, SQS, Azure Service Bus), we created:

### 1. Custom Configuration Classes

#### NatsListenerConfiguration
```csharp
public class NatsListenerConfiguration : ListenerConfiguration<NatsListenerConfiguration, NatsEndpoint>
{
    // NATS-specific configuration methods
    public NatsListenerConfiguration UseJetStream(string? streamName = null, string? consumerName = null)
    public NatsListenerConfiguration UseQueueGroup(string queueGroup)
    public NatsListenerConfiguration ConfigureDeadLetterQueue(Action<NatsDeadLetterConfiguration> configure)
    // etc...
}
```

#### NatsSubscriberConfiguration
```csharp
public class NatsSubscriberConfiguration : SubscriberConfiguration<NatsSubscriberConfiguration, NatsEndpoint>
{
    // NATS-specific publishing configuration
    public NatsSubscriberConfiguration UseJetStream(string? streamName = null)
}
```

### 2. Updated Extension Methods
The transport extension methods now return the custom configuration classes:

```csharp
public static NatsListenerConfiguration ListenToNatsSubject(this WolverineOptions options, string subject)
{
    var transport = options.NatsTransport();
    var endpoint = transport.EndpointForSubject(subject);
    endpoint.IsListener = true;
    
    return new NatsListenerConfiguration(endpoint);
}
```

### 3. Transport Helper Methods
Added `EndpointForSubject` method to `NatsTransport` to retrieve or create endpoints for subjects:

```csharp
public NatsEndpoint EndpointForSubject(string subject)
{
    var normalized = NormalizeSubject(subject);
    return _endpoints[normalized];
}
```

## Usage Examples

```csharp
// Basic listener
opts.ListenToNatsSubject("orders.created");

// Listener with JetStream
opts.ListenToNatsSubject("orders.processed")
    .UseJetStream("ORDERS_STREAM", "order-processor");

// Listener with queue group
opts.ListenToNatsSubject("orders.notifications")
    .UseQueueGroup("notification-workers");

// Chain multiple configurations
opts.ListenToNatsSubject("inventory.updated")
    .UseJetStream()
    .UseForReplies()
    .Sequential()
    .CircuitBreaker();
```

## Benefits
1. **Type Safety**: Returns NATS-specific configuration classes
2. **Fluent API**: Enables method chaining for configuration
3. **Consistency**: Follows the same pattern as other Wolverine transports
4. **Extensibility**: Easy to add new NATS-specific configuration options
5. **IntelliSense**: Better IDE support with transport-specific methods