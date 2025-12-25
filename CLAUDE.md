# Wolverine.Nats Transport Development

## Project Overview
We are building a NATS transport SDK for the Wolverine messaging framework. This will enable Wolverine applications to use NATS as a messaging transport, supporting both Core NATS and JetStream.

## Current Status
- ✅ Basic project structure created
- ✅ Core transport classes implemented (NatsTransport, NatsEndpoint, NatsListener, NatsSender)
- ✅ Configuration classes created
- ✅ Basic extension methods for UseNats()
- ✅ Build is working with Wolverine 5.9.2
- ✅ Basic serialization and messaging working (PingPong sample works)
- ✅ JetStream configuration and auto-provisioning implemented
- ✅ OrderProcessingWithJetStream sample demonstrating real-world usage
- ✅ Multi-tenancy support with subject-based isolation
- ✅ Ready for integration into Wolverine repo

## Wolverine 5.9.2 Compatibility Updates
- Updated to WolverineFx 5.9.2 (from 4.5.3)
- Using JasperFx.Blocks for RetryBlock (moved from Wolverine.Util.Dataflow)
- Multi-targeting .NET 8, .NET 9, and .NET 10
- Package ID updated to WolverineFx.Nats to match official transport naming

## Migration to Wolverine Repository

### Files to Copy
Copy the following to `src/Transports/NATS/` in the Wolverine fork:

```
src/Wolverine.Nats/           → src/Transports/NATS/Wolverine.Nats/
tests/Wolverine.Nats.Tests/   → src/Transports/NATS/Wolverine.Nats.Tests/
```

### csproj Changes for Wolverine Repo
Replace the csproj content with project references:

**Wolverine.Nats.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <Description>NATS Transport for Wolverine Messaging Systems</Description>
        <PackageId>WolverineFx.Nats</PackageId>
        <GenerateAssemblyProductAttribute>false</GenerateAssemblyProductAttribute>
        <GenerateAssemblyCopyrightAttribute>false</GenerateAssemblyCopyrightAttribute>
        <GenerateAssemblyVersionAttribute>false</GenerateAssemblyVersionAttribute>
        <GenerateAssemblyFileVersionAttribute>false</GenerateAssemblyFileVersionAttribute>
        <GenerateAssemblyInformationalVersionAttribute>false</GenerateAssemblyInformationalVersionAttribute>
    </PropertyGroup>
    <ItemGroup>
        <ProjectReference Include="..\..\..\Wolverine\Wolverine.csproj"/>
    </ItemGroup>
    <ItemGroup>
        <PackageReference Include="NATS.Net" Version="2.6.8" />
    </ItemGroup>
    <Import Project="../../../../Analysis.Build.props"/>
</Project>
```

**Wolverine.Nats.Tests.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <IsPackable>false</IsPackable>
        <IsTestProject>true</IsTestProject>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
        <PackageReference Include="Shouldly" Version="4.3.0" />
        <PackageReference Include="xunit" Version="2.9.0"/>
        <PackageReference Include="xunit.runner.visualstudio" Version="2.8.0">
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            <PrivateAssets>all</PrivateAssets>
        </PackageReference>
        <PackageReference Include="coverlet.collector" Version="6.0.4">
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            <PrivateAssets>all</PrivateAssets>
        </PackageReference>
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="..\..\..\Testing\Wolverine.ComplianceTests\Wolverine.ComplianceTests.csproj" />
        <ProjectReference Include="..\Wolverine.Nats\Wolverine.Nats.csproj"/>
    </ItemGroup>
</Project>
```

### Enable Compliance Tests
In `NatsTransportComplianceTests.cs`, change `#if false` to `#if true` to enable the compliance tests.

### Solution File Updates
Add to `wolverine.sln`:
1. Create solution folder "NATS" under "Transports"
2. Add both projects to the folder

### Docker Compose
Add NATS to the Wolverine docker-compose.yml if not present:
```yaml
nats:
  image: nats:latest
  ports:
    - "4222:4222"
    - "8222:8222"
  command: ["--jetstream", "-m", "8222"]
```

## Build Commands
```bash
dotnet build
dotnet test
```

## Architecture Notes
- Following Wolverine's transport pattern (ITransport, Endpoint, IListener, ISender)
- Inherits from BrokerTransport<NatsEndpoint> for proper resource management
- Supporting both Core NATS (at-most-once) and JetStream (at-least-once)
- Queue groups for load balancing
- Full authentication support (username/password, token, NKey, JWT)
- TLS and mutual TLS support
- Multi-tenancy via subject-based isolation

## Dependencies
- WolverineFx 5.9.2
- NATS.Net 2.6.8
- JasperFx.Blocks (for RetryBlock, comes with WolverineFx)

## Key Classes
- `NatsTransport` - Main transport, inherits from `BrokerTransport<NatsEndpoint>`
- `NatsEndpoint` - Endpoint configuration, implements `IBrokerEndpoint`
- `NatsListener` - Message listener, implements `IListener`, `ISupportDeadLetterQueue`
- `NatsSender` - Message sender, implements `ISender`
- `NatsEnvelopeMapper` / `JetStreamEnvelopeMapper` - Map Wolverine envelopes to NATS messages
- `NatsTransportExpression` - Fluent configuration API
- `NatsListenerConfiguration` / `NatsSubscriberConfiguration` - Endpoint configuration

## Sample Projects
1. **PingPong** - Basic Core NATS messaging example
2. **OrderProcessingWithJetStream** - Comprehensive JetStream example with:
   - Event-driven architecture
   - Saga pattern implementation
   - Consumer groups for horizontal scaling
   - Stream auto-provisioning
   - Dead letter queue configuration
