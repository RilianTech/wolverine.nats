using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using JasperFx.Core;
using Wolverine;
using Wolverine.Nats.Internal;
using Wolverine.Runtime;
using Wolverine.Tracking;
using Xunit;
using FluentAssertions;

namespace Wolverine.Nats.Tests;

[Collection("NATS Integration Tests")]
[Trait("Category", "Integration")]
public class NatsTransportIntegrationTests : IAsyncLifetime
{
    private IHost? _sender;
    private IHost? _receiver;
    private static int _counter = 0;
    private string _receiverSubject = "";

    public async Task InitializeAsync()
    {
        // In CI, NATS_URL is set to use port 4222
        // Locally, we'll try 4223 first (docker-compose), then 4222 (if running locally)
        var natsUrl = Environment.GetEnvironmentVariable("NATS_URL");

        if (string.IsNullOrEmpty(natsUrl))
        {
            // Try docker-compose port first
            if (await IsNatsAvailable("nats://localhost:4223"))
            {
                natsUrl = "nats://localhost:4223";
            }
            else if (await IsNatsAvailable("nats://localhost:4222"))
            {
                natsUrl = "nats://localhost:4222";
            }
            else
            {
                // NATS not available
                return;
            }
        }

        // Double-check the URL we got works
        if (!await IsNatsAvailable(natsUrl))
        {
            return;
        }

        var number = ++_counter;
        _receiverSubject = $"test.receiver.{number}";

        _sender = await Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.ServiceName = "Sender";
                opts.UseNats(natsUrl).AutoProvision();
                opts.PublishAllMessages().ToNatsSubject(_receiverSubject);
            })
            .StartAsync();

        _receiver = await Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.ServiceName = "Receiver";
                opts.UseNats(natsUrl).AutoProvision();
                opts.ListenToNatsSubject(_receiverSubject).Named("receiver");
            })
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_sender != null)
            await _sender.StopAsync();
        if (_receiver != null)
            await _receiver.StopAsync();
        _sender?.Dispose();
        _receiver?.Dispose();
    }

    [Fact]
    public async Task send_message_to_and_receive_through_nats()
    {
        if (_sender == null || _receiver == null)
            return; // Skip if NATS not available

        // Arrange
        var message = new TestMessage(Guid.NewGuid(), "Hello NATS!");

        // Act
        var tracked = await _sender
            .TrackActivity()
            .AlsoTrack(_receiver)
            .SendMessageAndWaitAsync(message);

        // Assert
        tracked.Sent.SingleMessage<TestMessage>().Should().BeEquivalentTo(message);

        tracked.Received.SingleMessage<TestMessage>().Should().BeEquivalentTo(message);
    }

    [Fact]
    public async Task can_send_and_receive_multiple_messages()
    {
        if (_sender == null || _receiver == null)
            return;

        // Arrange
        var messages = new[]
        {
            new TestMessage(Guid.NewGuid(), "Message 1"),
            new TestMessage(Guid.NewGuid(), "Message 2"),
            new TestMessage(Guid.NewGuid(), "Message 3")
        };

        // Act & Assert
        foreach (var message in messages)
        {
            var tracked = await _sender
                .TrackActivity()
                .AlsoTrack(_receiver)
                .SendMessageAndWaitAsync(message);

            tracked.Sent.SingleMessage<TestMessage>().Should().BeEquivalentTo(message);

            tracked.Received.SingleMessage<TestMessage>().Should().BeEquivalentTo(message);
        }
    }

    [Fact]
    public void nats_transport_is_registered()
    {
        if (_sender == null)
            return;

        var runtime = _sender.Services.GetRequiredService<IWolverineRuntime>();
        var transport = runtime.Options.Transports.GetOrCreate<NatsTransport>();

        transport.Should().NotBeNull();
        transport.Protocol.Should().Be("nats");
    }

    [Fact]
    public void endpoints_are_configured_correctly()
    {
        if (_receiver == null)
            return;

        var runtime = _receiver.Services.GetRequiredService<IWolverineRuntime>();
        var transport = runtime.Options.Transports.GetOrCreate<NatsTransport>();

        var endpointUri = new Uri($"nats://subject/{_receiverSubject}");
        var endpoint = transport.TryGetEndpoint(endpointUri);

        endpoint.Should().NotBeNull();
        endpoint.Should().BeOfType<NatsEndpoint>();

        var natsEndpoint = (NatsEndpoint)endpoint!;
        natsEndpoint.Subject.Should().Be(_receiverSubject);
        natsEndpoint.EndpointName.Should().Be("receiver");
    }

    [Fact]
    public async Task scheduled_send_uses_wolverine_envelope_wrapper()
    {
        if (_sender == null || _receiver == null) return;
        
        // NATS doesn't support native scheduled send
        // Wolverine will handle it internally by wrapping in a scheduled-envelope
        var message = new TestMessage(Guid.NewGuid(), "Scheduled Message");
        
        // Send with a delay
        var result = await _sender
            .TrackActivity()
            .AlsoTrack(_receiver)
            .SendMessageAndWaitAsync(message, new DeliveryOptions { ScheduleDelay = 1.Seconds() });
        
        // When using scheduled send with a transport that doesn't support it natively,
        // Wolverine sends a "scheduled-envelope" wrapper message
        var sentEnvelopes = result.Sent.Envelopes().ToList();
        sentEnvelopes.Should().HaveCount(1);
        sentEnvelopes.First().MessageType.Should().Be("scheduled-envelope");
        
        // The scheduled envelope contains our actual message which will be 
        // delivered after the delay by Wolverine's internal scheduling
    }

    private async Task<bool> IsNatsAvailable(string natsUrl)
    {
        try
        {
            using var testHost = await Host.CreateDefaultBuilder()
                .UseWolverine(opts =>
                {
                    opts.UseNats(natsUrl);
                })
                .StartAsync();

            await testHost.StopAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public record TestMessage(Guid Id, string Text);

public class TestMessageHandler
{
    public void Handle(TestMessage message)
    {
        // Message is handled
    }
}
