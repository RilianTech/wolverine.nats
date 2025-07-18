# Wolverine.Nats

NATS transport support for the Wolverine messaging framework.

## Features

- **Core NATS** messaging support with subject-based routing
- **JetStream** support for reliable, persistent messaging
- **Queue Groups** for load balancing across multiple consumers
- **Authentication** support (username/password, token, NKey, JWT)
- **TLS** support including mutual TLS
- Request/Reply pattern support
- Auto-provisioning of JetStream resources

## Installation

```bash
dotnet add package Wolverine.Nats
```

## Quick Start

### Basic Configuration

```csharp
using var host = Host.CreateDefaultBuilder()
    .UseWolverine(opts =>
    {
        // Connect to NATS
        opts.UseNats("nats://localhost:4222");

        // Publish messages to a subject
        opts.PublishMessage<OrderPlaced>()
            .ToNatsSubject("orders.placed");

        // Listen to messages from a subject
        opts.ListenToNatsSubject("orders.*")
            .DefaultIncomingMessage<OrderMessage>();
    })
    .Build();
```

### Using JetStream

```csharp
using var host = Host.CreateDefaultBuilder()
    .UseWolverine(opts =>
    {
        opts.UseNats("nats://localhost:4222")
            .AutoProvision(); // Auto-create streams and consumers

        // Publish to JetStream
        opts.PublishMessage<OrderPlaced>()
            .ToNatsSubject("orders.placed")
            .UseJetStream("ORDERS");

        // Listen with JetStream consumer
        opts.ListenToNatsSubject("orders.*")
            .UseJetStream("ORDERS", "order-processor")
            .DefaultIncomingMessage<OrderMessage>();
    })
    .Build();
```

### Queue Groups (Load Balancing)

```csharp
opts.ListenToNatsSubject("orders.*")
    .WithQueueGroup("order-processors")
    .DefaultIncomingMessage<OrderMessage>();
```

### Authentication

```csharp
// Username/Password
opts.UseNats("nats://localhost:4222")
    .WithAuthentication("user", "password");

// Token
opts.UseNats("nats://localhost:4222")
    .WithToken("my-auth-token");

// NKey
opts.UseNats("nats://localhost:4222")
    .WithNKey("/path/to/nkey/file");

// JWT
opts.UseNats("nats://localhost:4222")
    .WithJWT("jwt-token", "nkey-seed");
```

### TLS

```csharp
// Enable TLS
opts.UseNats("nats://localhost:4222")
    .WithTls();

// Mutual TLS
opts.UseNats("nats://localhost:4222")
    .WithMutualTls("/path/to/client.crt", "/path/to/client.key");
```

### Advanced JetStream Configuration

```csharp
opts.UseNats("nats://localhost:4222")
    .AutoProvision()
    .ConfigureJetStream(js =>
    {
        js.Retention = "limits";
        js.MaxAge = TimeSpan.FromDays(7);
        js.MaxMessages = 1000000;
        js.Replicas = 3;
        js.AckWait = TimeSpan.FromMinutes(5);
        js.MaxDeliver = 5;
    });
```

## Message Patterns

### Publish/Subscribe

```csharp
// Publisher
await bus.PublishAsync(new OrderPlaced { OrderId = 123 });

// Subscriber
public class OrderPlacedHandler
{
    public Task Handle(OrderPlaced message)
    {
        Console.WriteLine($"Order {message.OrderId} was placed");
        return Task.CompletedTask;
    }
}
```

### Request/Reply

```csharp
// Send request and wait for reply
var response = await bus.InvokeAsync<OrderStatus>(new GetOrderStatus { OrderId = 123 });

// Handler
public class GetOrderStatusHandler
{
    public OrderStatus Handle(GetOrderStatus query)
    {
        return new OrderStatus { OrderId = query.OrderId, Status = "Shipped" };
    }
}
```

## Integration with Existing NATS Infrastructure

The transport maps Wolverine concepts to NATS:

- Wolverine **endpoints** map to NATS **subjects**
- Wolverine **queue groups** map to NATS **queue groups**
- Wolverine **message types** are sent as headers
- Supports both Core NATS (at-most-once) and JetStream (at-least-once)

## Configuration Options

### Transport Configuration

- `ConnectionString`: NATS server URL (default: `nats://localhost:4222`)
- `ConnectTimeout`: Connection timeout (default: 10 seconds)
- `RequestTimeout`: Request/Reply timeout (default: 30 seconds)
- `EnableTls`: Enable TLS connection
- `AutoProvision`: Auto-create JetStream resources
- `AutoPurgeOnStartup`: Purge streams on startup (development only)

### Endpoint Configuration

- `UseJetStream`: Enable JetStream for the endpoint
- `StreamName`: JetStream stream name
- `ConsumerName`: JetStream consumer name (durable)
- `QueueGroup`: Queue group for load balancing
- `CustomHeaders`: Additional headers to include

## Development

This transport is designed to work seamlessly with Wolverine's programming model while leveraging NATS features:

- Automatic serialization/deserialization
- Error handling and retries
- Message correlation and routing
- Integration with Wolverine's durability features

## License

This project follows the same license as the main Wolverine project.