using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports;
using Wolverine.Transports.Sending;

namespace Wolverine.Nats.Internal;

public class NatsEndpoint : Endpoint, IBrokerEndpoint
{
    private readonly NatsTransport _transport;
    private NatsConnection? _connection;
    private ILogger<NatsEndpoint>? _logger;
    private NatsEnvelopeMapper? _mapper;

    public NatsEndpoint(string subject, NatsTransport transport, EndpointRole role)
        : base(new Uri($"nats://subject/{subject}"), role)
    {
        Subject = subject;
        _transport = transport;

        EndpointName = subject;
        Mode = EndpointMode.BufferedInMemory;
    }

    public string Subject { get; }

    /// <summary>
    /// NATS specific serializer configuration
    /// </summary>
    public object? NatsSerializer { get; set; }

    /// <summary>
    /// NATS specific headers to include in messages
    /// </summary>
    public Dictionary<string, string> CustomHeaders { get; set; } = new();

    /// <summary>
    /// Queue group for load balancing (listeners only)
    /// </summary>
    public string? QueueGroup { get; set; }

    /// <summary>
    /// Stream name for JetStream (if using JetStream)
    /// </summary>
    public string? StreamName { get; set; }

    /// <summary>
    /// Consumer name for JetStream (if using JetStream)
    /// </summary>
    public string? ConsumerName { get; set; }

    /// <summary>
    /// Whether to use JetStream for this endpoint
    /// </summary>
    public bool UseJetStream { get; set; }

    /// <summary>
    /// Enable native dead letter queue handling
    /// </summary>
    public bool DeadLetterQueueEnabled { get; set; } = true;

    /// <summary>
    /// Optional subject to publish dead letter messages to
    /// </summary>
    public string? DeadLetterSubject { get; set; }

    /// <summary>
    /// Maximum delivery attempts before moving to DLQ
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    protected override bool supportsMode(EndpointMode mode)
    {
        return mode switch
        {
            EndpointMode.Inline => true,
            EndpointMode.BufferedInMemory => true,
            EndpointMode.Durable => UseJetStream, // Only support durable mode with JetStream
            _ => false
        };
    }

    protected override ISender CreateSender(IWolverineRuntime runtime)
    {
        _connection = _transport.Connection;
        _logger = runtime.LoggerFactory.CreateLogger<NatsEndpoint>();
        _mapper = new NatsEnvelopeMapper(this);
        
        // Configure the mapper with MessageType if set
        if (MessageType != null)
        {
            _mapper.ReceivesMessage(MessageType);
        }

        return new NatsSender(this, _connection, _logger, _mapper, runtime.Cancellation);
    }

    public override async ValueTask<IListener> BuildListenerAsync(
        IWolverineRuntime runtime,
        IReceiver receiver
    )
    {
        _connection = _transport.Connection;
        _logger = runtime.LoggerFactory.CreateLogger<NatsEndpoint>();

        var listener = new NatsListener(
            this,
            _connection,
            runtime,
            receiver,
            _logger,
            runtime.Cancellation
        );

        await listener.StartAsync();

        return listener;
    }

    public NatsHeaders BuildHeaders(Envelope envelope)
    {
        var headers = new NatsHeaders();
        _mapper?.MapEnvelopeToOutgoing(envelope, headers);

        // Add custom headers specific to this endpoint
        foreach (var header in CustomHeaders)
        {
            headers[header.Key] = header.Value;
        }

        return headers;
    }

    public async ValueTask<bool> CheckAsync()
    {
        // Ensure we have a connection
        _connection ??= _transport.Connection;

        if (_connection == null || _connection.ConnectionState != NatsConnectionState.Open)
        {
            return false;
        }

        // For Core NATS, connection check is enough
        if (!UseJetStream || string.IsNullOrEmpty(StreamName))
        {
            return true;
        }

        // For JetStream, check if the stream exists
        try
        {
            var js = _connection.CreateJetStreamContext();
            var stream = await js.GetStreamAsync(
                StreamName,
                cancellationToken: CancellationToken.None
            );
            return stream != null;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask TeardownAsync(ILogger logger)
    {
        // Ensure we have a connection
        _connection ??= _transport.Connection;

        if (_connection == null || _connection.ConnectionState != NatsConnectionState.Open)
        {
            return;
        }

        // For Core NATS, nothing to tear down
        if (!UseJetStream || string.IsNullOrEmpty(StreamName))
        {
            return;
        }

        // For JetStream, we might want to delete consumers (but not streams)
        if (!string.IsNullOrEmpty(ConsumerName))
        {
            try
            {
                var js = _connection.CreateJetStreamContext();
                await js.DeleteConsumerAsync(StreamName, ConsumerName);
                logger.LogInformation(
                    "Deleted consumer {Consumer} from stream {Stream}",
                    ConsumerName,
                    StreamName
                );
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to delete consumer {Consumer} from stream {Stream}",
                    ConsumerName,
                    StreamName
                );
            }
        }
    }

    public async ValueTask SetupAsync(ILogger logger)
    {
        // Ensure we have a connection
        _connection ??= _transport.Connection;

        if (_connection == null || _connection.ConnectionState != NatsConnectionState.Open)
        {
            throw new InvalidOperationException("NATS connection is not available or not open");
        }

        // For Core NATS, nothing to set up
        if (!UseJetStream || string.IsNullOrEmpty(StreamName))
        {
            logger.LogInformation("Using Core NATS for subject {Subject}", Subject);
            return;
        }

        // For JetStream, ensure the stream exists
        var js = _connection.CreateJetStreamContext();

        try
        {
            // Try to get the stream first
            var stream = await js.GetStreamAsync(StreamName);
            logger.LogInformation("Using existing JetStream stream {Stream}", StreamName);
        }
        catch
        {
            // Stream doesn't exist, create it
            logger.LogInformation(
                "Creating JetStream stream {Stream} for subjects {Subjects}",
                StreamName,
                Subject
            );

            var config = new StreamConfig(StreamName, new[] { Subject })
            {
                Retention = StreamConfigRetention.Workqueue, // Delete after ack
                Discard = StreamConfigDiscard.Old,
                MaxAge = TimeSpan.FromDays(1),
                DuplicateWindow = TimeSpan.FromMinutes(2),
                MaxMsgs = 1_000_000
            };

            await js.CreateStreamAsync(config);
            logger.LogInformation("Created JetStream stream {Stream}", StreamName);
        }

        // Set up consumer if needed
        if (!string.IsNullOrEmpty(ConsumerName) && Role == EndpointRole.Application)
        {
            var consumerConfig = new ConsumerConfig
            {
                Name = ConsumerName,
                DurableName = ConsumerName,
                FilterSubject = Subject,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                AckWait = TimeSpan.FromSeconds(30),
                MaxDeliver = MaxDeliveryAttempts,
                ReplayPolicy = ConsumerConfigReplayPolicy.Instant
            };

            await js.CreateOrUpdateConsumerAsync(StreamName, consumerConfig);
            logger.LogInformation(
                "Created/updated consumer {Consumer} on stream {Stream}",
                ConsumerName,
                StreamName
            );
        }
    }
}
