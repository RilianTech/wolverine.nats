# NATS Security and Multi-Tenancy Guide for Wolverine

This guide explains how to implement secure, multi-tenant messaging using NATS' **native multi-tenancy capabilities**. 

> **Key Insight**: NATS provides the most sophisticated native multi-tenancy of any message broker. Our Wolverine transport leverages these built-in capabilities rather than reimplementing multi-tenancy from scratch.
>
> For a comprehensive analysis of NATS native multi-tenancy features, see [NATS_NATIVE_MULTITENANCY_ANALYSIS.md](NATS_NATIVE_MULTITENANCY_ANALYSIS.md).

## Multi-Tenancy Implementation Strategy

### Recommended Approach: Start Simple, Scale to Enterprise

**Phase 1: Subject-Based Multi-Tenancy** (Current - Simple & Effective)
- Use NATS subject hierarchy with tenant ID in subject path
- Leverage NATS subject mapping for automatic tenant routing
- Single connection, works with existing authentication
- **Implementation Status**: Ready for Phase 1

**Phase 2: Account-Based Multi-Tenancy** (Future - Maximum Security)  
- Complete namespace isolation per tenant via NATS accounts
- Separate connections and JetStream storage per tenant
- Enterprise-grade security with zero cross-tenant leakage

**Phase 3: Auth Callout Integration** (Enterprise - Dynamic)
- External IAM system integration (LDAP, OAuth, SAML)
- Dynamic tenant provisioning and credential management
- Real-time authentication via NATS auth callout service

## NATS Account System Overview

NATS provides true multi-tenancy through its account system:

```
┌─────────────────┐
│    Operator     │  Root of trust
└────────┬────────┘
         │
    ┌────┴────┬──────────┬───────────┐
    │         │          │           │
┌───▼───┐ ┌──▼───┐ ┌────▼────┐ ┌───▼───┐
│Tenant A│ │Tenant B│ │Services │ │Admin  │
│Account │ │Account │ │Account  │ │Account│
└───┬───┘ └──┬───┘ └────┬────┘ └───┬───┘
    │        │           │          │
  Users    Users    System Svcs   Admins
```

Each account provides:
- **Complete subject namespace isolation**
- **Independent connection limits**
- **Separate JetStream storage**
- **No cross-account communication by default**

## JWT-Based Authentication

### JWT Hierarchy

1. **Operator JWT**: Signs account JWTs
2. **Account JWT**: Signs user JWTs, defines account limits
3. **User JWT**: Actual connection credentials

### Implementation Pattern

```csharp
public class NatsSecurityProvider
{
    private readonly string _operatorJwt;
    private readonly NKey _operatorSigningKey;
    
    public async Task<AccountCredentials> CreateTenantAccount(
        string tenantId,
        AccountLimits limits)
    {
        // Create account signing key
        var accountKey = NKey.CreateAccount();
        
        // Create account JWT
        var accountClaims = new AccountClaims
        {
            Subject = accountKey.PublicKey,
            Name = $"tenant-{tenantId}",
            
            // Account limits
            Limits = new OperatorLimits
            {
                // Connection limits
                Conn = limits.MaxConnections ?? 100,
                LeafNodeConn = 0,  // No leaf nodes
                
                // Data limits
                Data = limits.MaxDataPerMonth ?? 10_737_418_240,  // 10GB
                Payload = limits.MaxPayloadSize ?? 1_048_576,     // 1MB
                
                // Subscription limits
                Subs = limits.MaxSubscriptions ?? 1000,
                
                // JetStream limits
                MemoryStorage = limits.MaxMemoryStorage ?? 1_073_741_824,  // 1GB
                DiskStorage = limits.MaxDiskStorage ?? 10_737_418_240,     // 10GB
                Streams = limits.MaxStreams ?? 10,
                Consumer = limits.MaxConsumers ?? 100
            },
            
            // Default permissions
            DefaultPermissions = new Permissions
            {
                Pub = new Permission { Allow = new[] { ">" } },
                Sub = new Permission { Allow = new[] { ">" } }
            }
        };
        
        var accountJwt = EncodeJWT(accountClaims, _operatorSigningKey);
        
        return new AccountCredentials
        {
            AccountId = accountKey.PublicKey,
            AccountJWT = accountJwt,
            AccountSigningKey = accountKey
        };
    }
    
    public async Task<UserCredentials> CreateUser(
        string tenantId,
        string userId,
        UserPermissions permissions,
        NKey accountSigningKey)
    {
        // Create user signing key
        var userKey = NKey.CreateUser();
        
        // Create user JWT
        var userClaims = new UserClaims
        {
            Subject = userKey.PublicKey,
            Name = $"{tenantId}-{userId}",
            
            // User permissions
            Permissions = new Permissions
            {
                Pub = new Permission 
                { 
                    Allow = permissions.PublishSubjects,
                    Deny = permissions.DenyPublishSubjects
                },
                Sub = new Permission
                {
                    Allow = permissions.SubscribeSubjects,
                    Deny = permissions.DenySubscribeSubjects
                },
                
                // Response permissions for request/reply
                Resp = new ResponsePermission
                {
                    MaxMsgs = 1,
                    Expires = TimeSpan.FromMinutes(5)
                }
            },
            
            // Connection limits
            Limits = new UserLimits
            {
                Data = permissions.MaxDataPerDay ?? 1_073_741_824,  // 1GB/day
                Payload = permissions.MaxPayloadSize ?? 1_048_576,   // 1MB
                Subs = permissions.MaxSubscriptions ?? 100
            },
            
            // Time restrictions
            Times = permissions.AllowedTimes,
            Locale = permissions.AllowedLocale
        };
        
        var userJwt = EncodeJWT(userClaims, accountSigningKey);
        
        return new UserCredentials
        {
            JWT = userJwt,
            Seed = userKey.Seed
        };
    }
}
```

