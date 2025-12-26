using Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.Nats;

var builder = Host.CreateApplicationBuilder(args);

await builder
    .UseWolverine(opts =>
    {
        opts.ApplicationAssembly = typeof(Program).Assembly;

        opts.UseNats(builder.Configuration);

        opts.ListenToNatsSubject("pings");
        opts.PublishMessage<Pong>().ToNatsSubject("pongs");

        opts.Services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });
    })
    .Build()
    .RunAsync();
