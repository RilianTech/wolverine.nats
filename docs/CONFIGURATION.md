# Wolverine.Nats Configuration Guide

This guide consolidates all configuration patterns for Wolverine.Nats, from basic setup to advanced enterprise scenarios.

## Quick Start Configuration

### Basic Setup
```csharp
builder.Host.UseWolverine(opts =>
{
    // Simple connection
    opts.UseNats("nats://localhost:4222");
    
    // Listen to messages
    opts.ListenToNatsSubject("greetings");
});
```

### Connection Strings
```csharp
// Local development
opts.UseNats("nats://localhost:4222");

// With credentials
opts.UseNats("nats://user:pass@localhost:4222");

// Multiple servers
opts.UseNats("nats://server1:4222,server2:4222,server3:4222");

// Secure connection
opts.UseNats("tls://secure.example.com:4443");
```

## Authentication Configuration

### Token Authentication
```csharp
opts.UseNats("nats://localhost:4222")
    .WithToken("s3cr3t-t0k3n");
```

### Username/Password
```csharp
opts.UseNats("nats://localhost:4222")
    .WithCredentials("username", "password");
```

### NKey Authentication (Recommended)
```csharp
opts.UseNats("nats://localhost:4222")
    .WithNKey("/path/to/user.nk");
```

### JWT with Credentials File (Enterprise)
```csharp
opts.UseNats(config => {
    config.ConnectionString = "nats://connect.ngs.global";
    config.CredentialsFile = "/path/to/user.creds";
});
```

## TLS Configuration

### Basic TLS
```csharp
opts.UseNats("nats://secure.example.com:4443")
    .UseTls();
```

### Mutual TLS (mTLS)
```csharp
opts.UseNats(config => {
    config.ConnectionString = "nats://secure.example.com:4443";
    config.EnableTls = true;
    config.ClientCertFile = "/path/to/client-cert.pem";
    config.ClientKeyFile = "/path/to/client-key.pem";
    config.CaFile = "/path/to/ca.pem";
    config.TlsInsecure = false; // Verify server certificate
});
```

### TLS with Certificate Objects
```csharp
opts.UseNats(config => {
    config.EnableTls = true;
    config.ClientCertificate = clientCert;
    config.TrustedCertificates = new[] { caCert };
});
```

## JetStream Configuration

### Basic JetStream Setup
```csharp
opts.UseNats("nats://localhost:4222")
    .AutoProvision()  // Auto-create streams
    .UseJetStream(js => {
        js.MaxMessages = 100_000;
        js.MaxAge = TimeSpan.FromDays(7);
        js.MaxBytes = 1024L * 1024 * 1024; // 1GB
    });
```

### Stream Configuration Helpers

#### Define Streams Explicitly
```csharp
opts.UseNats("nats://localhost:4222")
    .DefineStream("ORDERS", stream => {
        stream.WithSubjects("orders.>", "payment.>", "inventory.>")
              .WithLimits(
                  maxMessages: 1_000_000,
                  maxBytes: 1024L * 1024 * 1024, // 1GB
                  maxAge: TimeSpan.FromDays(30)
              )
              .WithReplicas(3); // For production HA
    });
```

#### Work Queue Streams
```csharp
opts.UseNats("nats://localhost:4222")
    .DefineWorkQueueStream("TASKS", "tasks.>", "jobs.>");
```

#### Log Streams (Event Sourcing)
```csharp
opts.UseNats("nats://localhost:4222")
    .DefineLogStream("AUDIT_LOG", 
        retention: TimeSpan.FromDays(90),
        subjects: "audit.>", "security.>");
```

#### High Availability Streams
```csharp
opts.UseNats("nats://localhost:4222")
    .DefineReplicatedStream("CRITICAL_EVENTS", 
        replicas: 3,
        subjects: "payments.>", "orders.>");
```

### Consumer Configuration
```csharp
// Durable consumer with specific settings
opts.ListenToNatsSubject("orders.created")
    .UseJetStream("ORDERS", "order-processor") // Stream, Consumer name
    .UseQueueGroup("processors")
    .MaximumParallelMessages(10)
    .ConfigureDeadLetterQueue(3, "orders.dlq");

// Pull consumer with batching
opts.ListenToNatsSubject("bulk.process")
    .UseJetStream("BULK", config => {
        config.MaxAckPending = 1000;
        config.MaxBatch = 100;
        config.MaxExpires = TimeSpan.FromSeconds(30);
    })
    .BufferedInMemory(5000);
```

## Subject and Endpoint Configuration

### Publishing Configuration
```csharp
// Static subject
opts.PublishMessage<OrderCreated>()
    .ToNatsSubject("orders.created");

// Dynamic subject with properties
opts.PublishMessage<OrderCreated>()
    .ToNatsSubject("orders.{Region}.created");

// Use JetStream for publishing
opts.PublishMessage<CriticalOrder>()
    .ToNatsSubject("orders.critical")
    .UseJetStream("ORDERS");
```

