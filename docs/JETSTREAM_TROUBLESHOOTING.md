# JetStream Troubleshooting Guide

## Common Issues

### "consumer not found" Error

This error occurs when trying to get a consumer that doesn't exist yet. The updated code now handles this by:

1. First trying to get an existing consumer
2. If that fails, creating a new consumer

### Stream Not Found

If services start before the stream is created, they will fail. Solutions:

1. **Define streams in all services** - Each service can define the same stream configuration
2. **Use a startup script** - Create streams before starting services
3. **Start OrderService first** - Since it defines the stream

### Consumer Configuration

When using durable consumers with JetStream:
- Consumer name should be unique per service
- Use queue groups for load balancing
- Don't set FilterSubject on durable consumers that need to listen to multiple subjects

## Debugging Steps

1. **Check if NATS is running:**
```bash
docker ps | grep nats
```

2. **Check if streams exist:**
```bash
nats stream ls
```

3. **Check stream details:**
```bash
nats stream info ORDERS
```

4. **Check consumers:**
```bash
nats consumer ls ORDERS
```

5. **Check consumer details:**
```bash
nats consumer info ORDERS order-service
```

## Manual Stream Creation

If auto-provisioning isn't working, create the stream manually:

```bash
nats stream add ORDERS \
  --subjects "orders.>,payment.>,inventory.>" \
  --retention limits \
  --max-msgs 1000000 \
  --max-bytes 1g \
  --max-age 30d \
  --storage file \
  --replicas 1
```

## Testing Individual Services

Test services one at a time to isolate issues:

```bash
# Terminal 1 - Start NATS
docker-compose up -d

# Terminal 2 - Create stream manually if needed
nats stream add ORDERS --subjects "orders.>,payment.>,inventory.>"

# Terminal 3 - Start one service
dotnet run --project OrderService

# Check if consumer was created
nats consumer ls ORDERS
```

## Consumer Patterns

### Single Subject Consumer (Ephemeral)
```csharp
opts.ListenToNatsSubject("orders.created");
```

### Multi-Subject Consumer (Durable)
```csharp
opts.UseNats("nats://localhost:4222")
    .ConfigureListeners(listener =>
    {
        listener.UseJetStream("ORDERS", "my-service")
                .UseQueueGroup("my-service");
    });

// All listeners share the same consumer
opts.ListenToNatsSubject("orders.created");
opts.ListenToNatsSubject("orders.updated");
```

## Log Analysis

Look for these log messages:

```
info: Wolverine.Nats.Internal.NatsTransport[0]
      JetStream context initialized
      
info: Wolverine.Nats.Internal.NatsTransport[0]
      Created stream ORDERS with subjects: orders.>, payment.>, inventory.>
      
info: Wolverine.Nats.Internal.NatsListener[0]
      Created new consumer order-service
```

## Architecture Considerations

### Option 1: Shared Stream Definition
All services define the same stream. First one to start creates it.

### Option 2: Dedicated Setup Service
A separate service creates all streams before other services start.

### Option 3: Manual Setup
Use NATS CLI or admin scripts to create streams before deployment.