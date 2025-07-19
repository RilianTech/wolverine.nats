# Security & Multi-Tenancy Guide

## Overview

NATS provides the most sophisticated multi-tenancy and security model of any message broker, with complete isolation at the infrastructure level rather than application level.

## Security Architecture

### Authentication Methods

#### 1. Token Authentication (Simple)
```csharp
opts.UseNats("nats://localhost:4222")
    .WithToken("s3cr3t-t0k3n");
```

#### 2. Username/Password (Basic)
```csharp
opts.UseNats("nats://localhost:4222")
    .WithCredentials("user", "password");
```

#### 3. NKey Authentication (Recommended)
```csharp
// Generate NKey pair
// nats-box: nk -gen user -pubout

opts.UseNats("nats://localhost:4222")
    .WithNKey("/path/to/user.nk");
```

#### 4. JWT Authentication (Enterprise)
```csharp
// Using .creds file (contains JWT + NKey)
opts.UseNats(config => {
    config.ConnectionString = "nats://connect.ngs.global";
    config.CredentialsFile = "/path/to/user.creds";
});
```

### TLS Configuration

#### Basic TLS
```csharp
opts.UseNats("nats://secure.example.com:4443")
    .UseTls();
```

#### Mutual TLS (mTLS)
```csharp
opts.UseNats(config => {
    config.ConnectionString = "nats://secure.example.com:4443";
    config.EnableTls = true;
    config.ClientCertFile = "/path/to/client-cert.pem";
    config.ClientKeyFile = "/path/to/client-key.pem";
    config.CaFile = "/path/to/ca.pem";
});
```

## Multi-Tenancy Patterns

### Phase 1: Subject-Based Multi-Tenancy

The simplest approach using subject prefixes:

```csharp
// Configure tenant prefix
public class NatsTenantSubjectResolver : ISubjectResolver
{
    public string ResolveSubject(string baseSubject, Envelope envelope)
    {
        var tenantId = envelope.TenantId ?? "default";
        return $"{tenantId}.{baseSubject}";
    }
    
    public string? ExtractTenantId(string subject)
    {
        var parts = subject.Split('.');
        return parts.Length > 1 ? parts[0] : null;
    }
}

// Register resolver
opts.UseNats(config => {
    config.SubjectResolver = new NatsTenantSubjectResolver();
});

// Usage - automatically prefixed with tenant ID
await bus.PublishAsync(new OrderCreated { ... });
// Published to: acme.orders.created
```

#### Subject Permissions per Tenant
```json
{
  "permissions": {
    "publish": {
      "allow": ["acme.>"]
    },
    "subscribe": {
      "allow": ["acme.>", "$JS.API.>"]
    }
  }
}
```

### Phase 2: Account-Based Multi-Tenancy

Complete isolation using NATS accounts:

#### Server Configuration
```yaml
# nats-server.conf
accounts: {
  # System account for administration
  SYS: {
    users: [{user: admin, password: admin123}]
  }
  
  # Tenant accounts
  ACME: {
    jetstream: enabled
    users: [
      {user: acme_app, password: "$2a$11$..."}
    ]
    limits: {
      subs: 1000
      payload: 1MB
      data: 10GB
      conn: 100
    }
  }
  
  GLOBEX: {
    jetstream: enabled  
    users: [
      {user: globex_app, password: "$2a$11$..."}
    ]
    limits: {
      subs: 1000
      payload: 1MB  
      data: 10GB
      conn: 100
    }
  }
}
```

#### Dynamic Tenant Connections
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
    
    private NatsOpts GetTenantConfig(string tenantId)
    {
        // Load from configuration
        return NatsOpts.Default with
        {
            Url = "nats://localhost:4222",
            Username = $"{tenantId}_app",
            Password = GetTenantPassword(tenantId)
        };
    }
}
```

### Phase 3: JWT-Based Multi-Tenancy

Enterprise-grade security with hierarchical trust:

#### Operator Configuration
```bash
# Create operator
nsc add operator MYCOMPANY

# Create account for tenant
nsc add account ACME
nsc edit account ACME \
  --js-mem-storage 1G \
  --js-disk-storage 10G \
  --conn 100 \
  --leaf 10

# Create user
nsc add user --account ACME acme_app

# Generate creds file
nsc generate creds -a ACME -n acme_app > acme.creds
```

#### Dynamic JWT Provisioning
```csharp
public class JwtTenantService
{
    private readonly string _operatorSeed;
    
