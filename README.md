# Wolverine.Nats

NATS transport support for the Wolverine messaging framework, enabling high-performance messaging with both Core NATS and JetStream.

## Features

- **Core NATS** messaging support with subject-based routing and at-most-once delivery
- **JetStream** support for reliable, persistent messaging with at-least-once delivery
- **Queue Groups** for load balancing across multiple consumers  
- **Dead Letter Queue** support with configurable retry policies
- **Authentication** support (username/password, token, NKey, JWT)
- **TLS** support including mutual TLS
- **MQTT Gateway Integration** - Connect MQTT devices through NATS server's MQTT gateway
- **Stream Management** - Automatic JetStream stream and consumer creation
- **IBrokerEndpoint** compliance for proper resource lifecycle management

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
        // Connect to NATS with fluent configuration
        opts.UseNats("nats://localhost:4222")
            .AutoProvision()  // Auto-create streams and consumers
            .UseJetStream(js =>
            {
                js.MaxMessages = 100_000;
                js.MaxAge = TimeSpan.FromDays(7);
            });

        // Publish messages to a subject
        opts.PublishMessage<OrderPlaced>()
            .ToNatsSubject("orders.placed");

        // Listen to messages from a subject
        opts.ListenToNatsSubject("orders.placed")
            .ProcessInline();
    })
    .Build();
```

### Advanced Configuration with Fluent API

```csharp
using var host = Host.CreateDefaultBuilder()
    .UseWolverine(opts =>
    {
        // Configure NATS with all options
        opts.UseNats("nats://localhost:4222")
            .AutoProvision()
            .WithSubjectPrefix("myapp")  // Prefix all subjects
            .WithCredentials("user", "password")
            .UseTls()
            .ConfigureTimeouts(
                connectTimeout: TimeSpan.FromSeconds(5),
                requestTimeout: TimeSpan.FromSeconds(30)
            )
            .UseJetStream(js =>
            {
                js.Retention = "workqueue";
                js.MaxMessages = 1_000_000;
                js.AckWait = TimeSpan.FromMinutes(1);
            });

        // Configure endpoints
        opts.ListenToNatsSubject("orders.placed")
            .UseJetStream("ORDERS", "order-processor")
            .UseQueueGroup("order-processors")
            .ProcessInline()
            .ConfigureDeadLetterQueue(3, "orders.dlq");
    })
    .Build();
```

### Queue Groups (Load Balancing)

```csharp
opts.ListenToNatsSubject("orders.placed")
    .UseQueueGroup("order-processors");
```

### Dead Letter Queue Configuration

```csharp
// Configure with max attempts and dead letter subject
opts.ListenToNatsSubject("orders.placed")
    .UseJetStream("ORDERS", "order-processor")
    .ConfigureDeadLetterQueue(3, "orders.dlq");

// Just set the dead letter subject
opts.ListenToNatsSubject("payments.process")
    .UseJetStream()
    .DeadLetterTo("payments.errors");

// Disable DLQ
opts.ListenToNatsSubject("notifications.send")
    .UseJetStream()
    .DisableDeadLetterQueueing();
```

### MQTT Gateway Integration

```csharp
// Connect MQTT devices through NATS MQTT gateway
opts.UseNats("nats://localhost:4222"); // NATS server with MQTT gateway enabled

// MQTT devices publish to "sensors/temperature" 
// Wolverine receives on "sensors.temperature"
opts.ListenToNatsSubject("sensors.temperature");

// Wolverine publishes to "commands.device.123"
// MQTT devices receive on "commands/device/123"
opts.PublishMessage<DeviceCommand>()
    .ToNatsSubject("commands.device.{DeviceId}");
```

### Authentication

```csharp
// Username/Password
opts.UseNats("nats://localhost:4222")
    .WithCredentials("user", "password");

// Token
opts.UseNats("nats://localhost:4222")
    .WithToken("my-auth-token");

