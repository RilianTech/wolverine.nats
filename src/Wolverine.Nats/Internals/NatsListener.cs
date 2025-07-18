using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using Wolverine.Runtime;
using Wolverine.Transports;
using Wolverine.Util.Dataflow;

namespace Wolverine.Nats.Internals;

public class NatsListener : IListener
{
    private readonly NatsEndpoint _endpoint;
    private readonly NatsConnection _connection;
    private readonly IWolverineRuntime _runtime;
    private readonly IReceiver _receiver;
    private readonly ILogger<NatsEndpoint> _logger;
    private readonly CancellationTokenSource _cancellation;
    private readonly RetryBlock<NatsEnvelope> _complete;
    private readonly RetryBlock<NatsEnvelope> _defer;
    
    private IAsyncDisposable? _subscription;
    private Task? _consumerTask;

    public NatsListener(NatsEndpoint endpoint, NatsConnection connection, IWolverineRuntime runtime, 
        IReceiver receiver, ILogger<NatsEndpoint> logger, CancellationToken parentCancellation)
    {
        _endpoint = endpoint;
        _connection = connection;
        _runtime = runtime;
        _receiver = receiver;
        _logger = logger;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(parentCancellation);
        
        Address = endpoint.Uri;

        _complete = new RetryBlock<NatsEnvelope>(async (envelope, _) =>
        {
            // For JetStream messages, acknowledge them
            if (envelope.JetStreamMsg != null)
            {
                await envelope.JetStreamMsg.Value.AckAsync(cancellationToken: _cancellation.Token);
            }
        }, logger, _cancellation.Token);

        _defer = new RetryBlock<NatsEnvelope>(async (envelope, _) =>
        {
            // For JetStream messages, NAck to trigger redelivery
            if (envelope.JetStreamMsg != null)
            {
                await envelope.JetStreamMsg.Value.NakAsync(cancellationToken: _cancellation.Token);
            }
            
            // For core NATS, we'll need to manually retry
            await Task.Delay(TimeSpan.FromSeconds(5), _cancellation.Token);
            await _receiver.ReceivedAsync(this, envelope);
        }, logger, _cancellation.Token);
    }

    public Uri Address { get; }

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
        _logger.LogInformation("Starting Core NATS listener for subject {Subject} with queue group {QueueGroup}", 
            _endpoint.Subject, _endpoint.QueueGroup ?? "(none)");

        if (!string.IsNullOrEmpty(_endpoint.QueueGroup))
        {
            _subscription = await _connection.SubscribeCoreAsync<byte[]>(_endpoint.Subject, _endpoint.QueueGroup, 
                cancellationToken: _cancellation.Token);
        }
        else
        {
            _subscription = await _connection.SubscribeCoreAsync<byte[]>(_endpoint.Subject, 
                cancellationToken: _cancellation.Token);
        }

        _consumerTask = Task.Run(async () =>
        {
            await foreach (var msg in ((INatsSub<byte[]>)_subscription).Msgs.ReadAllAsync(_cancellation.Token))
            {
                try
                {
                    var envelope = EnvelopeMapper.ToEnvelope(msg, _runtime);
                    var natsEnvelope = new NatsEnvelope(envelope, msg, null);
                    
                    await _receiver.ReceivedAsync(this, natsEnvelope);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing NATS message from subject {Subject}", msg.Subject);
                }
            }
        }, _cancellation.Token);
    }

    private async Task StartJetStreamConsumerAsync()
    {
        _logger.LogInformation("Starting JetStream listener for stream {Stream}, consumer {Consumer}, subject {Subject}", 
            _endpoint.StreamName, _endpoint.ConsumerName ?? "(ephemeral)", _endpoint.Subject);

        var js = _connection.CreateJetStreamContext();
        
        // Create or get consumer
        INatsJSConsumer consumer;
        if (!string.IsNullOrEmpty(_endpoint.ConsumerName))
        {
            // Use existing durable consumer
            consumer = await js.GetConsumerAsync(_endpoint.StreamName!, _endpoint.ConsumerName, _cancellation.Token);
        }
        else
        {
            // Create ephemeral consumer
            var config = new ConsumerConfig
            {
                FilterSubject = _endpoint.Subject,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                MaxDeliver = 3,
                AckWait = TimeSpan.FromSeconds(30)
            };
            
            consumer = await js.CreateOrUpdateConsumerAsync(_endpoint.StreamName!, config, _cancellation.Token);
        }

        _consumerTask = Task.Run(async () =>
        {
            await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: _cancellation.Token))
            {
                try
                {
                    var envelope = EnvelopeMapper.ToEnvelope(msg, _runtime);
                    envelope.Attempts = (int)(msg.Metadata?.NumDelivered ?? 0);
                    
                    var natsEnvelope = new NatsEnvelope(envelope, null, msg);
                    
                    await _receiver.ReceivedAsync(this, natsEnvelope);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing JetStream message from subject {Subject}", msg.Subject);
                    
                    // NAck the message to trigger redelivery
                    await msg.NakAsync(cancellationToken: _cancellation.Token);
                }
            }
        }, _cancellation.Token);
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
        
        return new ValueTask(Task.WhenAll(
            _subscription?.DisposeAsync().AsTask() ?? Task.CompletedTask,
            _consumerTask ?? Task.CompletedTask,
            Task.Run(() =>
            {
                _complete.Dispose();
                _defer.Dispose();
                _cancellation.Dispose();
            })
        ));
    }
}