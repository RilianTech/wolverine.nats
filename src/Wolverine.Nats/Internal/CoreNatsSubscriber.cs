using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using Wolverine.Transports;

namespace Wolverine.Nats.Internal;

/// <summary>
/// Core NATS subscriber for at-most-once message delivery
/// </summary>
internal class CoreNatsSubscriber : INatsSubscriber
{
    private readonly NatsEndpoint _endpoint;
    private readonly NatsConnection _connection;
    private readonly ILogger<NatsEndpoint> _logger;
    private readonly NatsEnvelopeMapper _mapper;
    private IAsyncDisposable? _subscription;
    private Task? _consumerTask;

    public CoreNatsSubscriber(
        NatsEndpoint endpoint,
        NatsConnection connection,
        ILogger<NatsEndpoint> logger,
        NatsEnvelopeMapper mapper
    )
    {
        _endpoint = endpoint;
        _connection = connection;
        _logger = logger;
        _mapper = mapper;
    }

    public bool SupportsNativeDeadLetterQueue => false;

    public async Task StartAsync(
        IListener listener,
        IReceiver receiver,
        CancellationToken cancellation
    )
    {
        _logger.LogInformation(
            "Starting Core NATS listener for subject {Subject} with queue group {QueueGroup}",
            _endpoint.Subject,
            _endpoint.QueueGroup ?? "(none)"
        );

        if (!string.IsNullOrEmpty(_endpoint.QueueGroup))
        {
            _subscription = await _connection.SubscribeCoreAsync<byte[]>(
                _endpoint.Subject,
                _endpoint.QueueGroup,
                cancellationToken: cancellation
            );
        }
        else
        {
            _subscription = await _connection.SubscribeCoreAsync<byte[]>(
                _endpoint.Subject,
                cancellationToken: cancellation
            );
        }

        _consumerTask = Task.Run(
            async () =>
            {
                try
                {
                    await foreach (
                        var msg in ((INatsSub<byte[]>)_subscription).Msgs.ReadAllAsync(cancellation)
                    )
                    {
                        try
                        {
                            var envelope = new NatsEnvelope(msg, null);
                            _mapper.MapIncomingToEnvelope(envelope, msg);

                            await receiver.ReceivedAsync(listener, envelope);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Error processing NATS message from subject {Subject}",
                                msg.Subject
                            );
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                    _logger.LogDebug(
                        "NATS listener for {Subject} was cancelled",
                        _endpoint.Subject
                    );
                }
            },
            cancellation
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription != null)
        {
            await _subscription.DisposeAsync();
        }

        if (_consumerTask != null)
        {
            await _consumerTask;
        }
    }
}
