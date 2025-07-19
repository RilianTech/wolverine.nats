# IoT Integration with NATS MQTT Gateway

## Overview

NATS provides a built-in MQTT gateway that allows IoT devices to communicate using MQTT protocol while leveraging NATS's superior routing and scaling capabilities. This creates a seamless bridge between IoT edge devices and cloud services.

## Architecture

```
IoT Device (MQTT) → NATS Server (MQTT Gateway) → Wolverine Services (NATS)
                           ↓
                    Translation Rules
                    MQTT Topic ↔ NATS Subject
```

## MQTT Gateway Configuration

### Basic Setup

```yaml
# nats-server.conf
mqtt {
  port: 1883
  
  # TLS for secure MQTT
  tls {
    cert_file: "/path/to/mqtt-cert.pem"
    key_file: "/path/to/mqtt-key.pem"
  }
  
  # Authentication
  authorization {
    username: "mqtt-user"
    password: "mqtt-pass"
  }
  
  # Connection limits
  max_clients: 10000
  max_pending_size: 10MB
}
```

### Topic Translation Rules

NATS automatically translates between MQTT topics and NATS subjects:

| MQTT Topic | NATS Subject | Notes |
|------------|--------------|-------|
| `sensors/temp/room1` | `sensors.temp.room1` | `/` → `.` |
| `sensors/+/room1` | `sensors.*.room1` | `+` → `*` |
| `sensors/#` | `sensors.>` | `#` → `>` |

## Wolverine Integration Patterns

### 1. Device Telemetry Collection

```csharp
// IoT devices publish to MQTT: sensors/temperature/{deviceId}
// Wolverine receives on NATS: sensors.temperature.{deviceId}

opts.ListenToNatsSubject("sensors.temperature.*")
    .UseQueueGroup("telemetry-processors")
    .ProcessInline();  // Low latency for real-time data

public class TemperatureHandler
{
    public async Task Handle(
        TelemetryMessage message, 
        IMessageContext context)
    {
        var deviceId = context.Subject.Split('.').Last();
        
        // Process telemetry
        await SaveToTimeSeries(deviceId, message.Value);
        
        // Check thresholds
        if (message.Value > 30)
        {
            await context.PublishAsync(new HighTemperatureAlert
            {
                DeviceId = deviceId,
                Temperature = message.Value
            });
        }
    }
}
```

### 2. Command and Control

```csharp
// Send commands to devices
opts.PublishMessage<DeviceCommand>()
    .ToNatsSubject("devices.{DeviceType}.{DeviceId}.command");

public class DeviceCommandService
{
    private readonly IMessageBus _bus;
    
    public async Task RestartDevice(string deviceId)
    {
        // Published to: devices.sensor.ABC123.command
        // Device receives on MQTT: devices/sensor/ABC123/command
        await _bus.PublishAsync(new DeviceCommand
        {
            DeviceId = deviceId,
            DeviceType = "sensor",
            Command = "restart",
            Parameters = new { delay = 5 }
        });
    }
}
```

### 3. Request/Reply with IoT Devices

```csharp
// Configure timeout for device responses
opts.ConfigureNats(config => 
{
    config.RequestTimeout = TimeSpan.FromSeconds(10);
});

public class DeviceQueryService
{
    private readonly IMessageBus _bus;
    
    public async Task<DeviceStatus> GetDeviceStatus(string deviceId)
    {
        try
        {
            // Send request to device
            var status = await _bus.InvokeAsync<DeviceStatus>(
                new GetStatus { DeviceId = deviceId },
                timeout: TimeSpan.FromSeconds(5)
            );
            
            return status;
        }
        catch (TimeoutException)
        {
            return new DeviceStatus { Online = false };
        }
    }
}
```

### 4. Device Registration and Discovery

```csharp
// Devices announce themselves on connect
opts.ListenToNatsSubject("devices.announce")
    .UseJetStream("DEVICES")  // Persist announcements
    .ProcessSequential();

public class DeviceRegistry
{
    private readonly IDocumentStore _store;
    
    public async Task Handle(DeviceAnnouncement announcement)
    {
        var device = new Device
        {
            Id = announcement.DeviceId,
            Type = announcement.DeviceType,
            Capabilities = announcement.Capabilities,
            LastSeen = DateTimeOffset.UtcNow,
            Metadata = announcement.Metadata
        };
        
        await _store.StoreAsync(device);
        
        // Subscribe to device-specific topics
        await SubscribeToDevice(device);
    }
}
```

## Advanced Patterns

### 1. Edge Computing with Leaf Nodes

Deploy NATS to edge locations for local processing:

```yaml
# Edge NATS server
leafnodes {
  remotes = [
    {
      url: "nats://cloud.example.com:7422"
      credentials: "/path/to/edge.creds"
    }
  ]
}

mqtt {
  port: 1883
}
```

```csharp
// Edge processing
opts.ListenToNatsSubject("sensors.>")
    .UseQueueGroup("edge-processors")
    .BufferedInMemory(1000)  // Handle connection issues
    .ProcessAsync(async (batch, context) =>
    {
        // Aggregate at edge
        var summary = AggregateTelemetry(batch);
        
        // Forward summary to cloud
        await context.PublishAsync(summary, 
            opts => opts.ToNatsSubject("cloud.telemetry.summary"));
    });
```

### 2. Firmware Updates

