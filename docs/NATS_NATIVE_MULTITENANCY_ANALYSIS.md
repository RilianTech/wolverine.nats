# NATS Native Multi-Tenancy Analysis for Wolverine Transport

## Executive Summary

NATS provides **the most sophisticated native multi-tenancy capabilities** of any message broker, with complete namespace isolation, dynamic authentication, and powerful subject transformation. This analysis shows how NATS's built-in features relate to Wolverine's multi-tenancy patterns and what our transport implementation needs to provide.

## NATS Native Multi-Tenancy Capabilities

### 1. **Account-Based Complete Isolation**

NATS accounts provide **true multi-tenancy** with zero cross-tenant message leakage:

```yaml
# Complete subject namespace isolation per account
accounts:
  TENANT_ACME:
    users: [{ user: acme_app, password: secret }]
    
  TENANT_GLOBEX: 
    users: [{ user: globex_app, password: secret }]
    
  SHARED_SERVICES:
    users: [{ user: svc_user, password: secret }]
    exports:
      - service: orders.processing.>  # Shared order processing
      - stream: events.audit.>        # Shared audit events
```

**Key Benefits:**
- **Bulletproof isolation**: Impossible for tenants to access each other's data
- **Independent resource limits**: Per-account connection, storage, and throughput limits
- **Separate JetStream storage**: Each account has its own streams and consumers
- **Zero performance interference**: Tenant A's load doesn't affect Tenant B

### 2. **Auth Callout - Dynamic Multi-Tenant Authentication**

The **Auth Callout** feature (NATS 2.10+) enables **dynamic tenant authentication** via **external sidecar services**.

