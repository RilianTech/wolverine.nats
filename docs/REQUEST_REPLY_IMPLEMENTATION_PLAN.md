# NATS Request/Reply Implementation Plan

## Overview

This document outlines the implementation plan for adding Request/Reply pattern support to Wolverine.Nats, enabling `InvokeAsync<T>` functionality using NATS native request/reply capabilities.

## Current Status

**Status**: Not yet implemented  
**Priority**: Medium  
**Estimated Effort**: 1-2 weeks  
**Complexity**: Medium - requires integration with Wolverine's remote invocation infrastructure

## Technical Requirements

### 1. NATS Request/Reply Fundamentals

NATS request/reply is built on core pub/sub with these key concepts:
- **Inbox Subjects**: Unique reply-to subjects (e.g., `_INBOX.ABC123XYZ`)
- **Automatic Correlation**: The inbox subject itself provides correlation
- **Timeout Handling**: Built-in timeout support via subscription options
- **No Separate Protocol**: Uses standard NATS pub/sub under the hood

### 2. NATS.Net Client API

The primary method we'll integrate with:
```csharp
public async ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(
    string subject,
    TRequest? data,
    NatsHeaders? headers = default,
    INatsSerialize<TRequest>? requestSerializer = default,
    INatsDeserialize<TReply>? replySerializer = default,
    NatsPubOpts? requestOpts = default,
    NatsSubOpts? replyOpts = default,
    CancellationToken cancellationToken = default)
```

### 3. Wolverine Integration Points

Based on research of Wolverine's remote invocation system:

**Key Interfaces:**
- `IMessageInvoker` - Entry point for `InvokeAsync<T>`
- `IReplyTracker` - Manages waiting for replies
- `IRemoteInvocation` - Handles remote message invocation
- `MessageRoute` - Routing information for messages

**Reply Tracking:**
```csharp
public interface IReplyTracker
{
    Task<T> RegisterListener<T>(Envelope envelope, CancellationToken cancellation, TimeSpan? timeout);
    void Handle(Envelope envelope);
}
```

## Implementation Plan

### Phase 1: Core Request/Reply Infrastructure (Week 1)

#### 1.1 Extend NatsSender with Request Capability

```csharp
public class NatsSender : ISender, ISupportNativeScheduling, ISupportsRemoteInvocation
{
    // Existing implementation...
    
    // New request/reply support
    public async Task<T> InvokeAsync<T>(object message, CancellationToken cancellation, TimeSpan? timeout)
    {
        var envelope = new Envelope(message)
        {
            Id = Guid.NewGuid(),
            Source = _endpoint.TransportUri.ToString(),
            ConversationId = CombGuidIdGeneration.NewGuid(),
            ReplyRequested = typeof(T).ToMessageTypeName()
        };

        // Create inbox and set reply URI
        var inboxSubject = _connection.NewInbox();
        envelope.ReplyUri = new Uri($"nats://{inboxSubject}");

        // Configure reply subscription options
        var replyOpts = new NatsSubOpts 
        { 
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
            MaxMsgs = 1,
            ThrowIfNoResponders = true
        };

        try
        {
            // Use NATS native request/reply
            var response = await _connection.RequestAsync<byte[], byte[]>(
                _endpoint.Subject,
                envelope.Data,
                headers: _endpoint.BuildHeaders(envelope),
                replyOpts: replyOpts,
                cancellationToken: cancellation
            );

            // Map response back to Wolverine envelope
            var replyEnvelope = new Envelope();
            _mapper.MapIncomingToEnvelope(replyEnvelope, response);
            
            return (T)replyEnvelope.Message!;
        }
        catch (NatsNoReplyException)
        {
            throw new TimeoutException($"Request to {_endpoint.Subject} timed out after {timeout}");
        }
        catch (NatsNoRespondersException)
        {
            throw new IndeterminateRoutesException($"No handlers found for {_endpoint.Subject}");
        }
    }
}
```

#### 1.2 Update Interface Implementation

```csharp
public interface ISupportsRemoteInvocation
{
    Task<T> InvokeAsync<T>(object message, CancellationToken cancellation, TimeSpan? timeout);
}
```

#### 1.3 Enhance Envelope Mapping

```csharp
public class NatsEnvelopeMapper : EnvelopeMapper<NatsMsg<byte[]>, NatsHeaders>
{
    protected override void writeOutgoingHeader(NatsHeaders headers, string key, string value)
    {
        // Existing implementation...
        
        // Special handling for reply-to
        if (key == "reply-to" && !string.IsNullOrEmpty(value))
        {
            // Extract NATS subject from reply URI
            var replyUri = new Uri(value);
            var replySubject = ExtractSubjectFromUri(replyUri);
            headers["reply-to"] = replySubject;
        }
    }

    protected override void mapHeaders(NatsMsg<byte[]> incoming, Envelope envelope)
    {
        // Existing implementation...
        
        // Map reply-to header back to ReplyUri
        if (incoming.Headers != null && incoming.Headers.TryGetValue("reply-to", out var replyTo))
        {
            envelope.ReplyUri = new Uri($"nats://{replyTo}");
        }
    }
}
```

