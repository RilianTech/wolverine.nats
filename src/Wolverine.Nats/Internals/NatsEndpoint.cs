using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Net;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports;
using Wolverine.Transports.Sending;

namespace Wolverine.Nats.Internals;

public class NatsEndpoint : Endpoint
{
    private readonly NatsTransport _transport;
    private NatsConnection? _connection;
    private ILogger<NatsEndpoint>? _logger;

    public NatsEndpoint(string subject, NatsTransport transport, EndpointRole role) 
        : base(new Uri($"nats://{subject}"), role)
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

    protected override bool supportsMode(EndpointMode mode)
    {
        return mode == EndpointMode.BufferedInMemory || mode == EndpointMode.Inline;
    }

    protected override ISender CreateSender(IWolverineRuntime runtime)
    {
        _connection = _transport.Connection;
        _logger = runtime.LoggerFactory.CreateLogger<NatsEndpoint>();
        
        return new NatsSender(this, _connection, _logger, runtime.Cancellation);
    }

    public override async ValueTask<IListener> BuildListenerAsync(IWolverineRuntime runtime, IReceiver receiver)
    {
        _connection = _transport.Connection;
        _logger = runtime.LoggerFactory.CreateLogger<NatsEndpoint>();

        var listener = new NatsListener(this, _connection, runtime, receiver, _logger, runtime.Cancellation);
        
        await listener.StartAsync();
        
        return listener;
    }

    public NatsHeaders BuildHeaders(Envelope envelope)
    {
        var headers = EnvelopeMapper.ToNatsHeaders(envelope);
        
        // Add custom headers specific to this endpoint
        foreach (var header in CustomHeaders)
        {
            headers[header.Key] = header.Value;
        }
        
        return headers;
    }

}