## Wolverine Transport Multi-Tenancy Patterns

### Phase 1: Subject-Based Multi-Tenancy (Current Implementation)

The simplest and most effective approach leverages NATS subject hierarchy:

```csharp
public class NatsSubjectBasedMultiTenancy
{
    public static void ConfigureWolverine(WolverineOptions opts)
    {
        opts.UseNats("nats://localhost:4222");
        
        // Publisher with tenant-aware subject resolution
        opts.PublishMessage<OrderCreated>()
            .ToNatsSubject("orders.created")  // Becomes "tenant.{id}.orders.created"
            .UseTenantIdFromHeader();
            
        // Listener for tenant-specific subjects
        opts.ListenToNatsSubject("tenant.*.orders.created")
            .TenantAware()  // Extracts tenant from subject
            .UseQueueGroup("order-processors");
    }
}

// NATS server subject mapping configuration
mappings: {
    # Automatic tenant prefixing
    "orders.*": "tenant.{{header('tenant-id')}}.orders.{{wildcard(1)}}"
    
    # Or deterministic partitioning by tenant
    "events.*": "tenant.{{partition(10,1)}}.events.{{wildcard(1)}}"
}
```

**Benefits of Subject-Based Approach:**
- ✅ Simple to implement with existing transport
- ✅ Works with single NATS connection
- ✅ Leverages NATS native subject mapping
- ✅ Tenant isolation via subject namespace
- ✅ Compatible with existing authentication

### Phase 2: Account-Based Multi-Tenancy (Future)

