using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Wolverine.Nats.Configuration;

/// <summary>
/// Wolverine-focused NATS transport configuration supporting all NATS.Net auth methods
/// </summary>
public class NatsTransportConfiguration
{
    // === Core Connection Settings ===

    /// <summary>
    /// NATS server connection string - supports comma-separated URLs for clustering
    /// </summary>
    public string ConnectionString { get; set; } = "nats://localhost:4222";

    /// <summary>
    /// Client name for connection identification
    /// </summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// Connection timeout for establishing NATS connection
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Request timeout for Wolverine's InvokeAsync pattern
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // === Authentication Methods (all NATS.Net auth patterns) ===

    /// <summary>
    /// Username for basic authentication
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for basic authentication
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Token authentication
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// JWT for JWT authentication (auth callout pattern)
    /// </summary>
    public string? Jwt { get; set; }

    /// <summary>
    /// NKey seed for JWT/NKey authentication
    /// </summary>
    public string? NKeySeed { get; set; }

    /// <summary>
    /// Path to NATS credentials file (.creds)
    /// </summary>
    public string? CredentialsFile { get; set; }

    // Note: Raw credentials content is not directly supported in NatsAuthOpts
    // Use CredentialsFile instead

    /// <summary>
    /// Path to NKey file
    /// </summary>
    public string? NKeyFile { get; set; }

    /// <summary>
    /// Dynamic authentication callback for advanced scenarios
    /// </summary>
    public Func<Uri, CancellationToken, ValueTask<NatsAuthCred>>? AuthCallback { get; set; }

    // === TLS Configuration ===

    /// <summary>
    /// Enable TLS for connections
    /// </summary>
    public bool EnableTls { get; set; }

    /// <summary>
    /// TLS mode (Auto, Prefer, Require, Disable)
    /// </summary>
    public TlsMode TlsMode { get; set; } = TlsMode.Auto;

    /// <summary>
    /// Skip TLS certificate verification (development only)
    /// </summary>
    public bool TlsInsecure { get; set; }

    /// <summary>
    /// Client certificate file for mutual TLS
    /// </summary>
    public string? ClientCertFile { get; set; }

    /// <summary>
    /// Client key file for mutual TLS
    /// </summary>
    public string? ClientKeyFile { get; set; }

    /// <summary>
    /// CA certificate file for custom CA
    /// </summary>
    public string? CaFile { get; set; }

    // === JetStream Configuration ===

    /// <summary>
    /// Enable JetStream for durable messaging
    /// </summary>
    public bool EnableJetStream { get; set; } = true;

    /// <summary>
    /// JetStream domain for multi-tenancy
    /// </summary>
    public string? JetStreamDomain { get; set; }

    /// <summary>
    /// JetStream API prefix (default: $JS.API)
    /// </summary>
    public string? JetStreamApiPrefix { get; set; }

    // === Wolverine Transport Settings ===

    /// <summary>
    /// Whether to automatically provision JetStream resources
    /// </summary>
    public bool AutoProvision { get; set; } = true;

    /// <summary>
    /// Default queue group for load balancing
    /// </summary>
    public string? DefaultQueueGroup { get; set; }

    /// <summary>
    /// Whether to normalize subjects (replace '/' with '.')
    /// </summary>
    public bool NormalizeSubjects { get; set; } = true;

    /// <summary>
    /// Default stream configuration for auto-provisioning
    /// </summary>
    public JetStreamDefaults JetStreamDefaults { get; set; } = new();

    // === Multi-Tenancy Extension Points (optional) ===

    /// <summary>
    /// Optional: Custom tenant ID resolver for multi-tenancy scenarios
    /// </summary>
    public ITenantIdResolver? TenantIdResolver { get; set; }

    /// <summary>
    /// Optional: Custom subject resolver for tenant-aware routing
    /// </summary>
    public ISubjectResolver? SubjectResolver { get; set; }

    /// <summary>
    /// Subject prefix template for tenant isolation (e.g., "tenant.{tenantId}")
    /// </summary>
    public string? TenantSubjectPrefix { get; set; }

    /// <summary>
    /// Build NatsOpts from this configuration for creating NATS client
    /// </summary>
    internal NatsOpts ToNatsOpts()
    {
        return NatsOpts.Default with
        {
            Url = ConnectionString,
            Name = ClientName ?? "wolverine-nats",
            ConnectTimeout = ConnectTimeout,
            CommandTimeout = RequestTimeout,
            AuthOpts = new NatsAuthOpts
            {
                Username = Username,
                Password = Password,
                Token = Token,
                Jwt = Jwt,
                Seed = NKeySeed,
                CredsFile = CredentialsFile,
                NKeyFile = NKeyFile,
                AuthCredCallback = AuthCallback
            },
            TlsOpts = new NatsTlsOpts
            {
                Mode = TlsMode,
                InsecureSkipVerify = TlsInsecure,
                CertFile = ClientCertFile,
                KeyFile = ClientKeyFile,
                CaFile = CaFile
            }
        };
    }

    /// <summary>
    /// Build NatsJSOpts from this configuration for JetStream
    /// </summary>
    internal NatsJSOpts? ToJetStreamOpts()
    {
        if (!EnableJetStream)
            return null;

        // NatsJSOpts constructor requires NatsOpts
        // Domain and ApiPrefix are set via constructor parameters
        return new NatsJSOpts(ToNatsOpts(), JetStreamDomain, JetStreamApiPrefix ?? "$JS.API");
    }
}

/// <summary>
/// JetStream defaults for Wolverine transport
/// </summary>
public class JetStreamDefaults
{
    /// <summary>
    /// Default retention policy for streams (limits, workqueue)
    /// </summary>
    public string Retention { get; set; } = "limits"; // "limits" or "workqueue"

    /// <summary>
    /// Default maximum age for messages
    /// </summary>
    public TimeSpan? MaxAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Default maximum number of messages per stream
    /// </summary>
    public long? MaxMessages { get; set; } = 1_000_000;

    /// <summary>
    /// Default maximum size of stream in bytes
    /// </summary>
    public long? MaxBytes { get; set; } = 1024 * 1024 * 1024; // 1GB

    /// <summary>
    /// Default number of replicas for streams
    /// </summary>
    public int Replicas { get; set; } = 1;

    /// <summary>
    /// Default acknowledgment policy for consumers
    /// </summary>
    public string AckPolicy { get; set; } = "explicit"; // "explicit", "none", "all"

    /// <summary>
    /// Default acknowledgment wait time
    /// </summary>
    public TimeSpan AckWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default maximum delivery attempts
    /// </summary>
    public int MaxDeliver { get; set; } = 3;

    /// <summary>
    /// Default duplicate window for deduplication
    /// </summary>
    public TimeSpan DuplicateWindow { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Optional extension interface for tenant ID resolution
/// </summary>
public interface ITenantIdResolver
{
    /// <summary>
    /// Resolve tenant ID from envelope (Wolverine context)
    /// </summary>
    string? ResolveTenantId(Envelope envelope);
}

/// <summary>
/// Optional extension interface for custom subject resolution
/// </summary>
public interface ISubjectResolver
{
    /// <summary>
    /// Resolve subject for outgoing messages with tenant context
    /// </summary>
    string ResolveSubject(string baseSubject, Envelope envelope);

    /// <summary>
    /// Extract tenant context from incoming subject
    /// </summary>
    string? ExtractTenantId(string subject);
}
