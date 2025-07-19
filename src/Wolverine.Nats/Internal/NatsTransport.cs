using JasperFx.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using Wolverine.Configuration;
using Wolverine.Nats.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace Wolverine.Nats.Internal;

public class NatsTransport : BrokerTransport<NatsEndpoint>, IAsyncDisposable
{
    public const string ProtocolName = "nats";

    private readonly JasperFx.Core.LightweightCache<string, NatsEndpoint> _endpoints = new();
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

    public override Uri ResourceUri =>
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
            
            // Provision configured streams
            if (Configuration.AutoProvision && Configuration.Streams.Any())
            {
                await ProvisionStreamsAsync();
            }
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

    private async Task ProvisionStreamsAsync()
    {
        _logger?.LogInformation("Provisioning {Count} configured streams", Configuration.Streams.Count);

        foreach (var (name, config) in Configuration.Streams)
        {
            try
            {
                // Check if stream exists
                var exists = false;
                try
                {
                    await JetStreamContext.GetStreamAsync(name);
                    exists = true;
                    _logger?.LogDebug("Stream {StreamName} already exists", name);
                }
                catch (NatsJSException)
                {
                    // Stream doesn't exist, we'll create it
                }

                if (!exists)
                {
                    // Create stream configuration
                    var streamConfig = new StreamConfig(name, config.Subjects)
                    {
                        Retention = config.Retention,
                        Storage = config.Storage,
                        MaxMsgs = config.MaxMessages ?? -1, // -1 for unlimited
                        MaxBytes = config.MaxBytes ?? -1, // -1 for unlimited
                        MaxAge = config.MaxAge ?? TimeSpan.Zero, // 0 for unlimited
                        MaxMsgsPerSubject = config.MaxMessagesPerSubject ?? 0, // 0 for default
                        Discard = config.DiscardPolicy,
                        NumReplicas = config.Replicas,
                        AllowRollupHdrs = config.AllowRollup,
                        AllowDirect = config.AllowDirect,
                        DenyDelete = config.DenyDelete,
                        DenyPurge = config.DenyPurge
                    };

                    await JetStreamContext.CreateStreamAsync(streamConfig);
                    _logger?.LogInformation("Created stream {StreamName} with subjects: {Subjects}", 
                        name, string.Join(", ", config.Subjects));
                }
                else
                {
                    // Optionally update stream if configuration has changed
                    _logger?.LogDebug("Stream {StreamName} already exists, skipping creation", name);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to provision stream {StreamName}", name);
                throw new InvalidOperationException($"Failed to provision stream '{name}'", ex);
            }
        }
    }

}
