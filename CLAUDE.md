# Wolverine.Nats

NATS transport for the Wolverine messaging framework.

## Build Commands

```bash
dotnet build
dotnet test
```

## Architecture

- `NatsTransport` - Main transport, inherits from `BrokerTransport<NatsEndpoint>`
- `NatsEndpoint` - Endpoint configuration, implements `IBrokerEndpoint`
- `NatsListener` - Message listener, implements `IListener`, `ISupportDeadLetterQueue`
- `NatsSender` - Message sender, implements `ISender`
- `NatsEnvelopeMapper` - Maps Wolverine envelopes to NATS messages

## Dependencies

- WolverineFx
- NATS.Net
