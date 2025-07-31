using Wolverine;
using Wolverine.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWolverine(opts =>
{
    // Configure NATS transport with global settings
    opts.UseNats("nats://localhost:4222")
        // Transport-level configuration affects all endpoints
        .UseJetStream(js =>
        {
            js.Domain = "wolverine-demo";
            js.StreamNamePrefix = "WOLVERINE_";
        })
        .UseTls(insecureSkipVerify: true)
        .WithSubjectPrefix("app")
        // Configure ALL listening endpoints
        .ConfigureListeners(listener =>
        {
            // These settings apply to ALL NATS listeners
            listener
                .UseJetStream()
                .UseQueueGroup("my-service")
                .ConfigureDeadLetterQueue(maxDeliveryAttempts: 3, deadLetterSubject: "dead-letter");
        })
        // Configure ALL sending endpoints
        .ConfigureSenders(sender =>
        {
            // These settings apply to ALL NATS publishers
            sender.UseJetStream();
        });

    // Individual endpoint configuration still works and can override defaults
    opts.ListenToNatsSubject("orders.created")
        .UseQueueGroup("order-processors") // Overrides the default "my-service"
        .ConfigureDeadLetterQueue(5); // Overrides the default 3 attempts

    opts.ListenToNatsSubject("orders.shipped").DisableDeadLetterQueueing(); // Disables DLQ for this specific endpoint

    // Publishing configuration
    opts.PublishMessage<OrderCreated>().ToNatsSubject("orders.created");

    opts.PublishMessage<OrderShipped>().ToNatsSubject("orders.shipped").UseJetStream("ORDERS"); // Override stream name for this endpoint
});

var app = builder.Build();

app.MapGet("/", () => "NATS Endpoint Configuration Example");

app.Run();

public record OrderCreated(Guid OrderId, string CustomerId, decimal Total);

public record OrderShipped(Guid OrderId, DateTime ShippedAt);

// Example handlers
public class OrderHandlers
{
    public void Handle(OrderCreated order)
    {
        Console.WriteLine($"Processing order {order.OrderId} for {order.CustomerId}");
    }

    public void Handle(OrderShipped shipment)
    {
        Console.WriteLine($"Order {shipment.OrderId} shipped at {shipment.ShippedAt}");
    }
}