### Phase 2: Response Handling (Week 1)

#### 2.1 Implement Reply Message Handling

```csharp
public class NatsListener : IListener, ISupportDeadLetterQueue
{
    // Existing implementation...

    private async Task ProcessMessage(NatsMsg<byte[]> msg)
    {
        var envelope = new Envelope();
        _mapper.MapIncomingToEnvelope(envelope, msg);
        
        // Check if this is a request requiring a reply
        if (!string.IsNullOrEmpty(msg.ReplyTo))
        {
            envelope.ReplyUri = new Uri($"nats://{msg.ReplyTo}");
        }

        var natsEnvelope = new NatsEnvelope(envelope, msg, null);
        await _receiver.ReceivedAsync(this, natsEnvelope);
    }

    // Handle outgoing replies
    public async Task SendReplyAsync(Envelope replyEnvelope, string replyToSubject)
    {
        var headers = _endpoint.BuildHeaders(replyEnvelope);
        await _connection.PublishAsync(replyToSubject, replyEnvelope.Data, headers);
    }
}
```

#### 2.2 Integrate with Wolverine's Reply Infrastructure

```csharp
public class NatsReplyTracker : IReplyTracker
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<Envelope>> _waitingRequests = new();

    public Task<T> RegisterListener<T>(Envelope envelope, CancellationToken cancellation, TimeSpan? timeout)
    {
        var tcs = new TaskCompletionSource<Envelope>();
        _waitingRequests[envelope.ConversationId] = tcs;

        // Set up timeout
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        if (timeout.HasValue)
        {
            timeoutCts.CancelAfter(timeout.Value);
        }

        timeoutCts.Token.Register(() =>
        {
            _waitingRequests.TryRemove(envelope.ConversationId, out _);
            tcs.TrySetException(new TimeoutException($"Request timed out after {timeout}"));
        });

        return tcs.Task.ContinueWith(task => (T)task.Result.Message!, cancellation);
    }

    public void Handle(Envelope envelope)
    {
        if (_waitingRequests.TryRemove(envelope.ConversationId, out var tcs))
        {
            tcs.SetResult(envelope);
        }
    }
}
```

### Phase 3: Configuration and Integration (Week 2)

#### 3.1 Add Request/Reply Configuration

```csharp
public class NatsTransportConfiguration
{
    // Existing properties...
    
    /// <summary>
    /// Default timeout for request/reply operations
    /// </summary>
    public TimeSpan DefaultRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Enable request/reply pattern support
    /// </summary>
    public bool EnableRequestReply { get; set; } = true;
    
    /// <summary>
    /// Inbox prefix for request/reply operations
    /// </summary>
    public string InboxPrefix { get; set; } = "_INBOX.wolverine";
}
```

#### 3.2 Update Transport Registration

```csharp
public class NatsTransport : TransportBase<NatsEndpoint>, IAsyncDisposable
{
    public override async ValueTask InitializeAsync(IWolverineRuntime runtime)
    {
        // Existing implementation...
        
        // Register reply tracker if request/reply is enabled
        if (Configuration.EnableRequestReply)
        {
            runtime.Container.TryAddSingleton<IReplyTracker, NatsReplyTracker>();
        }
    }
}
```

#### 3.3 Add Extension Methods

```csharp
public static class NatsRequestReplyExtensions
{
    /// <summary>
    /// Configure request/reply timeout for this endpoint
    /// </summary>
    public static NatsSubscriberConfiguration RequestTimeout(
        this NatsSubscriberConfiguration configuration, 
        TimeSpan timeout)
    {
        configuration.add(endpoint =>
        {
            // Store timeout in endpoint configuration
            endpoint.CustomHeaders["x-request-timeout"] = timeout.TotalSeconds.ToString();
        });
        
        return configuration;
    }
    
    /// <summary>
    /// Enable request/reply pattern for this publisher
    /// </summary>
    public static NatsSubscriberConfiguration EnableRequestReply(
        this NatsSubscriberConfiguration configuration)
    {
        configuration.add(endpoint =>
        {
            // Mark endpoint as supporting request/reply
            endpoint.CustomHeaders["x-supports-request-reply"] = "true";
        });
        
        return configuration;
    }
}
```

## Testing Strategy

### Unit Tests