```csharp
public class MultiTenantNatsTransport : ITransport
{
    private readonly ConcurrentDictionary<string, TenantConnection> _connections = new();
    private readonly INatsSecurityProvider _security;
    
    public async Task<INatsConnection> GetTenantConnection(string tenantId)
    {
        return await _connections.GetOrAddAsync(tenantId, 
            async (id) => await CreateTenantConnection(id));
    }
    
    private async Task<TenantConnection> CreateTenantConnection(string tenantId)
    {
        // Get or create tenant credentials
        var creds = await _security.GetTenantCredentials(tenantId);
        
        // Create NATS options with tenant auth
        var opts = NatsOpts.Default with
        {
            Name = $"wolverine-{tenantId}",
            
            // JWT authentication
            AuthOpts = new NatsAuthOpts
            {
                JWT = creds.JWT,
                Seed = creds.Seed
            },
            
            // TLS required for production
            TlsOpts = new NatsTlsOpts
            {
                Mode = TlsMode.Require
            },
            
            // Connection callbacks
            ConnectionOpenedCallback = async (conn, args) =>
            {
                _logger.LogInformation(
                    "Tenant {TenantId} connected to NATS",
                    tenantId);
            },
            
            ConnectionClosedCallback = async (conn, args) =>
            {
                _logger.LogWarning(
                    "Tenant {TenantId} disconnected from NATS: {Error}",
                    tenantId, args.Error);
            }
        };
        
        var connection = new NatsConnection(opts);
        await connection.ConnectAsync();
        
        // Create JetStream context
        var js = connection.CreateJetStreamContext();
        
        // Setup tenant streams
        await SetupTenantStreams(js, tenantId);
        
        return new TenantConnection
        {
            TenantId = tenantId,
            Connection = connection,
            JetStream = js
        };
    }
    
    private async Task SetupTenantStreams(
        INatsJSContext js,
        string tenantId)
    {
        // Commands stream
        await js.CreateStreamAsync(new StreamConfig
        {
            Name = $"{tenantId}-commands",
            Subjects = new[] { "cmd.>" },  // Within tenant's namespace
            Retention = StreamConfigRetention.WorkQueue,
            MaxAge = TimeSpan.FromHours(1),
            MaxMsgs = 100_000
        });
        
        // Events stream
        await js.CreateStreamAsync(new StreamConfig
        {
            Name = $"{tenantId}-events",
            Subjects = new[] { "evt.>" },
            Retention = StreamConfigRetention.Limits,
            MaxAge = TimeSpan.FromDays(7),
            MaxMsgs = 1_000_000
        });
    }
}
```

### Phase 3: Auth Callout Integration (Enterprise)