### Listening Configuration
```csharp
// Basic listener
opts.ListenToNatsSubject("orders.created");

// With queue group for load balancing
opts.ListenToNatsSubject("orders.created")
    .UseQueueGroup("order-processors");

// Wildcard subscriptions
opts.ListenToNatsSubject("orders.>")        // All order events
    .ProcessInline();

opts.ListenToNatsSubject("orders.*.created") // Created events all regions
    .UseQueueGroup("order-handlers");

// JetStream listener
opts.ListenToNatsSubject("orders.created")
    .UseJetStream("ORDERS", "order-service")
    .MaximumParallelMessages(5);
```

### Global Listener Configuration
```csharp
opts.UseNats("nats://localhost:4222")
    .ConfigureListeners(listener => {
        listener.UseQueueGroup("my-service");
        listener.ProcessInline();
        listener.MaximumParallelMessages(10);
    });
```

### Global Sender Configuration
```csharp
opts.UseNats("nats://localhost:4222")
    .ConfigureSenders(sender => {
        sender.UseJetStream(); // Use JetStream for all publishing
        sender.RequestTimeout = TimeSpan.FromSeconds(30);
    });
```

## Connection Configuration

### Connection Options
```csharp
opts.UseNats(config => {
    config.ConnectionString = "nats://localhost:4222";
    config.Name = "MyWolverineApp";
    config.ConnectTimeout = TimeSpan.FromSeconds(10);
    config.ReconnectWait = TimeSpan.FromSeconds(2);
    config.MaxReconnectAttempts = 60;
    config.PingInterval = TimeSpan.FromMinutes(2);
    config.MaxPingsOut = 2;
    config.LoggerFactory = loggerFactory;
});
```

### Buffer and Performance Settings
```csharp
opts.UseNats(config => {
    config.SubPendingBytesLimit = 1024 * 1024; // 1MB
    config.SubPendingMsgsLimit = 10000;
    config.WriterBufferSize = 32768; // 32KB
    config.ReaderBufferSize = 32768; // 32KB
});
```

## Multi-Tenancy Configuration

### Subject-Based Multi-Tenancy
```csharp
public class NatsTenantSubjectResolver : ISubjectResolver
{
    public string ResolveSubject(string baseSubject, Envelope envelope)
    {
        var tenantId = envelope.TenantId ?? "default";
        return $"{tenantId}.{baseSubject}";
    }
}

opts.UseNats(config => {
    config.SubjectResolver = new NatsTenantSubjectResolver();
});
```

### Account-Based Multi-Tenancy
```csharp
public class NatsTenantConnectionFactory
{
    private readonly Dictionary<string, NatsConnection> _connections = new();
    
    public async Task<NatsConnection> GetConnection(string tenantId)
    {
        if (!_connections.TryGetValue(tenantId, out var connection))
        {
            var config = GetTenantConfig(tenantId);
            connection = new NatsConnection(config);
            await connection.ConnectAsync();
            _connections[tenantId] = connection;
        }
        return connection;
    }
}
```

## Error Handling Configuration

### Dead Letter Queue
```csharp
opts.ListenToNatsSubject("orders.process")
    .UseJetStream("ORDERS")
    .ConfigureDeadLetterQueue(
        maxDeliveryAttempts: 3,
        deadLetterSubject: "orders.failed"
    );
```

### Circuit Breaker
```csharp
opts.ListenToNatsSubject("external.api.calls")
    .CircuitBreaker(cb => {
        cb.PauseTime = TimeSpan.FromMinutes(1);
        cb.FailureThreshold = 10;
        cb.SuccessThreshold = 5;
    });
```

### Retry Configuration
```csharp
opts.UseNats(config => {
    config.RetryPolicy = RetryPolicy.Exponential(
        initialDelay: TimeSpan.FromSeconds(1),
        maxDelay: TimeSpan.FromMinutes(5),
        maxAttempts: 10
    );
});
```

## IoT and MQTT Gateway Configuration

### Server-Side MQTT Gateway
```yaml
# nats-server.conf
mqtt {
  port: 1883
  
  tls {
    cert_file: "/path/to/mqtt-cert.pem"
    key_file: "/path/to/mqtt-key.pem"
  }
  
  authorization {
    users = [
      {
        user: "mqtt-device"
        password: "device-secret"
        permissions: {
          publish: ["devices.sensor.>"]
          subscribe: ["devices.commands.>"]
        }
      }
    ]
  }
  
  max_clients: 10000
  max_pending_size: 10MB
}
```

