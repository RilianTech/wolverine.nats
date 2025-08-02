# Wolverine.Nats Multi-Tenancy Design

## Overview

This document outlines the design for implementing Wolverine's multi-tenancy abstractions in the NATS transport, enabling tenant-isolated messaging while leveraging NATS's native security features.

## Goals

1. **Seamless Integration** - Work naturally with Wolverine's existing multi-tenancy patterns
2. **NATS Native** - Leverage NATS accounts, JWT auth, and subject hierarchies
3. **Performance** - Minimal overhead for tenant routing
4. **Flexibility** - Support multiple isolation strategies

## Architecture

### Core Components

```csharp
// 1. NatsTenant - Represents a tenant configuration
public class NatsTenant
{
    public string TenantId { get; set; }
    public NatsTransport? Transport { get; set; }
    public string? SubjectPrefix { get; set; }
    public string? CredentialsFile { get; set; }
    public string? ConnectionString { get; set; }
    
    // For account-based isolation
    public string? AccountId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

// 2. Update NatsTransport
public class NatsTransport : BrokerTransport<NatsEndpoint>
{
    // Add tenant support
    public Dictionary<string, NatsTenant> Tenants { get; } = new();
    
    // Override to support TenantedSender
    protected override ISender CreateSender(Uri uri, IWolverineRuntime runtime)
    {
        if (Tenants.Any())
        {
            var defaultSender = CreateNatsSender(uri, runtime, null);
            var tenantedSender = new TenantedSender(uri, TenantedIdBehavior, defaultSender);
            
            foreach (var tenant in Tenants.Values)
            {
                var tenantSender = CreateNatsSender(uri, runtime, tenant);
                tenantedSender.RegisterSender(tenant.TenantId, tenantSender);
            }
            
            return tenantedSender;
        }
        
        return CreateNatsSender(uri, runtime, null);
    }
}
```

### Configuration API

```csharp
public static class NatsTransportExpressionExtensions
{
    // Strategy 1: Subject-based isolation (simplest)
    public static NatsTransportExpression AddTenantWithSubjectPrefix(
        this NatsTransportExpression expression, 
        string tenantId, 
        string prefix)
    {
        expression.Transport.Tenants[tenantId] = new NatsTenant
        {
            TenantId = tenantId,
            SubjectPrefix = prefix
        };
        return expression;
    }
    
    // Strategy 2: Separate connections (resource intensive)
    public static NatsTransportExpression AddTenantWithConnection(
        this NatsTransportExpression expression,
        string tenantId,
        string connectionString)
    {
        var transport = new NatsTransport();
        transport.Configuration.ConnectionString = connectionString;
        
        expression.Transport.Tenants[tenantId] = new NatsTenant
        {
            TenantId = tenantId,
            Transport = transport,
            ConnectionString = connectionString
        };
        return expression;
    }
    
    // Strategy 3: Account-based isolation (most secure)
    public static NatsTransportExpression AddTenantWithAccount(
        this NatsTransportExpression expression,
        string tenantId,
        string credentialsFile)
    {
        expression.Transport.Tenants[tenantId] = new NatsTenant
        {
            TenantId = tenantId,
            CredentialsFile = credentialsFile
        };
        return expression;
    }
    
    // Configure tenant behavior
    public static NatsTransportExpression ConfigureMultiTenancy(
        this NatsTransportExpression expression,
        Action<MultiTenancyConfiguration> configure)
    {
        var config = new MultiTenancyConfiguration();
        configure(config);
        expression.Transport.TenantedIdBehavior = config.TenantedIdBehavior;
        expression.Transport.IsolationStrategy = config.IsolationStrategy;
        return expression;
    }
}

public class MultiTenancyConfiguration
{
    public TenantedIdBehavior TenantedIdBehavior { get; set; } = TenantedIdBehavior.FallbackToDefault;
    public TenantIsolationStrategy IsolationStrategy { get; set; } = TenantIsolationStrategy.SubjectPrefix;
}

public enum TenantIsolationStrategy
{
    SubjectPrefix,      // tenant1.orders.* vs tenant2.orders.*
    SeparateConnection, // Different connection per tenant
    NatsAccount        // NATS account-based isolation
}
```

