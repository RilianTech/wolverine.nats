using Wolverine;
using Wolverine.Nats;
using Wolverine.Nats.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWolverine(opts =>
{
    opts.UseNats("nats://localhost:4222");

    opts.ListenToNatsSubject("orders.created");

    opts.ListenToNatsSubject("orders.processed").UseJetStream("ORDERS_STREAM", "order-processor");

    opts.ListenToNatsSubject("orders.notifications").UseQueueGroup("notification-workers");

    opts.ListenToNatsSubject("payments.process")
        .UseJetStream()
        .ConfigureDeadLetterQueue(dlq =>
        {
            dlq.Enabled = true;
            dlq.DeadLetterSubject = "payments.failed";
            dlq.MaxDeliveryAttempts = 3;
        });

    opts.PublishMessage<OrderCreated>()
        .ToNatsSubject("orders.created")
        .UseJetStream("ORDERS_STREAM");

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

public record OrderCreated(Guid OrderId, decimal Total);

public record OrderProcessed(Guid OrderId, DateTime ProcessedAt);
