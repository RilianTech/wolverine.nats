using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Net;
using Wolverine;
using Wolverine.Tracking;
using Xunit;
using Xunit.Abstractions;

namespace Wolverine.Nats.Tests;

[Collection("NATS Integration Tests")]
public class RequestReplyTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private IHost? _host;

    public RequestReplyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // Skip tests if NATS server is not available
        if (!await IsNatsServerAvailable())
        {
            _output.WriteLine("NATS server not available at localhost:4222. Skipping integration tests.");
            return;
        }

        _host = await Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.ServiceName = "RequestReplyTest";
                opts.UseNats("nats://localhost:4222");

                // Configure publishing
                opts.PublishMessage<PingMessage>().ToNatsSubject("ping.request");

                // Configure listening
                opts.ListenToNatsSubject("ping.request");
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .StartAsync();
    }

    private async Task<bool> IsNatsServerAvailable()
    {
        try
        {
            await using var connection = new NatsConnection(NatsOpts.Default with { Url = "nats://localhost:4222" });
            await connection.ConnectAsync();
            return connection.ConnectionState == NatsConnectionState.Open;
        }
        catch
        {
            return false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [Fact]
    public async Task can_send_request_and_receive_reply()
    {
        // Skip if NATS server not available or host not initialized
        if (_host == null)
        {
            _output.WriteLine("Skipping test - NATS server not available");
            return;
        }

        var (session, response) = await _host
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .InvokeAndWaitAsync<PongMessage>(new PingMessage { Name = "TestPing" });

        Assert.NotNull(response);
        Assert.Equal("Hello TestPing", response.Message);
    }

    [Fact]
    public async Task can_handle_request_timeout()
    {
        // Skip if NATS server not available or host not initialized
        if (_host == null)
        {
            _output.WriteLine("Skipping test - NATS server not available");
            return;
        }

        // Send request to a subject with no handler
        var bus = _host.Services.GetRequiredService<IMessageBus>();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await bus.EndpointFor("nats://nonexistent.subject")
                .InvokeAsync<PongMessage>(
                    new PingMessage { Name = "TimedOut" },
                    timeout: TimeSpan.FromMilliseconds(100)
                );
        });
    }
}

public class PingMessage
{
    public string Name { get; set; } = "";
}

public class PongMessage
{
    public string Message { get; set; } = "";
}

public class PingHandler
{
    public PongMessage Handle(PingMessage ping)
    {
        return new PongMessage { Message = $"Hello {ping.Name}" };
    }
}
