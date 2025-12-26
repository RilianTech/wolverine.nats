using Wolverine;
using Wolverine.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWolverine(opts =>
{
    opts.UseNats("nats://localhost:4222")
        .UseJetStream(js =>
        {
            js.Domain = "wolverine-demo";
            js.StreamNamePrefix = "WOLVERINE_";
        })
        .UseTls(insecureSkipVerify: true)
        .WithSubjectPrefix("app")
        .ConfigureListeners(listener =>
        {
            listener
                .UseJetStream()
                .UseQueueGroup("my-service")
                .ConfigureDeadLetterQueue(maxDeliveryAttempts: 3, deadLetterSubject: "dead-letter");
        })
        .ConfigureSenders(sender =>
        {
            sender.UseJetStream();
        });

    opts.ListenToNatsSubject("orders.created")
        .UseQueueGroup("order-processors")
        .ConfigureDeadLetterQueue(5);

    opts.ListenToNatsSubject("orders.shipped").DisableDeadLetterQueueing();

    opts.PublishMessage<OrderCreated>().ToNatsSubject("orders.created");
    opts.PublishMessage<OrderShipped>().ToNatsSubject("orders.shipped").UseJetStream("ORDERS");
});

var app = builder.Build();

app.MapGet("/", () => "NATS Endpoint Configuration Example");

app.Run();

public record OrderCreated(Guid OrderId, string CustomerId, decimal Total);

public record OrderShipped(Guid OrderId, DateTime ShippedAt);

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