    public async Task<string> CreateTenantAccount(TenantInfo tenant)
    {
        // Create account JWT
        var accountClaims = new AccountClaims
        {
            Subject = tenant.Id,
            Name = tenant.Name,
            Limits = new OperatorLimits
            {
                Conn = tenant.MaxConnections,
                Subs = tenant.MaxSubscriptions,
                Payload = tenant.MaxPayloadSize,
                Data = tenant.MaxDataSize
            },
            JetStream = new JetStreamLimits
            {
                MemoryStorage = tenant.MemoryQuota,
                DiskStorage = tenant.DiskQuota,
                Streams = tenant.MaxStreams,
                Consumer = tenant.MaxConsumers
            }
        };
        
        return SignJwt(accountClaims, _operatorSeed);
    }
}
```

## Authorization Patterns

### Fine-Grained Permissions

#### Subject-Level Access Control
```json
{
  "permissions": {
    "publish": {
      "allow": [
        "orders.create",
        "orders.update.own.>",
        "inventory.request"
      ],
      "deny": [
        "orders.delete",
        "admin.>"
      ]
    },
    "subscribe": {
      "allow": [
        "orders.>",
        "notifications.user.{{user-id}}"
      ]
    }
  }
}
```

#### Response Permissions
```json
{
  "permissions": {
    "publish": {
      "allow": ["services.>"]
    },
    "subscribe": {
      "allow": ["_INBOX.>"]
    },
    "response": {
      "max": 1,
      "ttl": "5s"
    }
  }
}
```

## Security Best Practices

### 1. Connection Security
- Always use TLS in production
- Rotate credentials regularly
- Use NKeys or JWT, not passwords
- Enable connection authentication

### 2. Subject Design for Security
```
# Good - tenant isolation clear
acme.orders.created
acme.users.updated
globex.orders.created

# Bad - no tenant boundary  
orders.created
users.updated
```

### 3. Account Limits
```yaml
limits: {
  # Connection limits
  conn: 100           # max connections
  leaf: 10            # max leaf nodes
  
  # Message limits
  subs: 1000          # max subscriptions
  payload: 1MB        # max message size
  
  # Rate limits
  mpub: 1000          # msgs/sec publish
  msub: 5000          # msgs/sec subscribe
  
  # Storage limits
  data: 10GB          # total storage
  streams: 100        # max streams
}
```

### 4. Monitoring Security Events
```csharp
// Subscribe to system events
opts.ListenToNatsSubject("$SYS.SERVER.*.CLIENT.>")
    .UseQueueGroup("security-monitor")
    .ProcessInline();

public class SecurityEventHandler
{
    public async Task Handle(ClientConnect evt)
    {
        if (evt.Reason == "authentication_failed")
        {
            await LogSecurityEvent(evt);
            await NotifySecurityTeam(evt);
        }
    }
}
```

## Import/Export for Controlled Sharing

### Service Sharing Pattern
```yaml
# Shared services account
accounts:
  SHARED:
    exports:
      - service: auth.verify
        accounts: [ACME, GLOBEX]
        response_type: single
      
      - stream: audit.>
        accounts: [AUDIT_SYSTEM]

# Tenant account
  ACME:
    imports:
      - service: 
          account: SHARED
          subject: auth.verify
        to: auth.check
      
      - stream:
          account: SHARED  
          subject: audit.>
        to: events.audit
```

### Event Bus Pattern
```yaml
# Central event bus
accounts:
  EVENT_BUS:
    exports:
      - stream: events.>
        accounts: ["*"]  # All accounts
    
    imports:
      - stream: "*.events.>"
        account: "*"
        to: events.$1
```

## Compliance & Auditing

### Audit Logging
```csharp
// Capture all messages for compliance
opts.ListenToNatsSubject(">")
    .UseJetStream("AUDIT", config => {
        config.Retention = RetentionPolicy.Limits;
        config.MaxAge = TimeSpan.FromDays(2555); // 7 years
        config.Compression = StoreCompression.S2;
    })
    .Transform(msg => new AuditEvent
    {
        TenantId = ExtractTenantId(msg.Subject),
        Subject = msg.Subject,
        Payload = msg.Data,
        Headers = msg.Headers,
        Timestamp = DateTimeOffset.UtcNow
    });
```

### Data Residency
```yaml
# Regional accounts
accounts:
  ACME_EU:
    jetstream: {placement: {region: eu-west}}
  
  ACME_US:
    jetstream: {placement: {region: us-east}}
```

## Migration Guide

### From Single to Multi-Tenant

1. **Start with subject prefixes** (Phase 1)
2. **Add authentication** (users/passwords)
3. **Implement permissions** (subject ACLs)
4. **Move to accounts** when needed (Phase 2)
5. **Adopt JWT** for enterprise scale (Phase 3)

Each phase builds on the previous one, allowing gradual adoption of security features as your application grows.

## Key Takeaways

1. **NATS security is hierarchical** - Operator → Account → User
2. **Accounts provide hard boundaries** - Not just application logic
3. **Subject design is critical** - Plan namespaces early
4. **Start simple, evolve as needed** - Subject prefixes → Accounts → JWT
5. **Monitor everything** - Security events, failed auth, rate limits

NATS's security model enables true multi-tenant architectures without the complexity of managing separate infrastructure per tenant.