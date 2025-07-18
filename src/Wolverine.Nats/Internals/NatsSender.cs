using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Net;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace Wolverine.Nats.Internals;

public class NatsSender : ISender
{
    private readonly NatsEndpoint _endpoint;
    private readonly NatsConnection _connection;
    private readonly ILogger<NatsEndpoint> _logger;
    private readonly CancellationToken _cancellation;

    public NatsSender(NatsEndpoint endpoint, NatsConnection connection, ILogger<NatsEndpoint> logger, CancellationToken cancellation)
    {
        _endpoint = endpoint;
        _connection = connection;
        _logger = logger;
        _cancellation = cancellation;
        Destination = endpoint.Uri;
    }

    public bool SupportsNativeScheduledSend => false;
    public Uri Destination { get; }

    public async Task<bool> PingAsync()
    {
        try
        {
            // Try to publish a ping message and wait for connection confirmation
            var pingSubject = $"_INBOX.wolverine.ping.{Guid.NewGuid():N}";
            await _connection.PublishAsync(pingSubject, Array.Empty<byte>(), cancellationToken: _cancellation);
            return _connection.ConnectionState == NatsConnectionState.Open;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ping NATS endpoint {Subject}", _endpoint.Subject);
            return false;
        }
    }

    public async ValueTask SendAsync(Envelope envelope)
    {
        try
        {
            var headers = _endpoint.BuildHeaders(envelope);
            var data = envelope.Data ?? Array.Empty<byte>();

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Sending message {MessageId} to NATS subject {Subject}", 
                    envelope.Id, _endpoint.Subject);
            }

            if (_endpoint.UseJetStream && !string.IsNullOrEmpty(_endpoint.StreamName))
            {
                // Use JetStream for publishing
                var js = _connection.CreateJetStreamContext();
                var ack = await js.PublishAsync(_endpoint.Subject, data, headers: headers, cancellationToken: _cancellation);
                
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Message {MessageId} published to JetStream with sequence {Sequence}", 
                        envelope.Id, ack.Seq);
                }
            }
            else
            {
                // Use Core NATS for publishing
                await _connection.PublishAsync(_endpoint.Subject, data, headers: headers, cancellationToken: _cancellation);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message {MessageId} to NATS subject {Subject}", 
                envelope.Id, _endpoint.Subject);
            throw;
        }
    }
}