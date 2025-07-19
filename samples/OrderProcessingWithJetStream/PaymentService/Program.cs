using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentService;
using Shared.Events;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Nats;
using Wolverine.Nats.Internal;

var builder = Host.CreateApplicationBuilder(args);

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
            listener.UseJetStream("ORDERS", "payment-service")
                    .UseQueueGroup("payment-service")
                    .ConfigureDeadLetterQueue(5); // More retries for payment processing
        })
        .ConfigureSenders(sender =>
        {
            sender.UseJetStream("ORDERS");
        });

    // Listen for payment requests - configuration is inherited from defaults
    opts.ListenToNatsSubject("orders.payment.requested");

    // Publish payment events - configuration is inherited from defaults
    opts.PublishMessage<PaymentCompleted>()
        .ToNatsSubject("orders.payment.completed");

    opts.PublishMessage<PaymentFailed>()
        .ToNatsSubject("orders.payment.failed");

    // Configure error handling with longer delays for payment processing
    opts.OnException<PaymentProcessingException>()
        .RetryWithCooldown(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1))
        .Then.MoveToErrorQueue();

    // Enable logging
    opts.Services.AddLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddConsole();
    });
});

var host = builder.Build();

Console.WriteLine("=== Payment Service Starting ===");
Console.WriteLine("Processing payments via JetStream");
Console.WriteLine("Consumer group: payment-service");
Console.WriteLine("Simulating payment processing with 90% success rate");
Console.WriteLine("Press Ctrl+C to exit");
Console.WriteLine("================================");

await host.RunAsync();