# JetStream Integration Guide for Wolverine.Nats

This guide details how to leverage JetStream features for building a reliable, durable messaging transport for Wolverine.

## Overview

JetStream is NATS' persistence layer that provides:
- At-least-once delivery guarantees
- Message replay and persistence
- Acknowledgment-based reliability
- Consumer flow control
- Built-in deduplication

## Stream Design for Wolverine

### Stream Topology

```yaml
# Commands Stream - for request/response patterns
Stream: WOLVERINE_COMMANDS
Subjects: ["cmd.>"]
Retention: WorkQueue  # Delete after ack
MaxAge: 1 hour
MaxMsgs: 1,000,000
Discard: Old

# Events Stream - for publish/subscribe patterns  
Stream: WOLVERINE_EVENTS
Subjects: ["evt.>"]
Retention: Limits  # Keep for replay
MaxAge: 7 days
MaxMsgs: 10,000,000
Discard: Old

# Dead Letter Stream
Stream: WOLVERINE_DLQ
Subjects: ["dlq.>"]
Retention: Limits
MaxAge: 30 days
MaxMsgs: 100,000
```

### Stream Creation Code

```csharp
public async Task<INatsJSStream> CreateWolverineStream(
    INatsJSContext js,
    string streamName,
    string[] subjects,
    StreamConfigRetention retention)
{
    var config = new StreamConfig
    {
        Name = streamName,
        Subjects = subjects,
        Retention = retention,
        MaxAge = retention == StreamConfigRetention.WorkQueue 
            ? TimeSpan.FromHours(1) 
            : TimeSpan.FromDays(7),
        MaxMsgs = retention == StreamConfigRetention.WorkQueue 
            ? 1_000_000 
            : 10_000_000,
        Discard = StreamConfigDiscard.Old,
        DuplicateWindow = TimeSpan.FromMinutes(2),
        // For production
        NumReplicas = 3,
        // Compression for events
        Compression = retention == StreamConfigRetention.Limits 
            ? StreamConfigCompression.S2 
            : StreamConfigCompression.None
    };
    
    return await js.CreateStreamAsync(config);
}
```

## Consumer Patterns

### Pull Consumer for Handlers

Pull consumers are ideal for Wolverine handlers:

```csharp
public class JetStreamWolverineConsumer
{
    private readonly ConsumerConfig _config;
    
    public JetStreamWolverineConsumer(HandlerChain handlerChain)
    {
        _config = new ConsumerConfig
        {
            Name = $"wolverine-{handlerChain.MessageType.Name}",
            DurableName = $"wolverine-{handlerChain.MessageType.Name}",
            
            // Delivery
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            ReplayPolicy = ConsumerConfigReplayPolicy.Instant,
            
            // Flow control based on handler
            MaxAckPending = handlerChain.MaxConcurrency ?? 100,
            MaxDeliver = handlerChain.RetryLimit ?? 5,
            AckWait = handlerChain.Timeout ?? TimeSpan.FromSeconds(30),
            
            // Backoff for retries
            Backoff = GenerateBackoff(handlerChain.RetryLimit ?? 5),
            
            // Monitoring
            IdleHeartbeat = TimeSpan.FromSeconds(5),
            
            // Subject filtering
            FilterSubject = ConvertToNatsSubject(handlerChain.MessageType)
        };
    }
    
    private TimeSpan[] GenerateBackoff(int retries)
    {
        return Enumerable.Range(0, retries)
            .Select(i => TimeSpan.FromSeconds(Math.Pow(2, i)))
            .ToArray();
    }
}
```

### Ordered Consumer for Sagas

For saga/workflow patterns requiring strict ordering:

```csharp
public async Task<INatsJSConsumer> CreateOrderedConsumer(
    string streamName,
    string filterSubject)
{
    var config = new ConsumerConfig
    {
        Name = null,  // Ephemeral
        DeliverPolicy = ConsumerConfigDeliverPolicy.All,
        AckPolicy = ConsumerConfigAckPolicy.None,  // Auto-ack
        ReplayPolicy = ConsumerConfigReplayPolicy.Instant,
        MaxDeliver = 1,  // No retries
        MemoryStorage = true,  // Fast
        NumReplicas = 1,  // Single instance
        FilterSubject = filterSubject,
        IdleHeartbeat = TimeSpan.FromSeconds(5),
        InactiveThreshold = TimeSpan.FromMinutes(5)
    };
    
    return await js.CreateOrderedConsumerAsync(streamName, config);
}
```

## Message Flow Patterns

### 1. Inbox Pattern (At-Least-Once)

