# NATS Security and Multi-Tenancy for Wolverine

## Overview

NATS provides robust security and multi-tenancy features that can support Wolverine's multi-tenancy requirements through its account system, JWT-based authentication, and subject-based permissions. This document outlines how these features work and how they can be leveraged for Wolverine.

## Core Security Features

### 1. Account System and Isolation

NATS accounts provide **complete isolation** between tenants:

- **Subject Namespace Isolation**: Each account has its own independent subject namespace. A message published on subject 'foo' in Account A will not be seen by subscribers to 'foo' in Account B.
- **Default Isolation**: All topics within an account are not shared with any entity outside that account by default.
- **No Cross-Account Communication**: Traffic cannot enter or exit an account without explicit grants.
- **Multi-Tenancy Design**: Accounts are designed specifically for multi-tenancy, providing what containers do for process isolation, but for messaging.

### 2. Authentication Methods

NATS supports multiple authentication methods:

#### JWT-Based Authentication (Recommended for Multi-Tenancy)
- **Hierarchical Trust Model**:
  - **Operator Level**: Root of trust, signs account JWTs
  - **Account Level**: Account JWTs signed by operator
  - **User Level**: User JWTs signed by account
- **Ed25519 Only**: All JWTs must be signed using Ed25519 algorithm
- **Decentralized Management**: Authentication/authorization managed separately from server configuration
- **Dynamic Updates**: Can update permissions without server restarts

#### Other Authentication Methods
- **Username/Password**: Basic authentication
- **Token Authentication**: Simple bearer tokens
- **NKey Authentication**: Public key authentication without JWTs
- **TLS Client Certificates**: Mutual TLS authentication

### 3. Authorization and Subject Permissions

Fine-grained permissions can be set at multiple levels:

#### Account Level
- **Connection Limits**: Maximum connections per account
- **Data Limits**: Maximum payload sizes, message rates
- **JetStream Limits**: Stream/consumer limits, storage quotas

#### User Level
- **Publish Permissions**: Which subjects a user can publish to
- **Subscribe Permissions**: Which subjects a user can subscribe to
- **Allow/Deny Lists**: Explicit subject patterns for access control

Example user permissions:
```json
{
  "permissions": {
    "publish": {
      "allow": ["tenant.12345.>", "$JS.API.>"]
    },
    "subscribe": {
      "allow": ["tenant.12345.>", "_INBOX.>"],
      "deny": ["tenant.12345.admin.>"]
    }
  }
}
```

### 4. Import/Export for Cross-Account Communication

When controlled communication between accounts is needed:

#### Exports
- **Stream Exports**: Share a stream of messages with other accounts
- **Service Exports**: Expose request-reply services to other accounts
- **Public vs Private**: Can make exports public or require explicit authorization

#### Imports
- **Stream Imports**: Subscribe to streams from other accounts
- **Service Imports**: Access services from other accounts
- **Subject Mapping**: Can remap imported subjects to local namespace

Example export/import:
```json
// Account A exports
{
  "exports": [
    {
      "stream": "orders.>",
      "accounts": ["ACCOUNT_B_PUBLIC_KEY"]
    },
    {
      "service": "billing.calculate",
      "response_type": "stream"
    }
  ]
}

// Account B imports
{
  "imports": [
    {
      "stream": {
        "account": "ACCOUNT_A_PUBLIC_KEY",
        "subject": "orders.>"
      },
      "to": "external.orders.>"
    }
  ]
}
```

### 5. Security Best Practices

#### Subject Naming Conventions
- Use hierarchical subjects: `tenant.<id>.domain.entity.action`
- Leverage wildcards for permissions: `tenant.12345.orders.>`
- System subjects start with `$` (e.g., `$JS.API`)

#### Account Isolation Patterns
1. **Tenant per Account**: Each tenant gets a dedicated account
2. **Shared Services Account**: Common services in separate account with exports
3. **Admin Account**: System administration in isolated account

#### Connection Security
- **TLS Encryption**: Always use TLS in production
- **Mutual TLS**: For enhanced security between services
- **Authorization Callouts**: Dynamic authorization via external service (ADR-26)

### 6. Multi-Tenancy Patterns

#### Pattern 1: Complete Isolation
```
Account per Tenant:
- Account A (Tenant 1): subjects "orders.>", "inventory.>"
- Account B (Tenant 2): subjects "orders.>", "inventory.>"
- No communication between accounts
```

#### Pattern 2: Shared Services
```
Tenant Accounts + Services Account:
- Account A (Tenant 1): imports "notifications" service
- Account B (Tenant 2): imports "notifications" service
- Account S (Services): exports "notifications" service
```

#### Pattern 3: Hierarchical Organization
```
Organization Account with Sub-accounts:
- Account O (Organization): administrative functions
- Account O.P1 (Project 1): isolated project namespace
- Account O.P2 (Project 2): isolated project namespace
```

## Implementation Recommendations for Wolverine

### 1. Account Strategy
- **One Account per Tenant**: Provides strongest isolation
- **Subject Prefix Convention**: Use tenant ID as subject prefix
- **Consistent Naming**: `<tenant-id>.<domain>.<aggregate>.<event>`

### 2. Authentication Flow
1. Wolverine receives tenant context
2. Retrieve appropriate JWT for tenant's account
3. Connect to NATS with tenant-specific credentials
4. All operations scoped to tenant's account

### 3. Resource Naming
Following the Rilian pattern:
- **Streams**: `<tenant-id>-<stream-name>`
- **Subjects**: `<tenant-id>.<subject-path>`
- **KV Buckets**: `<tenant-id>-<bucket-name>`

### 4. Connection Management
- **Connection Pool per Account**: Separate connections for each tenant
- **Lazy Connection**: Only connect when tenant is active
- **Connection Limits**: Respect account connection limits

### 5. Security Configuration
```csharp
// Example configuration
public class NatsTenantConfiguration
{
    public string TenantId { get; set; }
    public string AccountJwt { get; set; }
    public string UserSeed { get; set; }
    public string[] AllowedSubjects { get; set; }
    public string[] DeniedSubjects { get; set; }
}
```

## Benefits for Wolverine

1. **Strong Isolation**: Complete message isolation between tenants
2. **Scalability**: Can handle thousands of accounts/tenants
3. **Dynamic Configuration**: Add/remove tenants without restart
4. **Fine-grained Control**: Precise permissions per tenant
5. **Audit Trail**: All operations traceable to specific account/user
6. **Resource Limits**: Prevent tenant resource abuse
7. **Secure by Default**: No accidental cross-tenant data leaks

## Considerations

1. **Connection Overhead**: Each account requires separate connection
2. **JWT Management**: Need infrastructure for JWT generation/distribution
3. **Account Limits**: NATS server may have limits on number of accounts
4. **Cross-Tenant Communication**: Requires explicit export/import configuration
5. **Monitoring**: Need account-aware monitoring/metrics

## Conclusion

NATS provides enterprise-grade multi-tenancy features that align well with Wolverine's requirements. The account system with JWT authentication offers strong isolation guarantees while maintaining flexibility for controlled cross-tenant communication when needed. The subject-based permission model allows fine-grained access control, and the overall architecture supports both simple and complex multi-tenancy patterns.