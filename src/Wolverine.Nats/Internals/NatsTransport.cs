using JasperFx.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Net;
using Wolverine.Configuration;
using Wolverine.Nats.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace Wolverine.Nats.Internals;

public class NatsTransport : TransportBase<NatsEndpoint>, IAsyncDisposable
{
    private readonly LightweightCache<string, NatsEndpoint> _endpoints = new();
    private NatsConnection? _connection;
    private INatsJSContext? _jetStreamContext;
    private ILogger<NatsTransport>? _logger;

    public NatsTransport() : base("nats", "NATS Transport")
    {
        _endpoints.OnMissing = subject =>
        {
            var normalized = NormalizeSubject(subject);
            return new NatsEndpoint(normalized, this, EndpointRole.Application);
        };
    }

    public string ResponseSubject { get; private set; } = "wolverine.response";
    
    public NatsTransportConfiguration Configuration { get; } = new();

    public NatsConnection Connection => _connection ?? throw new InvalidOperationException("NATS connection not initialized");
    
    public INatsJSContext JetStreamContext => _jetStreamContext ?? throw new InvalidOperationException("JetStream context not initialized");

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

    public override async ValueTask InitializeAsync(IWolverineRuntime runtime)
    {
        _logger = runtime.LoggerFactory.CreateLogger<NatsTransport>();
        
        // Configure response subject with node identifier
        ResponseSubject = $"wolverine.response.{runtime.Options.Durability.AssignedNodeNumber}";
        var responseEndpoint = _endpoints[ResponseSubject];
        responseEndpoint.IsUsedForReplies = true;
        responseEndpoint.IsListener = true;

        // Initialize NATS connection
        var natsOpts = BuildNatsOptions(runtime);
        _connection = new NatsConnection(natsOpts);
        await _connection.ConnectAsync();

        _logger.LogInformation("Connected to NATS at {Url}", Configuration.ConnectionString);
        
        // Initialize JetStream context if enabled
        if (Configuration.EnableJetStream)
        {
            var jsOpts = new NatsJSOpts(natsOpts, domain: Configuration.JetStreamDomain);
            _jetStreamContext = new NatsJSContext(_connection, jsOpts);
            _logger.LogInformation("JetStream context initialized");
        }

        // Compile all endpoints
        foreach (var endpoint in _endpoints)
        {
            endpoint.Compile(runtime);
        }
    }

    private NatsOpts BuildNatsOptions(IWolverineRuntime runtime)
    {
        var opts = NatsOpts.Default with
        {
            Url = Configuration.ConnectionString,
            Name = $"wolverine-{runtime.Options.ServiceName}",
            ConnectTimeout = Configuration.ConnectTimeout,
            RequestTimeout = Configuration.RequestTimeout,
        };

        // Configure authentication if provided
        if (!string.IsNullOrEmpty(Configuration.Username) && !string.IsNullOrEmpty(Configuration.Password))
        {
            opts = opts with
            {
                AuthOpts = new NatsAuthOpts
                {
                    Username = Configuration.Username,
                    Password = Configuration.Password
                }
            };
        }
        else if (!string.IsNullOrEmpty(Configuration.Token))
        {
            opts = opts with
            {
                AuthOpts = new NatsAuthOpts
                {
                    Token = Configuration.Token
                }
            };
        }
        else if (!string.IsNullOrEmpty(Configuration.NKeyFile))
        {
            opts = opts with
            {
                AuthOpts = new NatsAuthOpts
                {
                    NKey = Configuration.NKeyFile
                }
            };
        }

        // Configure TLS if enabled
        if (Configuration.EnableTls)
        {
            opts = opts with
            {
                TlsOpts = new NatsTlsOpts
                {
                    Mode = Configuration.TlsInsecure ? TlsMode.Implicit : TlsMode.Require,
                    InsecureSkipVerify = Configuration.TlsInsecure
                }
            };
        }

        return opts;
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