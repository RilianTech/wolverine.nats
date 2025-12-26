using Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pinger;
using Wolverine;
using Wolverine.Nats;

var builder = Host.CreateApplicationBuilder(args);

await builder
    .UseWolverine(opts =>
    {
        opts.ApplicationAssembly = typeof(Program).Assembly;

        opts.UseNats(builder.Configuration);

        opts.ListenToNatsSubject("pongs");

        opts.PublishMessage<Ping>().ToNatsSubject("pings");

        // Register the pinger service
        opts.Services.AddHostedService<PingerService>();
    })
    .Build()
    .RunAsync();
