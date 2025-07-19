using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;
using Wolverine.Transports;
using Wolverine.Transports.Sending;
using Wolverine.Util.Dataflow;

namespace Wolverine.Nats.Internal;

public class NatsListener : IListener, ISupportDeadLetterQueue
{
    private readonly NatsEndpoint _endpoint;
    private readonly NatsConnection _connection;
    private readonly IWolverineRuntime _runtime;
    private readonly IReceiver _receiver;
    private readonly ILogger<NatsEndpoint> _logger;
    private readonly CancellationTokenSource _cancellation;
    private readonly RetryBlock<NatsEnvelope> _complete;
    private readonly RetryBlock<NatsEnvelope> _defer;
    private readonly NatsEnvelopeMapper _mapper;
    private readonly JetStreamEnvelopeMapper _jsMapper;
    
    public IHandlerPipeline? Pipeline { get; private set; }

    private IAsyncDisposable? _subscription;
    private Task? _consumerTask;

    public NatsListener(
        NatsEndpoint endpoint,
        NatsConnection connection,
        IWolverineRuntime runtime,
        IReceiver receiver,
        ILogger<NatsEndpoint> logger,
        CancellationToken parentCancellation
    )
    {
        _endpoint = endpoint;
        _connection = connection;
        _runtime = runtime;
        _receiver = receiver;
        _logger = logger;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(parentCancellation);
        _mapper = new NatsEnvelopeMapper(endpoint);
        _jsMapper = new JetStreamEnvelopeMapper(endpoint);
        
        // Configure mappers with MessageType if set
        if (endpoint.MessageType != null)
        {
            _mapper.ReceivesMessage(endpoint.MessageType);
            _jsMapper.ReceivesMessage(endpoint.MessageType);
        }

        Address = endpoint.Uri;

        _complete = new RetryBlock<NatsEnvelope>(
            async (envelope, _) =>
            {
                // For JetStream messages, acknowledge them
                if (envelope.JetStreamMsg != null)
                {
                    await envelope.JetStreamMsg.Value.AckAsync(
                        cancellationToken: _cancellation.Token
                    );
                }
            },
            logger,
            _cancellation.Token
        );

        _defer = new RetryBlock<NatsEnvelope>(
            async (envelope, _) =>
            {
                // For JetStream messages, NAck to trigger redelivery
                if (envelope.JetStreamMsg != null)
                {
                    await envelope.JetStreamMsg.Value.NakAsync(
                        cancellationToken: _cancellation.Token
                    );
                }

                // For core NATS, we'll need to manually retry
                await Task.Delay(TimeSpan.FromSeconds(5), _cancellation.Token);
                await _receiver.ReceivedAsync(this, envelope);
            },
            logger,
            _cancellation.Token
        );
    }

    public Uri Address { get; }

    // ISupportDeadLetterQueue implementation
    public bool NativeDeadLetterQueueEnabled =>
        _endpoint.UseJetStream && _endpoint.DeadLetterQueueEnabled;

    public async Task MoveToErrorsAsync(Envelope envelope, Exception exception)
    {
        if (envelope is NatsEnvelope natsEnvelope)
        {
            // For JetStream messages, use AckTerm to terminate delivery
            // This will trigger the advisory message and prevent further redeliveries
            if (natsEnvelope.JetStreamMsg != null)
            {
                await natsEnvelope.JetStreamMsg.Value.AckTerminateAsync(
                    cancellationToken: _cancellation.Token
                );
                _logger.LogInformation(
                    "Terminated message delivery for {MessageId} after {Attempts} attempts",
                    envelope.Id,
                    envelope.Attempts
                );
            }

            // If configured, also publish the message to a separate dead letter subject
            if (!string.IsNullOrEmpty(_endpoint.DeadLetterSubject))
            {
                try
                {
                    // Add DLQ metadata to headers
                    var headers = _endpoint.BuildHeaders(envelope);
                    headers["x-dlq-reason"] = exception.Message;
                    headers["x-dlq-timestamp"] = DateTimeOffset.UtcNow.ToString("O");
                    headers["x-dlq-original-subject"] = _endpoint.Subject;
                    headers["x-dlq-attempts"] = envelope.Attempts.ToString();
                    headers["x-dlq-exception-type"] = exception.GetType().FullName ?? "Unknown";

                    await _connection.PublishAsync(
                        _endpoint.DeadLetterSubject,
                        envelope.Data,
                        headers,
                        cancellationToken: _cancellation.Token
                    );

                    _logger.LogInformation(
                        "Published dead letter message {MessageId} to {Subject}",
                        envelope.Id,
                        _endpoint.DeadLetterSubject
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to publish dead letter message {MessageId} to {Subject}",
                        envelope.Id,
                        _endpoint.DeadLetterSubject
                    );
                }
            }
        }
    }

    public async ValueTask CompleteAsync(Envelope envelope)
    {
        if (envelope is NatsEnvelope natsEnvelope)
        {
            await _complete.PostAsync(natsEnvelope);
        }
    }