## Implementation Strategies

### Strategy 1: Subject-Based Isolation (Recommended Starting Point)

```csharp
// Configuration
opts.UseNats("nats://localhost:4222")
    .ConfigureMultiTenancy(mt =>
    {
        mt.IsolationStrategy = TenantIsolationStrategy.SubjectPrefix;
        mt.TenantedIdBehavior = TenantedIdBehavior.TenantIdRequired;
    })
    .AddTenantWithSubjectPrefix("acme", "acme")
    .AddTenantWithSubjectPrefix("globex", "globex");

// Internal subject mapping
public class TenantAwareNatsSender : NatsSender
{
    protected override string GetTargetSubject(Envelope envelope)
    {
        if (!string.IsNullOrEmpty(envelope.TenantId) && 
            Tenants.TryGetValue(envelope.TenantId, out var tenant))
        {
            return $"{tenant.SubjectPrefix}.{base.GetTargetSubject(envelope)}";
        }
        
        return base.GetTargetSubject(envelope);
    }
}

// Listener configuration
public class TenantAwareNatsListener : NatsListener
{
    protected override string GetSubscriptionSubject()
    {
        // Subscribe to all tenant variants
        if (_endpoint.Transport.Tenants.Any())
        {
            // Use NATS wildcards: *.orders.> matches all tenants
            return $"*.{_endpoint.Subject}";
        }
        
        return _endpoint.Subject;
    }
    
    protected override void MapIncomingToEnvelope(NatsMsg<byte[]> msg, Envelope envelope)
    {
        base.MapIncomingToEnvelope(msg, envelope);
        
        // Extract tenant from subject: acme.orders.created -> acme
        var parts = msg.Subject.Split('.');
        if (parts.Length > 1 && _endpoint.Transport.Tenants.ContainsKey(parts[0]))
        {
            envelope.TenantId = parts[0];
        }
    }
}
```

### Strategy 2: Account-Based Isolation (Most Secure)

```csharp
// Configuration
opts.UseNats("nats://localhost:4222")
    .ConfigureMultiTenancy(mt =>
    {
        mt.IsolationStrategy = TenantIsolationStrategy.NatsAccount;
        mt.TenantedIdBehavior = TenantedIdBehavior.TenantIdRequired;
    })
    .AddTenantWithAccount("acme", "/creds/acme.creds")
    .AddTenantWithAccount("globex", "/creds/globex.creds");

// Tenant-specific connections
public class AccountBasedNatsTransport : NatsTransport
{
    private readonly Dictionary<string, NatsConnection> _tenantConnections = new();
    
    public async Task<NatsConnection> GetConnectionForTenant(string? tenantId)
    {
        if (string.IsNullOrEmpty(tenantId))
            return _defaultConnection;
            
        if (!_tenantConnections.TryGetValue(tenantId, out var connection))
        {
            var tenant = Tenants[tenantId];
            var opts = BuildNatsOpts(tenant);
            
            connection = new NatsConnection(opts);
            await connection.ConnectAsync();
            _tenantConnections[tenantId] = connection;
        }
        
        return connection;
    }
}
```

### Strategy 3: Hybrid Approach

```csharp
// Development: Subject prefixes
if (environment.IsDevelopment())
{
    opts.UseNats("nats://localhost:4222")
        .AddTenantWithSubjectPrefix("acme", "acme")
        .AddTenantWithSubjectPrefix("globex", "globex");
}
// Production: Account isolation
else
{
    opts.UseNats("nats://localhost:4222")
        .AddTenantWithAccount("acme", config["Nats:Tenants:Acme:Credentials"])
        .AddTenantWithAccount("globex", config["Nats:Tenants:Globex:Credentials"]);
}
```

## Usage Examples

### Basic Multi-Tenant Setup