```csharp
[Test]
public async Task can_invoke_request_reply_pattern()
{
    // Arrange
    using var host = await CreateHost(opts =>
    {
        opts.UseNats("nats://localhost:4222");
        opts.PublishMessage<PingMessage>().ToNatsSubject("ping");
        opts.ListenToNatsSubject("ping");
    });

    var bus = host.GetRequiredService<IMessageBus>();

    // Act
    var response = await bus.InvokeAsync<PongMessage>(new PingMessage { Value = "test" });

    // Assert
    response.Should().NotBeNull();
    response.Value.Should().Be("test");
}

[Test]
public async Task request_reply_respects_timeout()
{
    // Arrange - no listener, should timeout
    using var host = await CreateHost(opts =>
    {
        opts.UseNats("nats://localhost:4222");
        opts.PublishMessage<PingMessage>().ToNatsSubject("nonexistent");
    });

    var bus = host.GetRequiredService<IMessageBus>();

    // Act & Assert
    await Assert.ThrowsAsync<TimeoutException>(async () =>
    {
        await bus.InvokeAsync<PongMessage>(
            new PingMessage { Value = "test" },
            timeout: TimeSpan.FromMilliseconds(100));
    });
}
```

### Integration Tests

```csharp
public class PingHandler
{
    public PongMessage Handle(PingMessage ping)
    {
        return new PongMessage { Value = ping.Value };
    }
}

public class PingMessage
{
    public string Value { get; set; } = "";
}

public class PongMessage  
{
    public string Value { get; set; } = "";
}
```

### Sample Application

Create `samples/RequestReplyWithNats/` demonstrating:
- Simple request/reply pattern
- Timeout handling
- Error scenarios
- Performance characteristics

## Error Handling

### Exception Mapping

| NATS Exception | Wolverine Exception | Description |
|----------------|-------------------|-------------|
| `NatsNoReplyException` | `TimeoutException` | No response within timeout |
| `NatsNoRespondersException` | `IndeterminateRoutesException` | No handlers available |
| `NatsTimeoutException` | `TimeoutException` | Request timeout exceeded |

### Retry Logic

```csharp
public async Task<T> InvokeAsync<T>(object message, CancellationToken cancellation, TimeSpan? timeout)
{
    const int maxRetries = 3;
    var delay = TimeSpan.FromMilliseconds(100);
    
    for (int attempt = 0; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await PerformRequest<T>(message, cancellation, timeout);
        }
        catch (NatsNoRespondersException) when (attempt < maxRetries)
        {
            await Task.Delay(delay, cancellation);
            delay = TimeSpan.FromTicks(delay.Ticks * 2); // Exponential backoff
        }
    }
    
    throw new IndeterminateRoutesException($"No handlers found after {maxRetries} attempts");
}
```

## Performance Considerations

### Connection Reuse
- Use existing NATS connection for requests
- Avoid creating new connections per request
- Leverage connection pooling if needed

### Inbox Management
- Use NATS built-in inbox generation
- Automatic cleanup via subscription disposal
- Monitor inbox subscription lifecycle

### Memory Management
- Dispose reply subscriptions promptly
- Use `ValueTask<T>` for async operations
- Implement proper cancellation support

## JetStream Compatibility

### Request Messages
- Can use JetStream for durable request delivery
- Configure with appropriate retention policy
- Consider using Core NATS for faster delivery

### Reply Messages
- Should use Core NATS for immediate delivery
- Replies typically don't need persistence
- Keep reply subjects simple and ephemeral

```csharp
// Configuration for JetStream requests with Core NATS replies
opts.PublishMessage<ImportantRequest>()
    .ToNatsSubject("requests.important")
    .UseJetStream("REQUESTS") // Durable request delivery
    .EnableRequestReply(); // But replies use Core NATS
```

## Migration Strategy

### Phase 1: Basic Implementation
- Core request/reply functionality
- Basic timeout handling
- Simple error mapping

### Phase 2: Advanced Features  
- Retry logic and circuit breakers
- Performance optimizations
- JetStream integration options

### Phase 3: Production Features
- Monitoring and metrics
- Advanced error handling
- Load balancing considerations

## Success Criteria

✅ **Functional Requirements:**
- `IMessageBus.InvokeAsync<T>()` works with NATS subjects
- Proper timeout handling and cancellation support
- Error scenarios handled gracefully
- Reply correlation works correctly

✅ **Performance Requirements:**
- Sub-millisecond overhead for request/reply setup
- Memory efficient inbox management
- Scales to 1000+ concurrent requests

✅ **Integration Requirements:**
- Works with existing Wolverine message handlers
- Integrates with Wolverine's serialization system
- Compatible with both Core NATS and JetStream
- Supports Wolverine's error handling patterns

## Future Enhancements

### Advanced Patterns
- **Scatter-Gather**: Send to multiple responders, collect results
- **Load Balancing**: Distribute requests across multiple handlers
- **Circuit Breakers**: Handle downstream service failures gracefully

### Monitoring Integration
- Request/reply metrics collection
- Timeout and error rate monitoring
- Performance dashboards

### Multi-Tenancy Support
- Account-based request routing
- Secure reply subject isolation
- Permission-based access control

This implementation plan provides a comprehensive roadmap for adding robust Request/Reply support to Wolverine.Nats while maintaining compatibility with existing Wolverine patterns and leveraging NATS native capabilities.