    public async ValueTask DeferAsync(Envelope envelope)
    {
        if (envelope is NatsEnvelope natsEnvelope)
        {
            await _defer.PostAsync(natsEnvelope);
        }
    }

    public async Task StartAsync()
    {
        if (_endpoint.UseJetStream && !string.IsNullOrEmpty(_endpoint.StreamName))
        {
            await StartJetStreamConsumerAsync();
        }
        else
        {
            await StartCoreNatsSubscriptionAsync();
        }
    }

    private async Task StartCoreNatsSubscriptionAsync()
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
                cancellationToken: _cancellation.Token
            );
        }
        else
        {
            _subscription = await _connection.SubscribeCoreAsync<byte[]>(
                _endpoint.Subject,
                cancellationToken: _cancellation.Token
            );
        }

        _consumerTask = Task.Run(
            async () =>
            {
                try
                {
                    await foreach (
                        var msg in ((INatsSub<byte[]>)_subscription).Msgs.ReadAllAsync(
                            _cancellation.Token
                        )
                    )
                    {
                        try
                        {
                            var envelope = new NatsEnvelope(msg, null);
                            _mapper.MapIncomingToEnvelope(envelope, msg);

                            await _receiver.ReceivedAsync(this, envelope);
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
                    _logger.LogDebug("NATS listener for {Subject} was cancelled", _endpoint.Subject);
                }
            },
            _cancellation.Token
        );
    }

    private async Task StartJetStreamConsumerAsync()
    {
        _logger.LogInformation(
            "Starting JetStream listener for stream {Stream}, consumer {Consumer}, subject {Subject}",
            _endpoint.StreamName,
            _endpoint.ConsumerName ?? "(ephemeral)",
            _endpoint.Subject
        );

        var js = _connection.CreateJetStreamContext();

        // Create or get consumer
        INatsJSConsumer consumer;
        var config = new ConsumerConfig
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            MaxDeliver = _endpoint.MaxDeliveryAttempts,
            AckWait = TimeSpan.FromSeconds(30)
        };
        
        // Only set filter subject if not using a durable consumer
        // Durable consumers typically listen to multiple subjects
        if (string.IsNullOrEmpty(_endpoint.ConsumerName))
        {
            config.FilterSubject = _endpoint.Subject;
        }
        
        if (!string.IsNullOrEmpty(_endpoint.ConsumerName))
        {
            // Create or update durable consumer
            config.Name = _endpoint.ConsumerName;
            config.DurableName = _endpoint.ConsumerName;
            
            // Add queue group if specified
            if (!string.IsNullOrEmpty(_endpoint.QueueGroup))
            {
                config.DeliverGroup = _endpoint.QueueGroup;
            }
            
            // Try to get existing consumer first
            try
            {
                consumer = await js.GetConsumerAsync(
                    _endpoint.StreamName!,
                    _endpoint.ConsumerName,
                    _cancellation.Token
                );
                _logger.LogInformation("Using existing consumer {Consumer}", _endpoint.ConsumerName);
            }
            catch (NatsJSException)
            {
                // Consumer doesn't exist, create it
                consumer = await js.CreateOrUpdateConsumerAsync(
                    _endpoint.StreamName!,
                    config,
                    _cancellation.Token
                );
                _logger.LogInformation("Created new consumer {Consumer}", _endpoint.ConsumerName);
            }
        }
        else
        {
            // Create ephemeral consumer
            consumer = await js.CreateOrUpdateConsumerAsync(
                _endpoint.StreamName!,
                config,
                _cancellation.Token
            );
        }

        _consumerTask = Task.Run(
            async () =>
            {
                try
                {
                    await foreach (
                        var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: _cancellation.Token)
                    )
                    {
                        try
                        {
                            var envelope = new NatsEnvelope(null, msg);
                            _jsMapper.MapIncomingToEnvelope(envelope, msg);
                            envelope.Attempts = (int)(msg.Metadata?.NumDelivered ?? 0);

                            await _receiver.ReceivedAsync(this, envelope);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Error processing JetStream message from subject {Subject}",
                                msg.Subject
                            );

                            // NAck the message to trigger redelivery
                            await msg.NakAsync(cancellationToken: _cancellation.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                    _logger.LogDebug("JetStream consumer for {Stream}/{Consumer} was cancelled", 
                        _endpoint.StreamName, _endpoint.ConsumerName ?? "ephemeral");
                }
            },
            _cancellation.Token
        );
    }

    public ValueTask StopAsync()
    {
        _cancellation.Cancel();
        return new ValueTask();
    }

    public void Dispose()
    {
        _cancellation.Cancel();

        _subscription?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _consumerTask?.GetAwaiter().GetResult();

        _complete.Dispose();
        _defer.Dispose();
        _cancellation.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        _cancellation.Cancel();

        return new ValueTask(
            Task.WhenAll(
                _subscription?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                _consumerTask ?? Task.CompletedTask,
                Task.Run(() =>
                {
                    _complete.Dispose();
                    _defer.Dispose();
                    _cancellation.Dispose();
                })
            )
        );
    }
}
