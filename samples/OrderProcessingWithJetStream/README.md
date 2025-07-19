# Order Processing with JetStream Sample

This sample demonstrates a complete order processing system using Wolverine.Nats with NATS JetStream, showcasing:

- Stream configuration helpers for easy setup
- Work queue patterns with queue groups
- Dead letter queue handling
- Event sourcing with JetStream

## Architecture

The system consists of three services:

1. **OrderService** - REST API for order management
2. **InventoryService** - Handles inventory reservations
3. **PaymentService** - Processes payments

All services communicate through NATS JetStream using the ORDERS stream.

## Stream Configuration

The OrderService demonstrates using the new stream configuration helpers:

```csharp
opts.UseNats("nats://localhost:4223")
    .DefineStream("ORDERS", stream =>
    {
        stream.WithSubjects(
            "orders.>",      // All order-related subjects
            "payment.>",     // Payment events 
            "inventory.>"    // Inventory events
        )
        .WithLimits(
            maxMessages: 1_000_000,  // 1M messages max
            maxBytes: 1024L * 1024 * 1024, // 1GB storage
            maxAge: TimeSpan.FromDays(30)  // 30 days retention
        )
        .WithReplicas(1);  // Single replica for development
    });
```

## Running the Sample

1. Start NATS with JetStream:
```bash
docker-compose up -d
```

2. Run all services:
```bash
# Terminal 1
dotnet run --project OrderService

# Terminal 2
dotnet run --project InventoryService

# Terminal 3
dotnet run --project PaymentService
```

3. Create an order:
```bash
curl -X POST http://localhost:5100/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "CUST123",
    "items": [
      {"productId": "LAPTOP-001", "quantity": 1, "price": 999.99},
      {"productId": "MOUSE-001", "quantity": 2, "price": 29.99}
    ]
  }'
```

## Event Flow

1. **Order Created** → `orders.created`
   - InventoryService reserves inventory
   - Responds with `orders.inventory.reserved` or `orders.inventory.failed`

2. **Inventory Reserved** → `orders.payment.requested`
   - PaymentService processes payment
   - Responds with `orders.payment.completed` or `orders.payment.failed`

3. **Payment Completed** → Order marked as completed
4. **Any Failure** → Order cancelled, inventory released

## Load Balancing

All services use queue groups for horizontal scaling:
- Multiple instances of each service can run
- NATS distributes messages among instances
- No duplicate processing

## Error Handling

Each service configures dead letter queues:
- Failed messages retry with exponential backoff
- After max retries, messages move to dead letter queue
- Different retry policies per service type

## Monitoring

Check stream status:
```bash
nats stream info ORDERS
```

View consumer details:
```bash
nats consumer info ORDERS order-service
nats consumer info ORDERS inventory-service
nats consumer info ORDERS payment-service
```

## Configuration Patterns

### Work Queue Stream
For task processing with retention only while consumers exist:
```csharp
.DefineWorkQueueStream("TASKS", "tasks.>", "jobs.>")
```

### Log Stream
For audit logs with time-based retention:
```csharp
.DefineLogStream("AUDIT", TimeSpan.FromDays(90), "audit.>")
```

### Replicated Stream
For production high availability:
```csharp
.DefineReplicatedStream("CRITICAL", replicas: 3, "orders.>")
```