### Wolverine MQTT Integration
```csharp
// Handle MQTT device messages (topics converted to subjects)
// MQTT: sensors/temperature/room1 → NATS: sensors.temperature.room1
opts.ListenToNatsSubject("sensors.temperature.*")
    .UseQueueGroup("telemetry-processors")
    .ProcessInline();

// Send commands to MQTT devices
// NATS: devices.commands.ABC123 → MQTT: devices/commands/ABC123
opts.PublishMessage<DeviceCommand>()
    .ToNatsSubject("devices.commands.{DeviceId}");
```

## Performance Configuration

### High-Throughput Settings
```csharp
opts.UseNats(config => {
    config.UseThreadPoolCallback = false; // Use dedicated threads
    config.SubPendingBytesLimit = 10 * 1024 * 1024; // 10MB
    config.SubPendingMsgsLimit = 100000;
    config.WriterBufferSize = 65536; // 64KB
});

opts.ListenToNatsSubject("high.volume.topic")
    .BufferedInMemory(capacity: 10000)
    .MaximumParallelMessages(50)
    .ProcessInline();
```

### Memory-Optimized Settings
```csharp
opts.UseNats(config => {
    config.ObjectPoolSize = 1000;
    config.UseCompression = true;
    config.EnableHeadersOnly = true; // For routing decisions
});
```

## Serialization Configuration

### Custom Serializer
```csharp
opts.UseNats(config => {
    config.DefaultSerializer = new MessagePackSerializer();
    // or
    config.DefaultSerializer = new ProtobufSerializer();
});
```

### Per-Message Type Serialization
```csharp
opts.PublishMessage<BinaryTelemetry>()
    .ToNatsSubject("sensors.binary")
    .CustomizeOutgoing(env => {
        env.Serializer = new MessagePackSerializer();
    });
```

## Development vs Production Configuration

### Development
```csharp
if (builder.Environment.IsDevelopment())
{
    opts.UseNats("nats://localhost:4222")
        .AutoProvision()
        .UseJetStream(js => {
            js.MaxAge = TimeSpan.FromHours(1); // Short retention
            js.MaxMessages = 1000;
        });
}
```

### Production
```csharp
if (builder.Environment.IsProduction())
{
    opts.UseNats(config => {
        config.ConnectionString = builder.Configuration.GetConnectionString("NATS");
        config.CredentialsFile = "/app/secrets/nats.creds";
        config.EnableTls = true;
        config.Name = Environment.MachineName;
    })
    .DefineStream("ORDERS", stream => {
        stream.WithSubjects("orders.>")
              .WithLimits(maxMessages: 10_000_000)
              .WithReplicas(3)
              .Storage = StreamConfigurationStorageType.File;
    });
}
```

## Configuration Validation

### Startup Validation
```csharp
opts.UseNats("nats://localhost:4222")
    .ValidateConnection() // Ensure NATS is reachable
    .ValidateStreams()    // Ensure all streams exist
    .ValidatePermissions(); // Check subject permissions
```

### Health Checks
```csharp
builder.Services.AddHealthChecks()
    .AddNats(options => {
        options.ConnectionString = "nats://localhost:4222";
        options.Timeout = TimeSpan.FromSeconds(5);
    });
```

## Environment-Specific Configuration

### Using Configuration Files
```json
{
  "NATS": {
    "ConnectionString": "nats://prod-cluster:4222",
    "CredentialsFile": "/app/secrets/prod.creds",
    "JetStream": {
      "MaxMessages": 10000000,
      "MaxAge": "30d",
      "Replicas": 3
    }
  }
}
```

```csharp
var natsConfig = builder.Configuration.GetSection("NATS");
opts.UseNats(config => {
    config.ConnectionString = natsConfig["ConnectionString"];
    config.CredentialsFile = natsConfig["CredentialsFile"];
})
.UseJetStream(js => {
    js.MaxMessages = natsConfig.GetValue<int>("JetStream:MaxMessages");
    js.MaxAge = TimeSpan.Parse(natsConfig["JetStream:MaxAge"]);
});
```

## Best Practices

1. **Use TLS in production** - Never send credentials in plain text
2. **Plan subject hierarchies early** - Avoid subject conflicts between streams
3. **Use JetStream for critical messages** - Core NATS for ephemeral data
4. **Configure appropriate timeouts** - Match your application's SLA requirements
5. **Monitor connection health** - Implement health checks and reconnection logic
6. **Use queue groups for scaling** - Enable horizontal scaling of message processing
7. **Set appropriate buffer sizes** - Balance memory usage with performance
8. **Use durable consumers** - For services that need to survive restarts
9. **Configure dead letter queues** - Handle poison messages gracefully
10. **Test configurations** - Validate settings in development environments

This configuration guide covers all aspects of setting up Wolverine.Nats from simple development scenarios to complex enterprise deployments. Refer to specific sections based on your current needs and gradually adopt more advanced patterns as your application grows.