// NKey
opts.UseNats("nats://localhost:4222")
    .WithNKey("/path/to/nkey/file");

// Advanced configuration
opts.UseNats(config =>
{
    config.ConnectionString = "nats://localhost:4222";
    config.CredentialsFile = "/path/to/.creds";
    config.Jwt = "my-jwt-token";
    config.NKeySeed = "my-nkey-seed";
});
```

### TLS Configuration

```csharp
// Basic TLS
opts.UseNats("nats://localhost:4222")
    .UseTls();

// TLS with insecure skip verify (development only)
opts.UseNats("nats://localhost:4222")
    .UseTls(insecureSkipVerify: true);

// Advanced TLS configuration
opts.UseNats(config =>
{
    config.ConnectionString = "nats://localhost:4222";
    config.EnableTls = true;
    config.ClientCertFile = "/path/to/client.crt";
    config.ClientKeyFile = "/path/to/client.key";
    config.CaFile = "/path/to/ca.crt";
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
// Send a request and wait for a response
var response = await bus.InvokeAsync<OrderStatus>(new GetOrderStatus { OrderId = 123 });

// Or with timeout
var response = await bus.InvokeAsync<OrderStatus>(
    new GetOrderStatus { OrderId = 123 },
    timeout: TimeSpan.FromSeconds(30)
);

// Handler automatically sends the response back
public class OrderStatusHandler
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
- Wolverine **message types** are preserved in NATS headers
- Supports both Core NATS (at-most-once) and JetStream (at-least-once)
- **MQTT Gateway** - Seamlessly integrate MQTT devices through NATS MQTT gateway
- **Stream Management** - Automatic JetStream stream and consumer lifecycle

### NATS Subject Conventions

```csharp
// Core NATS subjects
"orders.placed"     // Simple subject
"orders.*.status"   // Single-level wildcard  
"orders.>"          // Multi-level wildcard

// JetStream with automatic stream creation
"ORDERS.placed"     // Stream: ORDERS, Subject: ORDERS.placed
```

## Configuration Options

### Transport Configuration (`NatsTransportConfiguration`)

| Property | Description | Default |
|----------|-------------|---------|
| `ConnectionString` | NATS server URL | `nats://localhost:4222` |
| `ConnectTimeout` | Connection timeout | 10 seconds |
| `RequestTimeout` | Request/Reply timeout | 30 seconds |  
| `Username` | Authentication username | null |
| `Password` | Authentication password | null |
| `Token` | Authentication token | null |
| `NKeyFile` | NKey file path | null |
| `EnableTls` | Enable TLS connection | false |
| `TlsInsecure` | Skip TLS verification (dev only) | false |
| `EnableJetStream` | Enable JetStream globally | false |
| `JetStreamDomain` | JetStream domain | null |

### Endpoint Configuration  

| Method | Description |
|--------|-------------|
| `UseJetStream(streamName, consumerName)` | Enable JetStream for durable messaging |
| `UseQueueGroup(groupName)` | Enable load balancing across consumers |
| `ConfigureDeadLetterQueue(config)` | Configure retry and DLQ behavior |
| `DisableDeadLetterQueueing()` | Disable dead letter queue handling |
| `DeadLetterTo(subject)` | Set dead letter subject |

### Dead Letter Queue Configuration

Dead letter queue configuration is now directly on endpoints:

| Method | Description |
|--------|-------------|
| `ConfigureDeadLetterQueue(maxAttempts, dlqSubject)` | Configure DLQ with retry count |
| `DeadLetterTo(subject)` | Set dead letter subject |
| `DisableDeadLetterQueueing()` | Disable DLQ handling |

## Getting Started

### Running NATS Server

For development and testing, use the provided Docker Compose setup:

```bash
# Start NATS server with JetStream
docker compose up -d

# Check if NATS is running
docker compose logs wolverine-nats-test

# Check health
docker compose ps

# Access monitoring at http://localhost:8222
```

**Docker Configuration:**
- **Port**: 4222 (standard NATS port)
- **Monitoring**: 8222 (NATS monitoring interface)
- **JetStream**: Enabled with 1GB memory, 10GB file storage
- **Data**: Persisted in Docker volume `wolverine-nats-data`

> **Note**: If you have NATS running locally on port 4222, copy `docker-compose.override.yml.example` to `docker-compose.override.yml` to use alternative ports.

**Cleanup:**
```bash
# Stop and remove containers
docker compose down

# Remove data volume (optional)
docker volume rm wolverine-nats_wolverine-nats-data
```

### Testing with Sample Applications

#### Basic Ping/Pong Sample
```bash
# Terminal 1 - Start the Ponger (receives messages)
cd samples/PingPongWithNats/Ponger
dotnet run

# Terminal 2 - Start the Pinger (sends messages)
cd samples/PingPongWithNats/Pinger
dotnet run
```

#### Real-World Order Processing Sample
Demonstrates JetStream, consumer groups, and saga pattern:
```bash
# Start all services (each in separate terminal)
cd samples/OrderProcessingWithJetStream
dotnet run --project OrderService      # API on port 5000
dotnet run --project InventoryService   # Can run multiple instances
dotnet run --project PaymentService     # Simulates payment processing

# Create an order via API
curl -X POST http://localhost:5000/api/orders -H "Content-Type: application/json" -d @order.json
```

See `samples/OrderProcessingWithJetStream/README.md` for detailed architecture and usage.

## Current Implementation Status

### ✅ Completed Features
- **Core NATS Transport** - Full pub/sub messaging support
- **JetStream Integration** - Durable messaging with stream management
- **Request/Reply Pattern** - `InvokeAsync<T>` support with automatic correlation
- **Dead Letter Queue** - Configurable retry and error handling
- **Queue Groups** - Load balancing across consumers
- **Stream Lifecycle** - Automatic stream and consumer management
- **Authentication & TLS** - Multiple authentication methods
- **MQTT Gateway Ready** - Works with NATS MQTT gateway out-of-the-box

### 🚧 Planned Features
- **Multi-Tenancy** - Account-based isolation (future consideration)

### ⚠️ Known Limitations

#### Scheduled Send
NATS does not support native scheduled send functionality. When using Wolverine's scheduled send features (e.g., `SendAsync` with `DeliveryOptions.ScheduleDelay`), messages will be wrapped in Wolverine's internal "scheduled-envelope" format and held by Wolverine until the scheduled time.

This differs from NATS JetStream's NAK with delay functionality, which is designed for consumer-side message redelivery rather than producer-side scheduled sending. See [NATS Server Issue #2846](https://github.com/nats-io/nats-server/issues/2846) for more details on NATS's approach to delayed message delivery.

## Architecture

This transport follows Wolverine's standard transport patterns:

- **`NatsTransport`** - Main transport inheriting from `BrokerTransport<NatsEndpoint>`
- **`NatsTransportExpression`** - Fluent configuration API following Wolverine patterns
- **`NatsEndpoint`** - Endpoint with `IBrokerEndpoint` support
- **`NatsListener`** - Message listener with `ISupportDeadLetterQueue`
- **`NatsSender`** - Message publisher
- **`NatsEnvelopeMapper`** - Envelope ↔ NATS message mapping
- **Configuration Classes** - Strongly-typed configuration with fluent API

All classes follow Wolverine's naming conventions and are located in the `Internal` namespace.

### Message Flow

```
Wolverine Message → NatsEnvelopeMapper → NATS Message → Network
Network → NATS Message → NatsEnvelopeMapper → Wolverine Message
```

## Contributing

This transport is part of the Wolverine ecosystem. For issues and contributions, please follow the main Wolverine project guidelines.

## License

This project follows the same license as the main Wolverine project.