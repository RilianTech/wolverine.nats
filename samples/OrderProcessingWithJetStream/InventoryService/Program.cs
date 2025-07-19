using InventoryService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Events;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Nats;
using Wolverine.Nats.Internal;

var builder = Host.CreateApplicationBuilder(args);

// Add services
builder.Services.AddSingleton<IInventoryRepository, InMemoryInventoryRepository>();
builder.Services.AddHostedService<InventoryInitializer>();

// Configure Wolverine with NATS JetStream
builder.UseWolverine(opts =>
{
    // Configure NATS transport with defaults for all endpoints
    opts.UseNats("nats://localhost:4223") // Using docker-compose port
        // Define the ORDERS stream (will only create if doesn't exist)
        .DefineStream("ORDERS", stream =>
        {
            stream.WithSubjects(
                "orders.>",              // All order-related subjects
                "payment.>",             // Payment events 
                "inventory.>"            // Inventory events
            )
            .WithLimits(
                maxMessages: 1_000_000,  // 1M messages max
                maxBytes: 1024L * 1024 * 1024, // 1GB storage
                maxAge: TimeSpan.FromDays(30)  // 30 days retention
            )
            .WithReplicas(1);  // Single replica for development
        })
        .ConfigureListeners(listener =>
        {
            listener.UseJetStream("ORDERS", "inventory-service")
                    .UseQueueGroup("inventory-service")
                    .ConfigureDeadLetterQueue(3);
        })
        .ConfigureSenders(sender =>
        {
            sender.UseJetStream("ORDERS");
        });

    // Listen for order events - configuration is inherited from defaults
    opts.ListenToNatsSubject("orders.created");
    opts.ListenToNatsSubject("orders.cancelled");
    opts.ListenToNatsSubject("orders.payment.failed");

    // Publish inventory events - configuration is inherited from defaults
    opts.PublishMessage<InventoryReserved>()
        .ToNatsSubject("orders.inventory.reserved");

    opts.PublishMessage<InventoryReservationFailed>()
        .ToNatsSubject("orders.inventory.failed");

    opts.PublishMessage<InventoryReleased>()
        .ToNatsSubject("orders.inventory.released");

    // Configure error handling - retry with exponential backoff
    opts.OnException<InvalidOperationException>()
        .RetryWithCooldown(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10))
        .Then.MoveToErrorQueue();

    // Enable detailed logging
    opts.Services.AddLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddConsole();
        logging.AddFilter("Wolverine.Nats", LogLevel.Debug);
    });
});

var host = builder.Build();

Console.WriteLine("=== Inventory Service Starting ===");
Console.WriteLine("Listening for order events on JetStream");
Console.WriteLine("Consumer group: inventory-service");
Console.WriteLine("This service can be scaled horizontally for load balancing");
Console.WriteLine("Press Ctrl+C to exit");
Console.WriteLine("================================");

await host.RunAsync();