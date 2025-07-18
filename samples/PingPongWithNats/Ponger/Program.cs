using Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.Nats;

await Host.CreateDefaultBuilder(args)
    .UseWolverine(opts =>
    {
        opts.ApplicationAssembly = typeof(Program).Assembly;

        // Configure NATS transport
        opts.UseNats("nats://localhost:4223");

        // Listen to ping messages
        opts.ListenToNatsSubject("pings");

        // Configure where to send Pong messages
        opts.PublishMessage<Pong>().ToNatsSubject("pongs");

        // TODO: Add JetStream support
        // For now, using Core NATS for at-most-once delivery

        // Enable console logging to see what's happening
        opts.Services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });
    })
    .Build()
    .RunAsync();
