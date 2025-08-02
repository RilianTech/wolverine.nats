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
        var natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
        opts.UseNats(natsUrl);

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
