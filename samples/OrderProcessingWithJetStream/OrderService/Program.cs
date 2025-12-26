using OrderService;
using OrderService.Data;
using Shared.Events;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Nats;
using Wolverine.Nats.Internal;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

builder.Host.UseWolverine(opts =>
{
    var natsTransport = opts.UseNats(builder.Configuration)
        .DefineStream(
            "ORDERS",
            stream =>
            {
                stream
                    .WithSubjects("orders.>", "payment.>", "inventory.>")
                    .WithLimits(
                        maxMessages: 1_000_000,
                        maxBytes: 1024L * 1024 * 1024,
                        maxAge: TimeSpan.FromDays(30)
                    )
                    .WithReplicas(1);
            }
        )
        .ConfigureListeners(listener =>
        {
            listener
                .UseJetStream("ORDERS", "order-service")
                .UseQueueGroup("order-service")
                .ConfigureDeadLetterQueue(3);
        })
        .ConfigureSenders(sender =>
        {
            sender.UseJetStream("ORDERS");
        });

    opts.PublishMessage<OrderCreated>().ToNatsSubject("orders.created");
    opts.PublishMessage<OrderCancelled>().ToNatsSubject("orders.cancelled");
    opts.PublishMessage<InventoryReserved>().ToNatsSubject("orders.inventory.reserved");
    opts.PublishMessage<InventoryReservationFailed>().ToNatsSubject("orders.inventory.failed");
    opts.PublishMessage<PaymentRequested>().ToNatsSubject("orders.payment.requested");
    opts.PublishMessage<OrderStatusChanged>().ToNatsSubject("orders.status.changed");

    opts.ListenToNatsSubject("orders.inventory.>");
    opts.ListenToNatsSubject("orders.payment.>");
    opts.ListenToNatsSubject("orders.shipping.>");

    opts.OnException<InvalidOperationException>()
        .MoveToErrorQueue()
        .AndPauseProcessing(TimeSpan.FromSeconds(30));

    opts.Services.AddLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Debug);
        logging.AddConsole();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();