```csharp
var builder = Host.CreateDefaultBuilder()
    .UseWolverine(opts =>
    {
        // Configure NATS with multi-tenancy
        opts.UseNats("nats://localhost:4222")
            .ConfigureMultiTenancy(mt =>
            {
                mt.IsolationStrategy = TenantIsolationStrategy.SubjectPrefix;
                mt.TenantedIdBehavior = TenantedIdBehavior.TenantIdRequired;
            })
            .AddTenantWithSubjectPrefix("tenant1", "tenant1")
            .AddTenantWithSubjectPrefix("tenant2", "tenant2");
        
        // Messages are automatically routed by tenant
        opts.PublishMessage<OrderPlaced>()
            .ToNatsSubject("orders.placed");
            
        // Listeners receive tenant context
        opts.ListenToNatsSubject("orders.>")
            .ProcessInline();
    });

// Sending with tenant context
public class OrderService
{
    private readonly IMessageBus _bus;
    
    public async Task PlaceOrder(Order order, string tenantId)
    {
        await _bus.PublishAsync(
            new OrderPlaced(order.Id), 
            new DeliveryOptions { TenantId = tenantId }
        );
    }
}

// Handlers automatically receive tenant context
public class OrderHandler
{
    public async Task Handle(OrderPlaced message, Envelope envelope)
    {
        var tenantId = envelope.TenantId;
        // Process for specific tenant
    }
}
```

### Dynamic Tenant Addition

```csharp
public class DynamicTenantService
{
    private readonly IWolverineRuntime _runtime;
    
    public async Task AddTenant(string tenantId, string credentialsFile)
    {
        var transport = _runtime.Options.Transports.GetOrCreate<NatsTransport>();
        
        // Add new tenant at runtime
        transport.Tenants[tenantId] = new NatsTenant
        {
            TenantId = tenantId,
            CredentialsFile = credentialsFile
        };
        
        // Rebuild senders to include new tenant
        await transport.RebuildSenders(_runtime);
    }
}
```

## Testing

```csharp
[Fact]
public async Task messages_are_isolated_by_tenant()
{
    // Arrange
    var tenant1Messages = new List<TestMessage>();
    var tenant2Messages = new List<TestMessage>();
    
    using var host = await Host.CreateDefaultBuilder()
        .UseWolverine(opts =>
        {
            opts.UseNats("nats://localhost:4222")
                .AddTenantWithSubjectPrefix("tenant1", "t1")
                .AddTenantWithSubjectPrefix("tenant2", "t2");
                
            opts.PublishAllMessages().ToNatsSubject("messages");
            
            opts.ListenToNatsSubject("*.messages")
                .ProcessInline();
        })
        .StartAsync();
    
    // Act
    var bus = host.Services.GetRequiredService<IMessageBus>();
    await bus.PublishAsync(new TestMessage("A"), new DeliveryOptions { TenantId = "tenant1" });
    await bus.PublishAsync(new TestMessage("B"), new DeliveryOptions { TenantId = "tenant2" });
    
    // Assert
    // Messages should be routed to correct tenant handlers
}
```

## Migration Path

### Phase 1: Subject Prefixes (Immediate)
- Implement subject-based routing
- No NATS server changes required
- Application-level isolation

### Phase 2: Authentication (Short-term)
- Add per-tenant credentials
- Still using subject prefixes
- Better security

### Phase 3: Account Isolation (Long-term)
- Full NATS account separation
- Complete infrastructure isolation
- Enterprise-grade security

## Performance Considerations

1. **Subject Prefixes**: Minimal overhead, single connection
2. **Separate Connections**: Higher memory usage, better isolation
3. **Account-Based**: Best isolation, requires JWT processing

## Security Considerations

1. **Always use TLS** in production
2. **Rotate credentials** regularly
3. **Monitor authentication failures**
4. **Implement rate limiting** per tenant
5. **Audit cross-tenant access** attempts

## Open Questions

1. Should we support automatic tenant detection from JWT claims?
2. How to handle tenant-specific JetStream configurations?
3. Should we provide built-in tenant provisioning APIs?
4. Integration with ASP.NET Core's multi-tenancy?

## Next Steps

1. Implement basic subject-based multi-tenancy
2. Add integration tests for all strategies
3. Create sample application demonstrating multi-tenancy
4. Document migration guide from single to multi-tenant
5. Performance benchmarks for different strategies