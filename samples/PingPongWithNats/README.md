# PingPong with NATS Sample

This sample demonstrates using Wolverine with NATS transport for basic messaging between two services.

## Prerequisites

1. NATS Server running (defaults to localhost:4222)
   ```bash
   # Using Docker Compose from repository root
   docker compose up -d
   
   # Or run NATS directly
   docker run -d --name nats -p 4222:4222 -p 8222:8222 nats -js
   
   # Or using NATS CLI
   nats-server -js
   ```

## Configuration

The samples use the following connection string priority:
1. `NATS_URL` environment variable
2. Default: `nats://localhost:4222`

To use a custom NATS server:
```bash
export NATS_URL=nats://your-server:4222
dotnet run
```

## Running the Sample

1. Build the solution:
   ```bash
   dotnet build
   ```

2. Start the Ponger service (in one terminal):
   ```bash
   cd Ponger
   dotnet run
   ```

3. Start the Pinger service (in another terminal):
   ```bash
   cd Pinger
   dotnet run
   ```

## What Happens

1. The Pinger service sends a `Ping` message every second to the "pings" subject
2. The Ponger service receives the `Ping` and responds with a `Pong` to the "pongs" subject
3. The Pinger service receives the `Pong` and logs the round-trip time

## Using JetStream (Durable Messaging)

To enable JetStream for at-least-once delivery, uncomment the JetStream configuration lines in both Program.cs files:

```csharp
// In Pinger/Program.cs
opts.ListenToNatsSubject("pongs")
    .UseJetStream("PINGPONG_STREAM", "pinger-consumer");

// In Ponger/Program.cs  
opts.ListenToNatsSubject("pings")
    .UseJetStream("PINGPONG_STREAM", "ponger-consumer");
```

This will:
- Create a JetStream stream named "PINGPONG_STREAM" automatically
- Create durable consumers for reliable message delivery
- Enable message acknowledgment and retry on failure

## Monitoring

You can monitor NATS using:
```bash
# View stream info
nats stream info PINGPONG_STREAM

# View consumer info
nats consumer info PINGPONG_STREAM pinger-consumer

# Monitor all subjects
nats sub ">"
```