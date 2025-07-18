using Wolverine;
using Wolverine.Nats;
using Wolverine.Nats.Configuration;

// Example demonstrating how to configure NATS endpoints with the new pattern

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWolverine(opts =>
{
    // Configure NATS transport
    opts.UseNats("nats://localhost:4222");
    
    // Example 1: Basic listener
    opts.ListenToNatsSubject("orders.created");
    
    // Example 2: Listener with JetStream
    opts.ListenToNatsSubject("orders.processed")
        .UseJetStream("ORDERS_STREAM", "order-processor");
    
    // Example 3: Listener with queue group for load balancing
    opts.ListenToNatsSubject("orders.notifications")
        .UseQueueGroup("notification-workers");
    
    // Example 4: Listener with dead letter queue configuration
    opts.ListenToNatsSubject("payments.process")
        .UseJetStream()
        .ConfigureDeadLetterQueue(dlq =>
        {
            dlq.Enabled = true;
            dlq.DeadLetterSubject = "payments.failed";
            dlq.MaxDeliveryAttempts = 3;
        });
    
    // Example 5: Publisher configuration
    opts.PublishMessage<OrderCreated>()
        .ToNatsSubject("orders.created")
        .UseJetStream("ORDERS_STREAM");
    
    // Example 6: Chaining multiple configurations
    opts.ListenToNatsSubject("inventory.updated")
        .UseJetStream()
        .UseForReplies()
        .Sequential()
        .CircuitBreaker(cb =>
        {
            cb.FailureThreshold = 5;
            cb.PauseTime = TimeSpan.FromSeconds(30);
        });
});

var app = builder.Build();
app.Run();

// Sample message types
public record OrderCreated(Guid OrderId, decimal Total);
public record OrderProcessed(Guid OrderId, DateTime ProcessedAt);