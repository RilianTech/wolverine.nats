# Wolverine.Nats Transport Development

## Project Overview
We are building a NATS transport SDK for the Wolverine messaging framework. This will enable Wolverine applications to use NATS as a messaging transport, supporting both Core NATS and JetStream.

## Current Status
- ✅ Basic project structure created
- ✅ Core transport classes implemented (NatsTransport, NatsEndpoint, NatsListener, NatsSender)
- ✅ Configuration classes created
- ✅ Basic extension methods for UseNats()
- ❌ Build is failing due to API mismatches

## Known Issues to Fix

### 1. NATS Client API Issues
- We're using `INatsConnection` but should use `INatsClient` from NATS.Net
- `CreateJetStreamContext()` is a method on `INatsClient`, not `INatsConnection`
- The NATS.Net package uses different interfaces than expected

### 2. Wolverine Envelope Issues
- Properties like `ReplyUri`, `ConversationId`, `Source`, and `SentAt` have internal setters
- We cannot directly set these properties when creating envelopes
- Need to find alternative approaches or use reflection

### 3. JetStream API Issues
- `AckAsync()` and `NakAsync()` methods don't exist directly on `NatsJSMsg<T>`
- Need to check the correct API for acknowledging JetStream messages

### 4. Method Signatures
- `DateTimeOffset` is not nullable in Envelope, but we're treating it as nullable
- Some type conversions need explicit casts (e.g., ulong to int)

## Reference Code Locations
- Wolverine source: `../wolverine`
- NATS reference implementation: `../nats.net`

## Build Commands
```bash
# From the repository root
dotnet build
dotnet test
```

## Next Steps
1. Fix the NATS client API usage (switch from INatsConnection to INatsClient)
2. Handle Wolverine Envelope read-only properties
3. Fix JetStream message acknowledgment
4. Add proper error handling
5. Implement missing transport features
6. Add unit tests
7. Add integration tests

## Architecture Notes
- Following Wolverine's transport pattern (ITransport, Endpoint, IListener, ISender)
- Supporting both Core NATS (at-most-once) and JetStream (at-least-once)
- Queue groups for load balancing
- Full authentication support (username/password, token, NKey, JWT)
- TLS and mutual TLS support

## Testing Strategy
- Unit tests for individual components
- Integration tests with real NATS server
- Compliance tests using Wolverine's transport test suite
- Performance benchmarks

## Dependencies
- WolverineFx 3.4.0
- NATS.Net 2.5.7
- JasperFx.Core 2.0.0
- Microsoft.Extensions.* 8.0.2

## Important Patterns
1. Envelope mapping between Wolverine and NATS headers
2. Subject normalization (replacing '/' with '.')
3. JetStream consumer management (durable vs ephemeral)
4. Error handling and retry logic with RetryBlock
5. Cancellation token propagation