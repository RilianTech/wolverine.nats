namespace Wolverine.Nats.Configuration;

public class NatsTransportConfiguration
{
    /// <summary>
    /// NATS server connection string (default: nats://localhost:4222)
    /// </summary>
    public string ConnectionString { get; set; } = "nats://localhost:4222";

    /// <summary>
    /// Connection timeout
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Request timeout for request/reply patterns
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Username for authentication
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for authentication
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Token for authentication
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// NKey file path for authentication
    /// </summary>
    public string? NKeyFile { get; set; }

    /// <summary>
    /// JWT for authentication
    /// </summary>
    public string? Jwt { get; set; }

    /// <summary>
    /// NKey seed for JWT authentication
    /// </summary>
    public string? NKeySeed { get; set; }

    /// <summary>
    /// Enable TLS
    /// </summary>
    public bool EnableTls { get; set; }

    /// <summary>
    /// Skip TLS certificate verification (for development only)
    /// </summary>
    public bool TlsInsecure { get; set; }

    /// <summary>
    /// Client certificate path for mutual TLS
    /// </summary>
    public string? ClientCertPath { get; set; }

    /// <summary>
    /// Client certificate key path for mutual TLS
    /// </summary>
    public string? ClientKeyPath { get; set; }

    /// <summary>
    /// Whether to automatically provision JetStream resources (streams, consumers)
    /// </summary>
    public bool AutoProvision { get; set; }

    /// <summary>
    /// Whether to automatically purge streams on startup (for development)
    /// </summary>
    public bool AutoPurgeOnStartup { get; set; }
    
    /// <summary>
    /// Whether to enable JetStream support
    /// </summary>
    public bool EnableJetStream { get; set; } = true;
    
    /// <summary>
    /// JetStream domain to connect to (optional)
    /// </summary>
    public string? JetStreamDomain { get; set; }

    /// <summary>
    /// Default stream configuration for auto-provisioning
    /// </summary>
    public JetStreamConfiguration JetStream { get; set; } = new();
}

public class JetStreamConfiguration
{
    /// <summary>
    /// Default retention policy for streams
    /// </summary>
    public string Retention { get; set; } = "limits";

    /// <summary>
    /// Default maximum age for messages
    /// </summary>
    public TimeSpan? MaxAge { get; set; }

    /// <summary>
    /// Default maximum number of messages per stream
    /// </summary>
    public long? MaxMessages { get; set; }

    /// <summary>
    /// Default maximum size of stream in bytes
    /// </summary>
    public long? MaxBytes { get; set; }

    /// <summary>
    /// Default number of replicas for streams
    /// </summary>
    public int Replicas { get; set; } = 1;

    /// <summary>
    /// Default acknowledgment policy for consumers
    /// </summary>
    public string AckPolicy { get; set; } = "explicit";

    /// <summary>
    /// Default acknowledgment wait time
    /// </summary>
    public TimeSpan AckWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default maximum delivery attempts
    /// </summary>
    public int MaxDeliver { get; set; } = 3;
}