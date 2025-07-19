# NATS + Wolverine Quick Start

Get up and running with NATS and Wolverine in 15 minutes.

## Prerequisites

1. **Install NATS Server**
   ```bash
   # macOS
   brew install nats-server
   
   # Windows
   choco install nats-server
   
   # Docker
   docker run -p 4222:4222 -p 8222:8222 nats:latest
   ```

2. **Install NATS CLI** (optional but recommended)
   ```bash
   # macOS/Linux
   brew install nats-io/nats-tools/nats
   
   # Go install
   go install github.com/nats-io/natscli/nats@latest
   ```

## Step 1: Create a New Project

```bash
dotnet new web -n QuickStartNats
cd QuickStartNats
dotnet add package Wolverine.Nats
```

## Step 2: Basic Pub/Sub

```csharp
// Program.cs
using Wolverine;
using Wolverine.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWolverine(opts =>
{
    // Connect to NATS
    opts.UseNats("nats://localhost:4222");

    // Listen to a subject
    opts.ListenToNatsSubject("greetings");
    
    // Optional: configure how messages are processed
    opts.Discovery.DisableConventionalDiscovery();
});

var app = builder.Build();

// HTTP endpoint to send messages
app.MapPost("/greet", async (IMessageBus bus, string name) =>
{
    await bus.PublishAsync(new Greeting { Name = name });
    return Results.Ok($"Greeting sent for {name}");
});

app.Run();

// Message types
public record Greeting(string Name);

// Message handler
public class GreetingHandler
{
    public void Handle(Greeting greeting)
    {
        Console.WriteLine($"Hello, {greeting.Name}!");
    }
}
```

## Step 3: Run and Test

1. **Start NATS Server**
   ```bash
   nats-server
   ```

2. **Run the application**
   ```bash
   dotnet run
   ```

3. **Send a message**
   ```bash
   curl -X POST http://localhost:5000/greet?name=World
   ```

4. **Verify output**
   You should see: `Hello, World!`

## Step 4: Add JetStream for Durability

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseNats("nats://localhost:4222")
        .AutoProvision()  // Auto-create streams
        .UseJetStream(js =>
        {
            js.MaxMessages = 100_000;
            js.MaxAge = TimeSpan.FromDays(7);
        });

    // Durable subscription
    opts.ListenToNatsSubject("orders.created")
        .UseJetStream("ORDERS")  // Stream name
        .UseQueueGroup("order-processors");  // Load balancing
});
```

## Step 5: Request/Reply Pattern

```csharp
// Add request/reply endpoint
app.MapGet("/calculate", async (IMessageBus bus, int a, int b) =>
{
    var result = await bus.InvokeAsync<CalculationResult>(
        new Calculate { A = a, B = b },
        timeout: TimeSpan.FromSeconds(5)
    );
    
    return Results.Ok(result);
});

// Request message
public record Calculate(int A, int B);

// Response message  
public record CalculationResult(int Sum);

// Handler that returns a response
public class CalculateHandler
{
    public CalculationResult Handle(Calculate calc)
    {
        return new CalculationResult(calc.A + calc.B);
    }
}
```

## Common Patterns

### Load Balancing with Queue Groups

```csharp
// Multiple instances will share the work
opts.ListenToNatsSubject("work.items")
    .UseQueueGroup("workers")
    .MaximumParallelMessages(10);
```

### Dead Letter Queue

```csharp
opts.ListenToNatsSubject("important.tasks")
    .UseJetStream("TASKS")
    .ConfigureDeadLetterQueue(3, "failed.tasks");
```

### Wildcards Subscriptions

```csharp
// Listen to all order events
opts.ListenToNatsSubject("orders.>")
    .ProcessInline();

// Listen to orders from any region
opts.ListenToNatsSubject("orders.*.created")
    .UseQueueGroup("order-handlers");
```

### Subject Hierarchies

```csharp
// Publish to specific subjects
await bus.PublishAsync(new OrderCreated(), opts => 
    opts.ToNatsSubject("orders.us-west.created"));

await bus.PublishAsync(new OrderShipped(), opts => 
    opts.ToNatsSubject("orders.us-west.shipped"));
```

## Monitoring

### NATS Server Monitoring

```bash
# View server stats
curl http://localhost:8222/varz

# Monitor message flow
nats sub ">"

# Check JetStream streams
nats stream ls
```

### Wolverine Metrics

```csharp
// Add metrics endpoint
app.MapGet("/metrics", (IMessageBus bus) =>
{
    var metrics = bus.GetMetrics();
    return Results.Ok(metrics);
});
```

## Docker Compose Setup

```yaml
# docker-compose.yml
version: '3.8'

services:
  nats:
    image: nats:latest
    ports:
      - "4222:4222"  # Client port
      - "8222:8222"  # Monitoring port
    command: "-js -m 8222"  # Enable JetStream
    
  app:
    build: .
    depends_on:
      - nats
    environment:
      - NATS_URL=nats://nats:4222
```

## Troubleshooting

### Connection Issues
```csharp
opts.UseNats(config =>
{
    config.ConnectionString = "nats://localhost:4222";
    config.ConnectTimeout = TimeSpan.FromSeconds(10);
    // Enable verbose logging
    config.LoggerFactory = loggerFactory;
});
```

### Message Not Received
1. Check subject names match exactly
2. Verify NATS server is running: `nats server check`
3. Monitor subjects: `nats sub ">"`
4. Check Wolverine logs for errors

### JetStream Issues
```bash
# Check stream status
nats stream info STREAM_NAME

# View consumer status
nats consumer info STREAM_NAME CONSUMER_NAME

# Purge messages
nats stream purge STREAM_NAME
```

## Next Steps

1. **Explore JetStream** - Add persistence to your messages
2. **Try Queue Groups** - Scale your message processing
3. **Add Security** - Configure authentication and TLS
4. **Monitor Performance** - Set up metrics and alerts
5. **Read Architecture Guide** - Understand the deeper concepts

## Useful Resources

- [NATS Documentation](https://docs.nats.io)
- [Wolverine Documentation](https://wolverine.netlify.app)
- [NATS CLI Cheat Sheet](https://docs.nats.io/using-nats/nats-tools/nats_cli)
- [JetStream Concepts](https://docs.nats.io/nats-concepts/jetstream)

You now have a working NATS + Wolverine application! The combination provides a powerful, scalable messaging platform that grows with your needs.