using OrderService;
using OrderService.Data;
using Shared.Events;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Nats;
using Wolverine.Nats.Internal;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add in-memory order repository (in real world, this would be a database)
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

// Configure Wolverine with NATS JetStream
builder.Host.UseWolverine(opts =>
{
    // Configure NATS transport using configuration
    var natsTransport = opts.UseNats(builder.Configuration)
        // Define the ORDERS stream with all subjects it will handle
        .DefineStream(
            "ORDERS",
            stream =>
            {
                stream
                    .WithSubjects(
                        "orders.>", // All order-related subjects
                        "payment.>", // Payment events
                        "inventory.>" // Inventory events
                    )
                    .WithLimits(
                        maxMessages: 1_000_000, // 1M messages max
                        maxBytes: 1024L * 1024 * 1024, // 1GB storage
                        maxAge: TimeSpan.FromDays(30) // 30 days retention
                    )
                    .WithReplicas(1); // Single replica for development
            }
        )
        .ConfigureListeners(listener =>
        {
            // Default all listeners to use JetStream with ORDERS stream
            listener
                .UseJetStream("ORDERS", "order-service")
                .UseQueueGroup("order-service")
                .ConfigureDeadLetterQueue(3);
        })
        .ConfigureSenders(sender =>
        {
            // Default all senders to use JetStream with ORDERS stream
            sender.UseJetStream("ORDERS");
        });

    // Publish events to specific subjects based on event type
    // Note: These will automatically use JetStream with ORDERS stream from our default configuration
    opts.PublishMessage<OrderCreated>().ToNatsSubject("orders.created");

    opts.PublishMessage<OrderCancelled>().ToNatsSubject("orders.cancelled");

    opts.PublishMessage<InventoryReserved>().ToNatsSubject("orders.inventory.reserved");

    opts.PublishMessage<InventoryReservationFailed>().ToNatsSubject("orders.inventory.failed");

    opts.PublishMessage<PaymentRequested>().ToNatsSubject("orders.payment.requested");

    opts.PublishMessage<OrderStatusChanged>().ToNatsSubject("orders.status.changed");

    // Listen for events that affect order status
    // Note: These will automatically use JetStream, ORDERS stream, order-service consumer, and queue group
    opts.ListenToNatsSubject("orders.inventory.>");

    opts.ListenToNatsSubject("orders.payment.>");

    opts.ListenToNatsSubject("orders.shipping.>");

    // Configure error handling
    opts.OnException<InvalidOperationException>()
        .MoveToErrorQueue()
        .AndPauseProcessing(TimeSpan.FromSeconds(30));

    // Enable console logging
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
