using Wolverine;
using Wolverine.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWolverine(opts =>
{
    // Use the new fluent API with NatsTransportExpression
    opts.UseNats("nats://localhost:4222")
        .AutoProvision()
        .UseJetStream(js =>
        {
            js.MaxMessages = 100_000;
            js.MaxAge = TimeSpan.FromDays(1);
        })
        .WithSubjectPrefix("myapp")
        .ConfigureTimeouts(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

    // Configure a listener
    opts.ListenToNatsSubject("orders.created")
        .UseForReplies()
        .ProcessInline()
        .ConfigureDeadLetterQueue(3, "orders.dlq");

    // Configure a publisher
    opts.PublishMessage<OrderCreated>().ToNatsSubject("orders.created");
});

var app = builder.Build();

app.MapPost(
    "/orders",
    async (IMessageBus bus) =>
    {
        var order = new OrderCreated(Guid.NewGuid(), DateTime.UtcNow);
        await bus.PublishAsync(order);
        return Results.Ok(order);
    }
);

app.Run();

public record OrderCreated(Guid OrderId, DateTime CreatedAt);

public class OrderCreatedHandler
{
    public void Handle(OrderCreated order)
    {
        Console.WriteLine($"Order created: {order.OrderId} at {order.CreatedAt}");
    }
}
