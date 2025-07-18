# Testing Wolverine.Nats

## Quick Start

### 1. Start NATS Server
```bash
# Start NATS with JetStream enabled
docker compose up -d

# Check if NATS is running
docker compose logs wolverine-nats-test

# Check health
docker compose ps
```

### 2. Run the PingPong Sample
```bash
# Terminal 1 - Start the Ponger (receives Ping, sends Pong)
cd samples/PingPongWithNats/Ponger
dotnet run

# Terminal 2 - Start the Pinger (sends Ping, receives Pong)
cd samples/PingPongWithNats/Pinger  
dotnet run
```

### 3. Monitor NATS
- Web UI: http://localhost:8223 (NATS monitoring)
- CLI: `docker exec wolverine-nats-test nats server check`

## NATS Configuration

The test setup includes:
- **Port**: 4223 (mapped from container's 4222)
- **Monitoring**: 8223 (mapped from container's 8222) 
- **JetStream**: Enabled with 1GB memory, 10GB file storage
- **Data**: Persisted in Docker volume `wolverine-nats-data`

## Testing Features

### Core NATS (At-Most-Once)
- Basic pub/sub messaging
- Queue groups for load balancing
- Wildcard subscriptions

### JetStream (At-Least-Once)
- Durable message persistence
- Stream and consumer management
- Message acknowledgment and retry
- Dead letter queue handling

### Dead Letter Queue
- Automatic retry with configurable attempts
- Advisory messages on max delivery
- Optional republishing to DLQ subject

## Cleanup
```bash
# Stop and remove containers
docker compose down

# Remove data volume (optional)
docker volume rm wolverine-nats_wolverine-nats-data
```