```csharp
public class JetStreamInboxProcessor
{
    public async Task ProcessInboxMessages(
        INatsJSConsumer consumer,
        IReceiver receiver,
        CancellationToken ct)
    {
        await foreach (var msg in consumer.ConsumeAsync<byte[]>(
            opts: new NatsJSConsumeOpts
            {
                MaxMsgs = 100,  // Batch size
                Expires = TimeSpan.FromSeconds(30),  // Wait time
                IdleHeartbeat = TimeSpan.FromSeconds(5)
            },
            cancellationToken: ct))
        {
            try
            {
                var envelope = ToEnvelope(msg);
                await receiver.ReceivedAsync(this, envelope);
                await msg.AckAsync();
            }
            catch (TransientException ex)
            {
                _logger.LogWarning(ex, "Transient error, retrying");
                // Exponential backoff based on redelivery count
                var delay = TimeSpan.FromSeconds(Math.Pow(2, msg.Metadata?.NumDelivered ?? 0));
                await msg.NakAsync(delay: delay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Permanent error, dead lettering");
                await msg.AckTerminateAsync();
                await PublishToDeadLetter(msg);
            }
        }
    }
}
```

### 2. Event Sourcing Pattern

```csharp
public class JetStreamEventStore
{
    public async Task AppendEvents(
        string aggregateId,
        IEnumerable<object> events)
    {
        var streamSubject = $"evt.{aggregateId}";
        
        foreach (var evt in events)
        {
            var envelope = new Envelope(evt);
            var headers = new NatsHeaders
            {
                ["Nats-Msg-Id"] = $"{aggregateId}:{envelope.Id}",
                ["Event-Type"] = evt.GetType().Name,
                ["Aggregate-Id"] = aggregateId,
                ["Event-Version"] = GetEventVersion(evt).ToString()
            };
            
            await _js.PublishAsync(
                streamSubject,
                envelope.Data,
                headers: headers,
                opts: new NatsJSPubOpts
                {
                    ExpectedLastMsgId = GetLastEventId(aggregateId),
                    ExpectedStream = "WOLVERINE_EVENTS"
                });
        }
    }
    
    public async Task<IEnumerable<T>> LoadEvents<T>(
        string aggregateId,
        long fromVersion = 0)
    {
        var consumer = await _js.CreateOrUpdateConsumerAsync(
            "WOLVERINE_EVENTS",
            new ConsumerConfig
            {
                FilterSubject = $"evt.{aggregateId}",
                DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence,
                OptStartSeq = fromVersion,
                AckPolicy = ConsumerConfigAckPolicy.None,
                ReplayPolicy = ConsumerConfigReplayPolicy.Instant
            });
            
        var events = new List<T>();
        await foreach (var msg in consumer.ConsumeAsync<byte[]>())
        {
            var evt = DeserializeEvent<T>(msg);
            events.Add(evt);
            
            if (IsLatestVersion(msg))
                break;
        }
        
        return events;
    }
}
```

### 3. Work Queue Pattern

```csharp
public class JetStreamWorkQueue
{
    public async Task<INatsJSConsumer> CreateWorkQueueConsumer(
        string queueName,
        int workers)
    {
        var config = new ConsumerConfig
        {
            Name = $"wq-{queueName}",
            DurableName = $"wq-{queueName}",
            FilterSubject = $"work.{queueName}",
            
            // Work queue settings
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            MaxAckPending = workers * 2,  // Allow some buffering
            MaxDeliver = 3,
            AckWait = TimeSpan.FromMinutes(5),  // Long running tasks
            
            // Fair distribution
            MaxWaiting = workers,  // Limit concurrent pulls
            
            // Monitoring
            IdleHeartbeat = TimeSpan.FromSeconds(30),
            InactiveThreshold = TimeSpan.FromMinutes(10)
        };
        
        return await _js.CreateOrUpdateConsumerAsync(
            "WOLVERINE_COMMANDS", 
            config);
    }
}
```

## Deduplication Strategies

### 1. Message-Level Deduplication

```csharp
public class DeduplicatingPublisher
{
    public async Task PublishWithDeduplication(
        Envelope envelope,
        string subject)
    {
        var headers = new NatsHeaders
        {
            // Use envelope ID for dedup
            ["Nats-Msg-Id"] = envelope.Id.ToString()
        };
        
        var ack = await _js.PublishAsync(
            subject,
            envelope.Data,
            headers: headers);
            
        if (ack.Duplicate)
        {
            _logger.LogDebug("Duplicate message {Id} detected", envelope.Id);
        }
    }
}
```

### 2. Stream-Level Deduplication

```csharp
var stream = await js.CreateStreamAsync(new StreamConfig
{
    Name = "DEDUPLICATED_STREAM",
    Subjects = new[] { "dedup.>" },
    
    // 5 minute deduplication window
    DuplicateWindow = TimeSpan.FromMinutes(5),
    
    // Store more message IDs in memory
    MaxMsgSize = 1024 * 1024,  // 1MB max
    MaxMsgs = 1_000_000
});
```

