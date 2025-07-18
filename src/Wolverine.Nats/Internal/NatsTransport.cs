using JasperFx.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Net;
using Wolverine.Configuration;
using Wolverine.Nats.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace Wolverine.Nats.Internal;

public class NatsTransport : BrokerTransport<NatsEndpoint>, IAsyncDisposable
{
    public const string ProtocolName = "nats";

    private readonly LightweightCache<string, NatsEndpoint> _endpoints = new();
    private NatsConnection? _connection;
    private INatsJSContext? _jetStreamContext;
    private ILogger<NatsTransport>? _logger;

    public NatsTransport()
        : base(ProtocolName, "NATS Transport")
    {
        _endpoints.OnMissing = subject =>
        {
            var normalized = NormalizeSubject(subject);
            return new NatsEndpoint(normalized, this, EndpointRole.Application);
        };
    }

    public Uri ResourceUri =>
        Configuration.ConnectionString != null
            ? new Uri(Configuration.ConnectionString)
            : new Uri("nats://localhost:4222");

    public string ResponseSubject { get; private set; } = "wolverine.response";

    public NatsTransportConfiguration Configuration { get; } = new();

    public NatsConnection Connection =>
        _connection ?? throw new InvalidOperationException("NATS connection not initialized");

    public INatsJSContext JetStreamContext =>
        _jetStreamContext
        ?? throw new InvalidOperationException("JetStream context not initialized");

    protected override IEnumerable<NatsEndpoint> endpoints() => _endpoints;

    protected override NatsEndpoint findEndpointByUri(Uri uri)
    {
        var subject = ExtractSubjectFromUri(uri);
        return _endpoints[subject];
    }

    public override Endpoint ReplyEndpoint()
    {
        return _endpoints[ResponseSubject];
    }

    public override async ValueTask ConnectAsync(IWolverineRuntime runtime)
    {
        _logger = runtime.LoggerFactory.CreateLogger<NatsTransport>();

        // Configure response subject with node identifier
        ResponseSubject = $"wolverine.response.{runtime.Options.Durability.AssignedNodeNumber}";
        var responseEndpoint = _endpoints[ResponseSubject];
        responseEndpoint.IsUsedForReplies = true;
        responseEndpoint.IsListener = true;

        // Initialize NATS connection
        var natsOpts = Configuration.ToNatsOpts();
        natsOpts = natsOpts with { Name = $"wolverine-{runtime.Options.ServiceName}" };
        _connection = new NatsConnection(natsOpts);
        await _connection.ConnectAsync();

        _logger.LogInformation("Connected to NATS at {Url}", Configuration.ConnectionString);

        // Initialize JetStream context if enabled
        if (Configuration.EnableJetStream)
        {
            _jetStreamContext = _connection.CreateJetStreamContext();
            _logger.LogInformation("JetStream context initialized");
        }
    }

    public override IEnumerable<PropertyColumn> DiagnosticColumns()
    {
        yield return new PropertyColumn("Subject", "header");
        yield return new PropertyColumn("Queue Group", "header");
        yield return new PropertyColumn("JetStream", "header");
        yield return new PropertyColumn("Consumer Name");
    }

    public static string NormalizeSubject(string subject)
    {
        return subject.Trim().Replace('/', '.');
    }

    public static string ExtractSubjectFromUri(Uri uri)
    {
        if (uri.Scheme != "nats")
        {
            throw new ArgumentException($"Invalid URI scheme. Expected 'nats', got '{uri.Scheme}'");
        }

        // Handle both nats://subject and nats://server/subject formats
        var path = uri.LocalPath.Trim('/');
        return string.IsNullOrEmpty(path) ? uri.Host : path;
    }

    public NatsEndpoint EndpointForSubject(string subject)
    {
        var normalized = NormalizeSubject(subject);
        return _endpoints[normalized];
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_connection != null)
            {
                await _connection.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error disposing NATS connection");
        }
    }
}
