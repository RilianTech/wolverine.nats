using Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pinger;
using Wolverine;
using Wolverine.Nats;

await Host.CreateDefaultBuilder(args)
    .UseWolverine(opts =>
    {
        opts.ApplicationAssembly = typeof(Program).Assembly;

        // Configure NATS transport
        opts.UseNats("nats://localhost:4223");

        // Listen for Pong messages coming back
        opts.ListenToNatsSubject("pongs");

        // Publish Ping messages to the pings subject
        opts.PublishMessage<Ping>().ToNatsSubject("pings");

        // TODO: Add JetStream support
        // For now, using Core NATS for at-most-once delivery

        // Register the pinger service that sends messages continuously
        opts.Services.AddHostedService<PingerService>();
    })
    .Build()
    .RunAsync();