## Error Handling and Dead Letters

### Dead Letter Implementation

```csharp
public class JetStreamDeadLetterHandler
{
    private readonly INatsJSContext _js;
    private readonly ILogger _logger;
    
    public async Task HandleDeadLetter(NatsJSMsg<byte[]> msg)
    {
        var dlqSubject = $"dlq.{msg.Subject.Replace('.', '_')}";
        
        var headers = new NatsHeaders(msg.Headers ?? new NatsHeaders())
        {
            ["Original-Subject"] = msg.Subject,
            ["Failed-At"] = DateTimeOffset.UtcNow.ToString("O"),
            ["Delivery-Count"] = msg.Metadata?.NumDelivered.ToString() ?? "0",
            ["Consumer"] = msg.Metadata?.Consumer ?? "unknown",
            ["Stream"] = msg.Metadata?.Stream ?? "unknown"
        };
        
        // Add error information
        if (msg.Headers?.TryGetValue("Error-Type", out var errorType) == true)
        {
            headers["Error-Type"] = errorType;
        }
        
        await _js.PublishAsync(
            dlqSubject,
            msg.Data,
            headers: headers);
            
        _logger.LogWarning(
            "Message dead lettered: {Subject} -> {DLQ}", 
            msg.Subject, 
            dlqSubject);
    }
}
```

### Retry Pattern with Backoff

```csharp
public class RetryHandler
{
    public async Task ProcessWithRetry(
        NatsJSMsg<byte[]> msg,
        Func<Task> handler)
    {
        try
        {
            await handler();
            await msg.AckAsync();
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            var attempt = (int)(msg.Metadata?.NumDelivered ?? 1);
            
            if (attempt >= _maxRetries)
            {
                await msg.AckTerminateAsync();
                await _deadLetterHandler.HandleDeadLetter(msg);
                return;
            }
            
            // Exponential backoff with jitter
            var baseDelay = Math.Pow(2, attempt - 1);
            var jitter = Random.Shared.NextDouble() * 0.3 * baseDelay;
            var delay = TimeSpan.FromSeconds(baseDelay + jitter);
            
            await msg.NakAsync(delay: delay);
        }
        catch (Exception ex)
        {
            // Permanent failure
            await msg.AckTerminateAsync();
            await _deadLetterHandler.HandleDeadLetter(msg);
        }
    }
}
```

## Monitoring and Operations

### Consumer Health Monitoring

```csharp
public class ConsumerMonitor
{
    public async Task MonitorConsumerHealth(
        string streamName,
        string consumerName)
    {
        // Subscribe to consumer advisories
        await _connection.SubscribeAsync(
            $"$JS.EVENT.ADVISORY.CONSUMER.MSG_TERMINATED.{streamName}.{consumerName}",
            msg => LogTerminatedMessage(msg));
            
        await _connection.SubscribeAsync(
            $"$JS.EVENT.ADVISORY.CONSUMER.MAX_DELIVERIES.{streamName}.{consumerName}",
            msg => LogMaxDeliveries(msg));
            
        // Periodic health check
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync())
        {
            var info = await _js.GetConsumerAsync(streamName, consumerName);
            
            if (info.Info.NumPending > _alertThreshold)
            {
                _logger.LogWarning(
                    "Consumer lag alert: {Consumer} has {Pending} messages",
                    consumerName,
                    info.Info.NumPending);
            }
        }
    }
}
```

### Stream Maintenance

```csharp
public class StreamMaintenance
{
    public async Task PurgeOldMessages(string streamName)
    {
        var stream = await _js.GetStreamAsync(streamName);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        
        await stream.PurgeAsync(new StreamPurgeRequest
        {
            Subject = "*",
            Keep = 0,
            Sequence = await GetSequenceBeforeDate(stream, cutoff)
        });
    }
    
    public async Task CompactStream(string streamName)
    {
        // For key-value patterns
        var stream = await _js.GetStreamAsync(streamName);
        
        await _js.UpdateStreamAsync(new StreamConfig(stream.Info.Config)
        {
            DiscardPerSubject = true,
            MaxMsgsPerSubject = 1
        });
    }
}
```

## Best Practices

1. **Stream Per Domain**: Create separate streams for different domains or message types
2. **Consumer Per Handler**: One consumer per Wolverine handler for isolation
3. **Appropriate Retention**: Use WorkQueue for commands, Limits for events
4. **Deduplication Window**: Set based on expected message patterns
5. **Monitor Consumer Lag**: Alert on growing pending messages
6. **Batch Processing**: Use pull consumers with appropriate batch sizes
7. **Graceful Shutdown**: Properly dispose consumers and complete in-flight messages

This integration guide provides patterns for leveraging JetStream's reliability and durability features in your Wolverine transport implementation.