> **Important**: Auth callout runs as a **separate sidecar service**, not within our Wolverine transport. Reference implementation: [synadia-io/callout.go](https://github.com/synadia-io/callout.go)

**Deployment Architecture**:
```
Wolverine App → NATS Server (auth_callout enabled) → Auth Callout Sidecar
```

**Auth Callout Sidecar Service** (separate microservice):
```go
// External service using synadia-io/callout.go library
func main() {
    // Connect as privileged auth user
    nc, _ := nats.Connect("nats://localhost:4222", nats.UserInfo("auth", "pwd"))
    
    // Tenant-aware authorization function
    authorizer := func(req *jwt.AuthorizationRequest) (string, error) {
        tenantId := extractTenantFromRequest(req)
        
        // Validate with your IAM (LDAP, OAuth, database)
        if !iamService.ValidateUser(req.UserNkey, tenantId) {
            return "", errors.New("unauthorized")
        }
        
        // Create tenant-scoped JWT
        uc := jwt.NewUserClaims(req.UserNkey)
        uc.Audience = fmt.Sprintf("TENANT_%s", strings.ToUpper(tenantId))
        uc.Sub.Allow.Add(fmt.Sprintf("tenant.%s.>", tenantId))
        uc.Expires = time.Now().Unix() + 3600
        
        return uc.Encode(accountSigningKey)
    }
    
    // Start the auth service
    svc, _ := callout.NewAuthorizationService(
        nc, callout.Authorizer(authorizer), callout.ResponseSignerKey(key))
    svc.Start()
}
```

**Wolverine Transport Configuration** (simple JWT + NKey pattern):
```csharp
public static void ConfigureWolverine(WolverineOptions opts, IConfiguration config)
{
    // Load tenant credentials from auth callout system (config, vault, etc.)
    var natsConfig = config.GetSection("Nats").Get<NatsOptions>();
    
    var natsOpts = NatsOpts.Default with
    {
        Name = natsConfig.Name,  // e.g., "wolverine-tenant-acme"
        AuthOpts = new NatsAuthOpts
        {
            Jwt = natsConfig.Jwt,           // Tenant-scoped JWT (from auth callout)
            Seed = natsConfig.NKeySeed,     // User's NKey seed  
            Token = natsConfig.ServiceToken // Optional: backend service token
        },
        TlsOpts = new NatsTlsOpts { Mode = TlsMode.Auto }
    };
    
    opts.UseNats(natsOpts);
    
    // NATS enforces tenant permissions automatically!
    // No manual tenant validation needed in our transport
}

// Configuration model (following proven pattern)
public class NatsOptions
{
    public string Url { get; set; } = "nats://localhost:4222";
    public string? Jwt { get; set; }           // From auth callout
    public string? NKeySeed { get; set; }      // User private key
    public string? ServiceToken { get; set; }  // Backend access
    public string Name { get; set; } = "wolverine-nats";
}
```

## Implementation Comparison

| Approach | Security | Complexity | Performance | Use Case |
|----------|----------|------------|-------------|----------|
| **Subject-Based** | Good | Low | Excellent | Most applications |
| **Account-Based** | Maximum | Medium | Excellent | Enterprise/Regulated |
| **Auth Callout** | Maximum | Low (sidecar) | Good | Dynamic/External IAM |

### Legacy Pattern: Subject-Based Isolation

```csharp
public class SubjectIsolatedTransport : NatsTransport
{
    protected override string GetSubjectForEndpoint(
        NatsEndpoint endpoint,
        string? tenantId)
    {
        if (string.IsNullOrEmpty(tenantId))
            return endpoint.Subject;
            
        // Prefix with tenant ID
        return $"tenant.{tenantId}.{endpoint.Subject}";
    }
    
    public override async ValueTask<IListener> BuildListenerAsync(
        IWolverineRuntime runtime,
        IReceiver receiver,
        string? tenantId)
    {
        var endpoint = GetEndpoint(receiver.Address);
        var subject = GetSubjectForEndpoint(endpoint, tenantId);
        
        // Create consumer with tenant-specific subject
        var consumer = await CreateConsumer(
            endpoint.StreamName,
            subject,
            $"{endpoint.ConsumerName}-{tenantId}");
            
        return new TenantAwareListener(
            endpoint,
            consumer,
            receiver,
            tenantId);
    }
}
```

## Cross-Account Communication

### Service Account Pattern

```csharp
public class ServiceAccountSetup
{
    public async Task SetupServiceAccount(
        INatsSecurityProvider security)
    {
        // Create service account
        var serviceAccount = await security.CreateAccount(
            "services",
            new AccountLimits { /* unlimited */ });
            
        // Export service subjects
        await serviceAccount.AddExport(new Export
        {
            Name = "Order Service",
            Subject = "svc.orders.>",
            Type = ExportType.Service,
            
            // Require authorization
            TokenReq = true
        });
        
        // Generate import tokens for tenants
        var token = await serviceAccount.GenerateActivationToken(
            "svc.orders.>",
            importingAccountId: tenantAccountId);
    }
    
    public async Task ImportServiceInTenant(
        string tenantId,
        string activationToken)
    {
        var tenantAccount = await GetTenantAccount(tenantId);
        
        // Import service with local mapping
        await tenantAccount.AddImport(new Import
        {
            Name = "Order Service",
            Subject = "orders.>",  // Local subject
            Account = serviceAccountId,
            
            // Maps to service account's subject
            To = "svc.orders.>",
            
            // Authorization token
            Token = activationToken
        });
    }
}
```

## Security Best Practices

### 1. Subject Naming Conventions

```csharp
public static class SubjectConventions
{
    // Hierarchical structure
    public static string BuildSubject(params string[] parts)
    {
        // tenant.{id}.{domain}.{entity}.{action}
        return string.Join(".", parts.Select(Sanitize));
    }
    
    private static string Sanitize(string part)
    {
        // Remove invalid characters
        return Regex.Replace(part, @"[^a-zA-Z0-9_-]", "_")
            .ToLowerInvariant();
    }
    
    // Examples:
    // tenant.acme.orders.created
    // tenant.acme.inventory.item.updated
    // system.audit.login.failed
}
```

### 2. Permission Templates

```csharp
public static class PermissionTemplates
{
    public static UserPermissions ReadOnlyUser(string tenantId) => new()
    {
        SubscribeSubjects = new[]
        {
            $"tenant.{tenantId}.>",     // All tenant data
            "_INBOX.>"                   // For request/reply
        },
        PublishSubjects = new[]
        {
            "_INBOX.>"                   // Reply only
        },
        DenyPublishSubjects = new[]
        {
            $"tenant.{tenantId}.*.write", // No writes
            $"tenant.{tenantId}.*.delete" // No deletes
        }
    };
    
    public static UserPermissions ServiceUser(string tenantId) => new()
    {
        SubscribeSubjects = new[]
        {
            $"tenant.{tenantId}.>",      // All tenant data
            $"svc.{tenantId}.>",         // Service endpoints
            "_INBOX.>"
        },
        PublishSubjects = new[]
        {
            $"tenant.{tenantId}.>",      // Full access
            $"svc.{tenantId}.>",
            "_INBOX.>"
        },
        MaxSubscriptions = 1000,
        MaxDataPerDay = 10_737_418_240   // 10GB
    };
}
```

### 3. Credential Management

```csharp
public class SecureCredentialStore
{
    private readonly IDataProtector _protector;
    private readonly IDistributedCache _cache;
    
    public async Task<NatsCredentials> GetCredentials(
        string tenantId,
        string userId)
    {
        var key = $"nats:creds:{tenantId}:{userId}";
        
        // Check cache
        var cached = await _cache.GetAsync(key);
        if (cached != null)
        {
            return DeserializeAndDecrypt(cached);
        }
        
        // Generate new credentials
        var creds = await GenerateCredentials(tenantId, userId);
        
        // Encrypt and cache
        var encrypted = SerializeAndEncrypt(creds);
        await _cache.SetAsync(key, encrypted, new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(1),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
        });
        
        return creds;
    }
    
    public async Task RevokeCredentials(
        string tenantId,
        string userId)
    {
        // Remove from cache
        await _cache.RemoveAsync($"nats:creds:{tenantId}:{userId}");
        
        // Add to revocation list
        await PublishRevocation(tenantId, userId);
    }
}
```

### 4. Audit Logging

```csharp
public class NatsAuditLogger
{
    public async Task SetupAuditSubscriptions(INatsConnection conn)
    {
        // Authentication events
        await conn.SubscribeAsync(
            "$SYS.ACCOUNT.*.AUTHENTICATION.*",
            LogAuthEvent);
            
        // Authorization failures
        await conn.SubscribeAsync(
            "$SYS.ACCOUNT.*.AUTHORIZATION.VIOLATION",
            LogAuthzViolation);
            
        // Connection events
        await conn.SubscribeAsync(
            "$SYS.ACCOUNT.*.CONNECTIONS.*",
            LogConnectionEvent);
    }
    
    private async ValueTask LogAuthEvent(NatsMsg<string> msg)
    {
        var parts = msg.Subject.Split('.');
        var accountId = parts[2];
        var eventType = parts[4];
        
        await _auditLog.LogAsync(new AuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = accountId,
            EventType = $"AUTH_{eventType}",
            Details = msg.Data,
            Subject = msg.Subject
        });
    }
}
```

## Monitoring Multi-Tenant Systems

### Tenant Metrics Collection

```csharp
public class TenantMetricsCollector
{
    public async Task CollectMetrics()
    {
        foreach (var tenant in _tenants)
        {
            var conn = await GetTenantConnection(tenant.Id);
            var info = await conn.GetAccountInfoAsync();
            
            // Record metrics
            _metrics.RecordGauge(
                "nats.tenant.connections",
                info.Connections,
                ("tenant_id", tenant.Id));
                
            _metrics.RecordGauge(
                "nats.tenant.messages.in",
                info.InMsgs,
                ("tenant_id", tenant.Id));
                
            _metrics.RecordGauge(
                "nats.tenant.bytes.in",
                info.InBytes,
                ("tenant_id", tenant.Id));
                
            // JetStream metrics
            var js = conn.CreateJetStreamContext();
            var streams = await js.ListStreamsAsync();
            
            await foreach (var stream in streams)
            {
                _metrics.RecordGauge(
                    "nats.tenant.stream.messages",
                    stream.State.Messages,
                    ("tenant_id", tenant.Id),
                    ("stream", stream.Config.Name));
            }
        }
    }
}
```

## Best Practices Summary

1. **Use Accounts for True Isolation**: One NATS account per tenant
2. **JWT Authentication**: Dynamic credential management
3. **TLS Required**: Always use TLS in production
4. **Least Privilege**: Grant minimal permissions needed
5. **Audit Everything**: Log all security events
6. **Monitor Limits**: Track usage against quotas
7. **Credential Rotation**: Regular key rotation
8. **Secure Defaults**: Deny by default, allow explicitly

This security and multi-tenancy guide provides patterns for building secure, isolated messaging systems that meet enterprise requirements.