# Wolverine.Nats Transport Development

## Project Overview
We are building a NATS transport SDK for the Wolverine messaging framework. This will enable Wolverine applications to use NATS as a messaging transport, supporting both Core NATS and JetStream.

## Current Status
- ✅ Basic project structure created
- ✅ Core transport classes implemented (NatsTransport, NatsEndpoint, NatsListener, NatsSender)
- ✅ Configuration classes created
- ✅ Basic extension methods for UseNats()
- ✅ Build is working with Wolverine 4.5.3
- ✅ Basic serialization and messaging working (PingPong sample works)
- ✅ JetStream configuration and auto-provisioning implemented
- ✅ OrderProcessingWithJetStream sample demonstrating real-world usage

## Resolved Issues

### 1. Serialization Error - FIXED
- Changed NatsEnvelope to inherit from Envelope directly
- Let NatsEnvelopeMapper populate properties instead of copying in constructor

### 2. URI Format - FIXED
- Changed from `nats://{subject}` to `nats://subject/{subject}` to match Wolverine patterns

### 3. Wolverine 4.5.3 API Changes - FIXED
- Added `Pipeline` property to NatsListener: `public IHandlerPipeline? Pipeline { get; private set; }`
- Made `ResourceUri` override in NatsTransport
- Switched from JasperFx.Core to JasperFx 1.2.2 to resolve LightweightCache conflicts

### 4. Package Versions - FIXED
- Using centralized package management with Directory.Packages.props
- Multi-targeting for .NET 8 and .NET 9

## Optional Reference Code Locations
These paths are optional and only needed for deeper investigation of framework internals:
- Wolverine source: `../wolverine` (if cloned alongside this repository)
- NATS.NET client source: `../nats.net` (official NATS client library)

## Build Commands
```bash
dotnet build
dotnet test
```

## Next Steps
1. Add comprehensive unit tests
2. Add integration tests with real NATS server
3. Implement advanced JetStream features (pull consumers, work queues)
4. Add support for NATS KV and Object Store
5. Performance optimization and benchmarking
6. Documentation and examples
7. Compliance tests using Wolverine's transport test suite

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
- WolverineFx 4.5.3
- NATS.Net 2.5.7
- JasperFx 1.2.2 (not JasperFx.Core - causes conflicts with Wolverine)
- Microsoft.Extensions.* 9.0.0 (for .NET 9 target)
- Microsoft.Extensions.* 8.0.x (for .NET 8 target)

## Important Patterns
1. Envelope mapping between Wolverine and NATS headers
2. Subject normalization (replacing '/' with '.')
3. JetStream consumer management (durable vs ephemeral)
4. Error handling and retry logic with RetryBlock
5. Cancellation token propagation

## Sample Projects
1. **PingPong** - Basic Core NATS messaging example
2. **StandardsAlignedExample** - Shows request/reply patterns
3. **OrderProcessingWithJetStream** - Comprehensive JetStream example with:
   - Event-driven architecture
   - Saga pattern implementation
   - Consumer groups for horizontal scaling
   - Stream auto-provisioning
   - Dead letter queue configuration