> **Important**: Auth callout is a **separate microservice** that runs alongside NATS server, not part of our Wolverine transport. It can be implemented using the [synadia-io/callout.go](https://github.com/synadia-io/callout.go) library or as a custom service.

**NATS Server Configuration** (enables auth callout):
```yaml
authorization:
  users:
    - { user: auth, password: pwd }  # Service user for callout service
  auth_callout:
    auth_users: [auth]  # Users that bypass callout
    issuer: AAB35RZ7HJSICG7D4IGPYO3CTQPWWGULJXZYD45QAWUKTXJYDI6EO7MV  # Account signing key
    # Optional encryption
    xkey: XBCW4J63ZDLH54GKXJLBJQOWXEWPIYXY23HBMWL5LX6U24FW3C6U2UUL
```

**External Auth Callout Service** (Go example from synadia-io/callout.go):
```go
// Authorizer function that validates users and creates JWTs
authorizer := func(req *jwt.AuthorizationRequest) (string, error) {
    // Extract tenant information from request
    tenantId := extractTenantFromRequest(req)
    
    // Validate against external IAM (LDAP, OAuth, database)
    if !validateUser(req.UserNkey, tenantId) {
        return "", errors.New("unauthorized")
    }
    
    // Create tenant-scoped user claims
    uc := jwt.NewUserClaims(req.UserNkey)
    uc.Audience = fmt.Sprintf("TENANT_%s", strings.ToUpper(tenantId))
    uc.Sub.Allow.Add(fmt.Sprintf("tenant.%s.>", tenantId))
    uc.Sub.Allow.Add("_INBOX.>")
    uc.Expires = time.Now().Unix() + 3600  // 1 hour
    
    return uc.Encode(signingKey)
}

// Start the auth service
svc, err := NewAuthorizationService(
    natsConn, 
    Authorizer(authorizer), 
    ResponseSignerKey(accountKey),
)
```
```

**Architecture Benefits:**
- **External IAM integration**: LDAP, OAuth, SAML, custom databases
- **Dynamic tenant provisioning**: Create accounts on-demand
- **Centralized policy management**: Single source of truth for permissions
- **Real-time revocation**: Immediate credential invalidation

### 3. **Subject Mapping - Tenant Routing & Transformation**

NATS **subject mapping** provides powerful tenant routing capabilities:

```yaml
# Automatic tenant prefixing
mappings:
  # Route tenant-specific subjects
  "orders.*": "tenant.{{partition(10,1)}}.orders.{{wildcard(1)}}"
  
  # Deterministic tenant partitioning  
  "events.*": "tenant.{{partition(5,1)}}.events.{{wildcard(1)}}"
  
  # Weighted routing for A/B testing
  "api.requests.*": 
    - { destination: "tenant.prod.api.{{wildcard(1)}}", weight: 90% }
    - { destination: "tenant.canary.api.{{wildcard(1)}}", weight: 10% }
```

**Powerful Features:**
- **Deterministic partitioning**: Hash-based tenant distribution  
- **Subject transformation**: Automatic tenant prefixing/routing
- **Weighted routing**: A/B testing and canary deployments
- **Token manipulation**: Split, slice, reorder subject components

### 4. **Import/Export - Controlled Cross-Tenant Communication**

Accounts can selectively share services and data streams:

```yaml
accounts:
  TENANT_ACME:
    imports:
      # Import shared order processing service
      - service: { account: SHARED_SERVICES, subject: orders.processing.> }
        to: orders.process  # Local subject mapping
      
      # Import audit event stream
      - stream: { account: SHARED_SERVICES, subject: events.audit.> }
        prefix: shared.audit  # Prefix with shared.audit.*
        
  SHARED_SERVICES:
    exports:
      # Export order processing to specific tenants
      - service: orders.processing.>
        accounts: [TENANT_ACME, TENANT_GLOBEX]
      
      # Export audit events publicly
      - stream: events.audit.>
```

## Wolverine Multi-Tenancy Integration Strategies

### Strategy 1: **Subject-Based Isolation** (Simplest Implementation)

Use NATS subject hierarchy with Wolverine's tenant ID propagation:

```csharp
public class NatsSubjectBasedMultiTenancy
{
    public static void ConfigureWolverine(WolverineOptions opts)
    {
        opts.UseNats("nats://localhost:4222");
        
        // Publisher configuration with tenant-aware subjects
        opts.PublishMessage<OrderCreated>()
            .ToNatsSubject("orders.created")  // Will become "tenant.{id}.orders.created"
            .UseTenantIdFromHeader();
            
        // Listener configuration for tenant-specific subjects
        opts.ListenToNatsSubject("tenant.*.orders.created")
            .TenantAware()  // Extract tenant from subject
            .UseQueueGroup("order-processors");  // Load balance within tenant
    }
}

// Custom subject resolver
public class TenantAwareSubjectResolver : INatsSubjectResolver
{
    public string ResolveSubject(string baseSubject, Envelope envelope)
    {
        if (!string.IsNullOrEmpty(envelope.TenantId))
        {
            return $"tenant.{envelope.TenantId}.{baseSubject}";
        }
        return baseSubject;
    }
    
    public string ExtractTenant(string subject)
    {
        // Extract from "tenant.{id}.orders.created" format
        var parts = subject.Split('.');
        return parts.Length > 2 && parts[0] == "tenant" ? parts[1] : null;
    }
}
```

### Strategy 2: **Account-Based Isolation** (Maximum Security)

Use separate NATS accounts per tenant:

```csharp
public class NatsAccountBasedMultiTenancy
{
    private readonly ConcurrentDictionary<string, INatsConnection> _tenantConnections = new();
    
    public async Task<INatsConnection> GetTenantConnection(string tenantId)
    {
        return await _tenantConnections.GetOrAddAsync(tenantId, async id =>
        {
            var creds = await _credentialProvider.GetTenantCredentials(id);
            
            var opts = NatsOpts.Default with
            {
                Name = $"wolverine-{id}",
                AuthOpts = new NatsAuthOpts
                {
                    JWT = creds.JWT,
                    Seed = creds.Seed
                }
            };
            
            var connection = new NatsConnection(opts);
            await connection.ConnectAsync();
            return connection;
        });
    }
}

// Wolverine configuration per tenant
public static void ConfigureTenant(WolverineOptions opts, string tenantId)
{
    var connection = await GetTenantConnection(tenantId);
    
    opts.UseNats(connection);  // Use tenant-specific connection
    
    // Now all subjects are within the tenant's account namespace
    opts.PublishMessage<OrderCreated>().ToNatsSubject("orders.created");
    opts.ListenToNatsSubject("orders.created");
    
    // JetStream streams are also tenant-isolated
    opts.ListenToNatsSubject("commands.process")
        .UseJetStream("COMMANDS")  // Tenant-specific stream
        .UseDurableConsumer("processor");
}
```

### Strategy 3: **Auth Callout Integration** (Enterprise Grade)

> **Key Insight**: Auth callout runs as a **separate sidecar service**, not within our Wolverine transport. Our transport just needs to **connect using the dynamically issued JWTs**.

**Deployment Architecture**:
```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│  Wolverine App  │    │   NATS Server   │    │ Auth Callout    │
│                 │    │                 │    │ Sidecar Service │
│  (our transport)│◄──►│  (auth_callout  │◄──►│                 │
│                 │    │   configured)   │    │ (separate Go/   │
│                 │    │                 │    │  C#/.NET svc)   │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

**Auth Callout Service Configuration** (separate microservice):
```go
// This runs as a separate service, not in Wolverine transport
func main() {
    // Connect to NATS as service user
    nc, _ := nats.Connect("nats://localhost:4222", nats.UserInfo("auth", "pwd"))
    
    // Define tenant-aware authorizer
    authorizer := func(req *jwt.AuthorizationRequest) (string, error) {
        // Extract tenant from connection metadata or username
        tenantId := extractTenantFromConnectionName(req)
        
        // Validate with external IAM (your choice: LDAP, OAuth, DB)
        if !validateTenantUser(req.UserNkey, tenantId) {
            return "", errors.New("unauthorized")
        }
        
        // Issue tenant-scoped JWT
        uc := jwt.NewUserClaims(req.UserNkey)
        uc.Audience = fmt.Sprintf("TENANT_%s", strings.ToUpper(tenantId))
        uc.Sub.Allow.Add(fmt.Sprintf("tenant.%s.>", tenantId))
        uc.Sub.Allow.Add("_INBOX.>")  // For request/reply
        uc.Expires = time.Now().Unix() + 3600
        
        return uc.Encode(accountSigningKey)
    }
    
    // Start auth callout service
    svc, _ := callout.NewAuthorizationService(
        nc, 
        callout.Authorizer(authorizer), 
        callout.ResponseSignerKey(accountKey),
    )
    svc.Start()
}
```

**Wolverine Transport Integration** (connects to auth-enabled NATS):
```csharp
public static void ConfigureWolverine(WolverineOptions opts, NatsAuthCredentials creds)
{
    // Incredibly simple - just pass the JWT + NKey from auth callout
    var natsOpts = NatsOpts.Default with
    {
        Name = creds.ClientName,  // e.g., "wolverine-tenant-acme"
        AuthOpts = new NatsAuthOpts
        {
            Jwt = creds.Jwt,           // Tenant-scoped JWT from auth callout
            Seed = creds.NKeySeed,     // User's NKey seed
            Token = creds.ServiceToken // Optional: backend service token
        }
    };
    
    opts.UseNats(natsOpts);
    
    // NATS automatically enforces tenant permissions from JWT!
    opts.PublishMessage<OrderCreated>().ToNatsSubject("orders.created");
    opts.ListenToNatsSubject("orders.created");  // Only receives permitted messages
}

// Configuration model (like your Rilian implementation)
public class NatsAuthCredentials 
{
    public string Jwt { get; set; }           // From auth callout service
    public string NKeySeed { get; set; }      // User's private key
    public string? ServiceToken { get; set; } // Backend service access
    public string ClientName { get; set; }    // Connection identifier
}
```

## Implementation Priority & Complexity

### What NATS Provides Natively ✅

1. **Complete namespace isolation** (accounts)
2. **Dynamic authentication** (auth callout) 
3. **Subject transformation** (mapping)
4. **Cross-tenant communication** (import/export)
5. **Resource limits** (per-account quotas)
6. **Security** (JWT-based hierarchical auth)

### What Wolverine Transport Needs 🔧

1. **Tenant ID propagation** (envelope headers)
2. **Subject resolution** (tenant-aware routing) 
3. **Connection management** (per-tenant connections for account strategy)
4. **Configuration patterns** (tenant-aware endpoint configuration)

### What We DON'T Need to Implement ❌

1. **Auth Callout Service** - This is a separate sidecar microservice
2. **JWT Signing Logic** - Handled by the external auth service
3. **IAM Integration** - The callout service integrates with your IAM
4. **Dynamic Account Creation** - NATS server + auth callout handles this
5. **Subject Permission Enforcement** - NATS automatically blocks unauthorized subjects
6. **Tenant Validation Logic** - JWT contains all tenant-scoped permissions

### Implementation Recommendation

**Phase 1: Subject-Based (Current)** ⭐
- Simplest to implement with current NATS transport
- Leverages NATS subject mapping for tenant routing
- Works with single connection and existing auth
- **Effort: 1-2 weeks**

**Phase 2: Account-Based (Future)** 
- Maximum security with complete isolation
- Requires connection management per tenant
- Integrates with enterprise IAM systems
- **Effort: 3-4 weeks**

**Phase 3: Auth Callout Integration (Enterprise)**
- Deploy separate auth callout sidecar service
- Configure NATS server for auth callout
- Update Wolverine transport to use dynamic JWTs
- **Effort: 2-3 weeks** (since auth service is separate)

## Key Insight: NATS Does The Heavy Lifting

**The critical realization**: NATS's native multi-tenancy is so sophisticated that our Wolverine transport primarily needs to:

1. **Map Wolverine tenant IDs to NATS subjects/accounts**
2. **Propagate tenant context through envelope headers** 
3. **Provide configuration patterns for tenant-aware routing**

NATS handles:
- ✅ **Physical isolation** (accounts/subjects)
- ✅ **Authentication & authorization** (JWT/auth callout)
- ✅ **Subject transformation** (mapping/partitioning)
- ✅ **Resource management** (limits/quotas)
- ✅ **Cross-tenant communication** (import/export)

## Comparison with Other Transports

| Feature | RabbitMQ | Azure Service Bus | NATS |
|---------|----------|-------------------|------|
| **Isolation** | Virtual Hosts | Namespaces | Accounts (Complete) |
| **Auth Integration** | LDAP Plugin | Azure AD | Auth Callout (Any IAM) |
| **Subject Routing** | Exchange Routing | Topic Filters | Subject Mapping (Advanced) |
| **Cross-Tenant** | Federation | Cross-Namespace | Import/Export (Controlled) |
| **Performance Impact** | High | Medium | **None** |
| **Operational Complexity** | High | Medium | **Low** |

## Conclusion

NATS provides **the most advanced native multi-tenancy capabilities** of any message broker. Our Wolverine transport implementation should:

1. **Start with subject-based multi-tenancy** (Phase 1) - simple and effective
2. **Leverage NATS subject mapping** for automatic tenant routing  
3. **Design for account-based isolation** (Phase 2) - future enterprise feature
4. **Document auth callout integration** patterns for enterprise users

The beauty of NATS is that **the multi-tenancy is largely built-in** - we mainly need to bridge Wolverine's tenant concepts with NATS's powerful native capabilities.

**Bottom Line**: NATS does the heavy lifting for multi-tenancy; our transport just needs to speak its language fluently.