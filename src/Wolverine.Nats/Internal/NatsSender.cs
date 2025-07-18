using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Net;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace Wolverine.Nats.Internal;

public class NatsSender : ISender
{
    private readonly NatsEndpoint _endpoint;
    private readonly NatsConnection _connection;
    private readonly ILogger<NatsEndpoint> _logger;
    private readonly NatsEnvelopeMapper _mapper;
    private readonly CancellationToken _cancellation;

    public NatsSender(
        NatsEndpoint endpoint,
        NatsConnection connection,
        ILogger<NatsEndpoint> logger,
        NatsEnvelopeMapper mapper,
        CancellationToken cancellation
    )
    {
        _endpoint = endpoint;
        _connection = connection;
        _logger = logger;
        _mapper = mapper;
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
            await _connection.PublishAsync(
                pingSubject,
                Array.Empty<byte>(),
                cancellationToken: _cancellation
            );
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
            var headers = new NatsHeaders();
            _mapper.MapEnvelopeToOutgoing(envelope, headers);

            // Add custom headers specific to this endpoint
            foreach (var header in _endpoint.CustomHeaders)
            {
                headers[header.Key] = header.Value;
            }

            var data = envelope.Data ?? Array.Empty<byte>();

            // Determine the target subject and reply-to
            var targetSubject = _endpoint.Subject;
            string? replyTo = null;

            // Handle special case for reply messages
            if (envelope.IsResponse && envelope.ReplyUri != null)
            {
                // This is a reply message, send directly to the reply subject
                targetSubject = NatsTransport.ExtractSubjectFromUri(envelope.ReplyUri);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Sending reply message {MessageId} to reply subject {ReplySubject}",
                        envelope.Id,
                        targetSubject
                    );
                }
            }
            else
            {
                // This is a request message, set reply-to if available
                if (envelope.ReplyUri != null)
                {
                    replyTo = NatsTransport.ExtractSubjectFromUri(envelope.ReplyUri);

                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Sending request message {MessageId} to NATS subject {Subject} with reply-to {ReplyTo}",
                            envelope.Id,
                            targetSubject,
                            replyTo
                        );
                    }
                }
                else
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Sending message {MessageId} to NATS subject {Subject}",
                            envelope.Id,
                            targetSubject
                        );
                    }
                }
            }

            if (
                _endpoint.UseJetStream
                && !string.IsNullOrEmpty(_endpoint.StreamName)
                && !envelope.IsResponse
            )
            {
                // Use JetStream for publishing (but not for replies)
                var js = _connection.CreateJetStreamContext();
                var ack = await js.PublishAsync(
                    targetSubject,
                    data,
                    headers: headers,
                    cancellationToken: _cancellation
                );

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Message {MessageId} published to JetStream with sequence {Sequence}",
                        envelope.Id,
                        ack.Seq
                    );
                }
            }
            else
            {
                // Use Core NATS for publishing (replies should always use Core NATS)
                await _connection.PublishAsync(
                    targetSubject,
                    data,
                    headers,
                    replyTo,
                    cancellationToken: _cancellation
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send message {MessageId} to NATS subject {Subject}",
                envelope.Id,
                _endpoint.Subject
            );
            throw;
        }
    }
}