```csharp
// Firmware distribution with JetStream
public class FirmwareService
{
    public async Task DistributeFirmware(
        FirmwarePackage firmware,
        string deviceFilter)
    {
        // Store firmware in JetStream
        await StoreInJetStream(firmware);
        
        // Notify devices
        await _bus.PublishAsync(new FirmwareAvailable
        {
            Version = firmware.Version,
            DownloadUrl = $"nats://firmware.{firmware.Id}",
            Checksum = firmware.Checksum,
            DeviceFilter = deviceFilter
        });
    }
}

// Device-side handling
opts.ListenToNatsSubject("devices.*.firmware.available")
    .UseJetStream("FIRMWARE")
    .MaximumParallelMessages(1)  // One update at a time
    .ProcessAsync(async (msg, context) =>
    {
        if (ShouldUpdate(msg))
        {
            await DownloadAndApplyFirmware(msg);
        }
    });
```

### 3. Digital Twin Synchronization

```csharp
// Maintain device state in cloud
public class DigitalTwinService
{
    private readonly IDocumentStore _store;
    
    // Subscribe to all device state changes
    public void Configure(WolverineOptions opts)
    {
        opts.ListenToNatsSubject("devices.*.state")
            .UseJetStream("DEVICE_STATE")
            .ProcessAsync(UpdateDigitalTwin);
    }
    
    private async Task UpdateDigitalTwin(
        DeviceState state, 
        IMessageContext context)
    {
        var deviceId = ExtractDeviceId(context.Subject);
        
        var twin = await _store.LoadAsync<DigitalTwin>(deviceId) 
            ?? new DigitalTwin { DeviceId = deviceId };
        
        twin.UpdateState(state);
        twin.LastUpdated = DateTimeOffset.UtcNow;
        
        await _store.StoreAsync(twin);
        
        // Publish twin change event
        await context.PublishAsync(new TwinUpdated
        {
            DeviceId = deviceId,
            Changes = twin.GetChanges()
        });
    }
}
```

## IoT-Specific Considerations

### 1. Message Size Optimization

```csharp
// Use binary serialization for IoT
opts.ConfigureNats(config =>
{
    config.DefaultSerializer = new MessagePackSerializer();
});

// Compact message format
public record TelemetryMessage(
    float Value,
    long Timestamp,
    byte Flags
);
```

### 2. Handling Intermittent Connectivity

```csharp
// Buffer messages during disconnects
opts.ListenToNatsSubject("devices.*.telemetry")
    .BufferedInMemory(10_000)
    .CircuitBreaker(cb =>
    {
        cb.PauseTime = TimeSpan.FromMinutes(1);
        cb.FailureThreshold = 100;
    });
```

### 3. QoS Mapping

| MQTT QoS | NATS Equivalent | Implementation |
|----------|-----------------|----------------|
| QoS 0 | Core NATS | At-most-once delivery |
| QoS 1 | JetStream | At-least-once with ack |
| QoS 2 | JetStream + Dedup | Exactly-once with message ID |

```csharp
// QoS 1 equivalent
opts.ListenToNatsSubject("sensors.>")
    .UseJetStream("TELEMETRY")
    .AckPolicy(AckPolicy.Explicit);

// QoS 2 equivalent  
opts.PublishMessage<CriticalCommand>()
    .ToNatsSubject("devices.commands")
    .UseJetStream("COMMANDS")
    .WithMessageId(msg => $"cmd-{msg.CommandId}")
    .DeduplicationWindow(TimeSpan.FromMinutes(5));
```

## Security for IoT

### Device Authentication

```yaml
# Per-device credentials
authorization {
  users = [
    {
      user: "device-001"
      password: "$2a$11$..."
      permissions: {
        publish: ["devices.sensor.001.>"]
        subscribe: ["devices.sensor.001.command"]
      }
    }
  ]
}
```

### TLS with Certificate Pinning

```csharp
opts.UseNats(config =>
{
    config.EnableTls = true;
    config.ClientCertFile = "/path/to/device.crt";
    config.ClientKeyFile = "/path/to/device.key";
    config.TlsInsecure = false;  // Verify server cert
});
```

## Monitoring IoT Deployments

```csharp
// Track device health
public class DeviceHealthMonitor
{
    public void Configure(WolverineOptions opts)
    {
        // Monitor connection events
        opts.ListenToNatsSubject("$SYS.MQTT.CLIENT.>")
            .ProcessInline();
        
        // Track message rates
        opts.ListenToNatsSubject("devices.>")
            .Instrument(metrics =>
            {
                metrics.Counter("iot.messages", msg => 1);
                metrics.Histogram("iot.message.size", msg => msg.Size);
            });
    }
}
```

## Best Practices

1. **Use subject hierarchy** for device organization
   - `devices.{type}.{location}.{id}.{metric}`
   
2. **Implement device groups** using wildcards
   - Subscribe to `devices.sensor.floor1.>` for all floor 1 sensors
   
3. **Leverage JetStream** for critical commands
   - Ensure delivery of configuration changes
   
4. **Plan for scale** with queue groups
   - Distribute processing across multiple workers
   
5. **Monitor everything**
   - Device connectivity, message rates, error rates

The NATS MQTT gateway provides a powerful bridge between IoT devices and cloud services, enabling Wolverine applications to seamlessly integrate with the IoT ecosystem while maintaining high performance and reliability.