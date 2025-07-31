# Wolverine.Nats Monitoring & Troubleshooting Guide

This guide consolidates all monitoring, debugging, and troubleshooting information for Wolverine.Nats deployments.

## Quick Diagnostics

### Health Check Commands
```bash
# Check if NATS server is running
nats server check

# Test connection
nats server ping

# Check server info
curl http://localhost:8222/varz

# Monitor real-time traffic
nats sub ">"
```

### Common Connection Issues

#### "Connection refused" Error
```bash
# Check if NATS server is running
docker ps | grep nats
# or
ps aux | grep nats-server

# Verify port is open
netstat -ln | grep 4222
telnet localhost 4222
```

**Solutions:**
1. Start NATS server: `nats-server -js -m 8222`
2. Check firewall settings
3. Verify connection string in code matches server

#### "Authentication failed" Error
```csharp
// Enable verbose logging to see auth details
opts.UseNats(config => {
    config.ConnectionString = "nats://localhost:4222";
    config.LoggerFactory = loggerFactory;
    config.Verbose = true; // Shows auth handshake
});
```

**Solutions:**
1. Verify credentials: username/password, token, or creds file
2. Check server auth configuration
3. Ensure user has required permissions

## NATS Server Monitoring

### Built-in HTTP Monitoring
```bash
# Server statistics
curl http://localhost:8222/varz | jq .

# Connection information
curl http://localhost:8222/connz | jq .

# Subscription information
curl http://localhost:8222/subsz | jq .

# Route information (clustering)
curl http://localhost:8222/routez | jq .
```

### Key Metrics to Monitor
```json
{
  "connections": 42,           // Active connections
  "total_connections": 156,    // Total since start
  "in_msgs": 1000000,         // Messages received
  "out_msgs": 1000000,        // Messages sent
  "in_bytes": 500000000,      // Bytes received
  "out_bytes": 500000000,     // Bytes sent
  "slow_consumers": 2,        // Slow consumer count
  "mem": 50000000,           // Memory usage
  "cpu": 15.5                // CPU percentage
}
```

### JetStream Monitoring
```bash
# List all streams
nats stream ls

# Stream details
nats stream info ORDERS

# Consumer information
nats consumer ls ORDERS
nats consumer info ORDERS order-processor

# Consumer lag and performance
nats consumer report

# Stream events (real-time)
nats events
```

## Application-Level Monitoring

### Wolverine Metrics Integration
```csharp
// Add metrics collection
builder.Services.AddMetrics();

// Configure NATS metrics
opts.UseNats("nats://localhost:4222")
    .ConfigureMetrics(metrics => {
        metrics.TrackMessageCounts = true;
        metrics.TrackProcessingDuration = true;
        metrics.TrackErrorRates = true;
        metrics.TrackConsumerLag = true; // JetStream only
    });

// Custom metrics endpoint
app.MapGet("/metrics", (IMessageBus bus) => {
    var metrics = bus.GetMetrics();
    return Results.Ok(metrics);
});
```

### Custom Health Checks
```csharp
builder.Services.AddHealthChecks()
    .AddNats(options => {
        options.ConnectionString = "nats://localhost:4222";
        options.Timeout = TimeSpan.FromSeconds(5);
    })
    .AddNatsJetStream(options => {
        options.StreamName = "ORDERS";
        options.CheckConsumerLag = true;
        options.MaxLagThreshold = TimeSpan.FromMinutes(5);
    });

// Health check endpoint
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### Connection Event Monitoring
```csharp
public class NatsConnectionMonitor
{
    private readonly ILogger<NatsConnectionMonitor> _logger;
    
    public void SetupConnectionEvents(NatsConnection connection)
    {
        connection.ConnectionDisconnected += (sender, args) => {
            _logger.LogWarning("NATS disconnected: {Reason}", args.Reason);
            // Trigger alerts, update health status
        };
        
        connection.ConnectionReconnected += (sender, args) => {
            _logger.LogInformation("NATS reconnected after {Attempts} attempts", 
                args.Attempts);
        };
        
        connection.ReconnectFailed += (sender, args) => {
            _logger.LogError("NATS reconnect failed after {Attempts} attempts. " +
                           "Last error: {Error}", args.Attempts, args.LastError);
        };
        
        connection.FlushTimeout += (sender, args) => {
            _logger.LogWarning("NATS flush timeout after {Timeout}", args.Timeout);
        };
        
        connection.ServerDiscovered += (sender, args) => {
            _logger.LogInformation("Discovered NATS server: {Server}", args.Server);
        };
    }
}
```

## JetStream Troubleshooting

### Stream Issues

#### "Stream not found" Error
**Diagnosis:**
```bash
# Check if stream exists
nats stream ls
nats stream info STREAM_NAME
```

**Solutions:**
1. **Auto-provision enabled:** 
   ```csharp
   opts.UseNats("nats://localhost:4222").AutoProvision();
   ```

2. **Manual stream creation:**
   ```bash
   nats stream add ORDERS \
     --subjects "orders.>" \
     --retention limits \
     --max-msgs 1000000
   ```

3. **Define in code:**
   ```csharp
   opts.UseNats("nats://localhost:4222")
       .DefineStream("ORDERS", stream => {
           stream.WithSubjects("orders.>");
       });
   ```

#### "Subject already in use" Error
**Diagnosis:**
```bash
# Find which stream owns the subject
nats stream ls --subject "orders.created"
```

**Solutions:**
1. Use existing stream name
2. Remove subject from other stream
3. Use more specific subject patterns

#### Consumer Issues

#### "Consumer not found" Error
**Diagnosis:**
```bash
# List consumers for stream
nats consumer ls ORDERS
nats consumer info ORDERS consumer-name
```

**Solutions:**
1. **Durable consumer with explicit name:**
   ```csharp
   opts.ListenToNatsSubject("orders.created")
       .UseJetStream("ORDERS", "order-processor"); // Explicit name
   ```

2. **Check consumer configuration:**
   ```csharp
   opts.ListenToNatsSubject("orders.created")
       .UseJetStream("ORDERS", config => {
           config.Durable = "order-processor";
           config.DeliverPolicy = DeliverPolicy.All;
       });
   ```

#### High Consumer Lag
**Diagnosis:**
```bash
# Check consumer lag
nats consumer report
nats consumer info ORDERS order-processor
```

**Solutions:**
1. **Increase parallel processing:**
   ```csharp
   opts.ListenToNatsSubject("orders.created")
       .MaximumParallelMessages(20);
   ```

2. **Scale horizontally with queue groups:**
   ```csharp
   opts.ListenToNatsSubject("orders.created")
       .UseQueueGroup("order-processors");
   ```

3. **Optimize message processing:**
   ```csharp
   opts.ListenToNatsSubject("orders.created")
       .ProcessInline() // Avoid extra queuing
       .BufferedInMemory(1000); // Reduce I/O
   ```

### Memory and Performance Issues

#### High Memory Usage
**Diagnosis:**
```bash
# Check server memory
curl http://localhost:8222/varz | jq '.mem'

# Check stream storage
nats stream info ORDERS
```

**Solutions:**
1. **Configure stream limits:**
   ```csharp
   .DefineStream("ORDERS", stream => {
       stream.WithLimits(
           maxMessages: 1_000_000,
           maxBytes: 1024L * 1024 * 1024, // 1GB
           maxAge: TimeSpan.FromDays(7)
       );
   });
   ```

2. **Use workqueue retention:**
   ```csharp
   .DefineWorkQueueStream("TASKS", "tasks.>");
   ```

3. **Enable compression:**
   ```csharp
   opts.UseNats(config => {
       config.Compression = true;
   });
   ```

#### Slow Message Processing
**Diagnosis:**
```csharp
// Add timing metrics
opts.ListenToNatsSubject("orders.created")
    .Instrument(metrics => {
        metrics.Timer("message.processing.duration");
        metrics.Counter("message.processing.count");
    });
```

**Solutions:**
1. **Optimize handler code**
2. **Use async patterns:**
   ```csharp
   public async Task Handle(OrderCreated order)
   {
       await ProcessOrderAsync(order);
   }
   ```

3. **Batch processing:**
   ```csharp
   opts.ListenToNatsSubject("orders.created")
       .BufferedInMemory(100)
       .ProcessAsync(async (batch, context) => {
           await ProcessBatch(batch);
       });
   ```

## Message Troubleshooting

### Messages Not Being Received

#### Subject Mismatch
**Diagnosis:**
```bash
# Monitor all subjects
nats sub ">"

# Test specific subject
nats pub "orders.created" "test message"
nats sub "orders.created"
```

**Common Issues:**
- Case sensitivity: `Orders.Created` vs `orders.created`
- Subject normalization: `orders/created` becomes `orders.created`
- Wildcard patterns: `orders.*` vs `orders.>`

#### Queue Group Issues
**Diagnosis:**
```bash
# Check subscriptions
curl http://localhost:8222/subsz | jq '.subs[] | select(.queue_group)'
```

**Solutions:**
1. **Ensure all instances use same queue group:**
   ```csharp
   opts.ListenToNatsSubject("orders.created")
       .UseQueueGroup("order-processors"); // Same name everywhere
   ```

2. **Check queue group membership:**
   ```csharp
   // Log queue group info
   _logger.LogInformation("Joining queue group: {QueueGroup}", 
       endpoint.QueueGroup);
   ```

### Message Duplication

#### JetStream Redelivery
**Diagnosis:**
```bash
# Check redelivery count
nats consumer info ORDERS order-processor
```

**Solutions:**
1. **Proper acknowledgment:**
   ```csharp
   public async Task Handle(OrderCreated order, IMessageContext context)
   {
       try 
       {
           await ProcessOrder(order);
           // Auto-acknowledged on success
       }
       catch (RetryableException)
       {
           throw; // Will retry
       }
       catch (PermanentException)
       {
           await context.MoveToDeadLetterQueueAsync();
       }
   }
   ```

2. **Configure max delivery attempts:**
   ```csharp
   opts.ListenToNatsSubject("orders.created")
       .UseJetStream("ORDERS")
       .ConfigureDeadLetterQueue(3, "orders.failed");
   ```

3. **Idempotent message processing:**
   ```csharp
   public async Task Handle(OrderCreated order)
   {
       if (await IsAlreadyProcessed(order.OrderId))
           return; // Skip duplicate
           
       await ProcessOrder(order);
       await MarkAsProcessed(order.OrderId);
   }
   ```

## Observability and Alerting

### Structured Logging
```csharp
public class NatsLoggingHandler
{
    private readonly ILogger<NatsLoggingHandler> _logger;
    
    public void Handle(object message, IMessageContext context)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["MessageType"] = message.GetType().Name,
            ["Subject"] = context.Subject,
            ["MessageId"] = context.Id,
            ["CorrelationId"] = context.CorrelationId,
            ["Source"] = context.Source
        });
        
        _logger.LogInformation("Processing message {MessageType} from {Subject}",
            message.GetType().Name, context.Subject);
    }
}
```

### OpenTelemetry Integration
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => {
        tracing.AddNatsInstrumentation();
        tracing.AddWolverineInstrumentation();
    })
    .WithMetrics(metrics => {
        metrics.AddNatsMetrics();
        metrics.AddWolverineMetrics();
    });
```

### Custom Alerts
```csharp
public class NatsAlerting
{
    public void ConfigureAlerts(WolverineOptions opts)
    {
        // Monitor system events
        opts.ListenToNatsSubject("$SYS.SERVER.*.CLIENT.>")
            .ProcessInline();
            
        // Monitor high error rates
        opts.ListenToNatsSubject("errors.>")
            .ProcessAsync(async (error, context) => {
                if (ShouldAlert(error))
                {
                    await SendAlert(error);
                }
            });
    }
    
    private async Task SendAlert(ErrorEvent error)
    {
        // Send to Slack, PagerDuty, etc.
        await _alertService.SendAsync(new Alert
        {
            Severity = error.Severity,
            Message = error.Message,
            Timestamp = error.Timestamp,
            Source = "NATS-Wolverine"
        });
    }
}
```

## Performance Tuning

### Connection Tuning
```csharp
opts.UseNats(config => {
    // Increase buffer sizes for high throughput
    config.WriterBufferSize = 65536; // 64KB
    config.ReaderBufferSize = 65536; // 64KB
    
    // Reduce ping intervals for faster failure detection
    config.PingInterval = TimeSpan.FromSeconds(30);
    config.MaxPingsOut = 2;
    
    // Increase pending limits
    config.SubPendingBytesLimit = 64 * 1024 * 1024; // 64MB
    config.SubPendingMsgsLimit = 500000;
});
```

### JetStream Performance
```csharp
// Use pull consumers for better control
opts.ListenToNatsSubject("high.volume.topic")
    .UseJetStream("HIGH_VOLUME", config => {
        config.MaxAckPending = 1000;
        config.AckWait = TimeSpan.FromSeconds(30);
        config.MaxDeliver = 3;
    })
    .MaximumParallelMessages(50)
    .BufferedInMemory(10000);
```

## Troubleshooting Checklist

### Basic Connectivity
- [ ] NATS server is running and accessible
- [ ] Connection string is correct
- [ ] Authentication credentials are valid
- [ ] Network connectivity (firewall, DNS)
- [ ] TLS configuration (if used)

### JetStream Issues
- [ ] JetStream is enabled on server (`-js` flag)
- [ ] Streams are created and accessible
- [ ] Consumer configuration is correct
- [ ] No subject conflicts between streams
- [ ] Consumer lag is within acceptable limits

### Message Flow
- [ ] Subject names match exactly (case sensitive)
- [ ] Serialization/deserialization working
- [ ] Handler methods are properly registered
- [ ] No poison messages blocking processing
- [ ] Dead letter queues configured if needed

### Performance
- [ ] Adequate buffer sizes configured
- [ ] Appropriate parallelism settings
- [ ] Queue groups used for scaling
- [ ] Resource limits not exceeded
- [ ] No memory leaks in handlers

### Security
- [ ] TLS enabled in production
- [ ] Appropriate subject permissions
- [ ] Account isolation (if multi-tenant)
- [ ] Credential rotation schedule
- [ ] Audit logging enabled

## Getting Help

### Enable Debug Logging
```csharp
opts.UseNats(config => {
    config.LoggerFactory = loggerFactory;
    config.Verbose = true; // Protocol-level logging
});

// In appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Wolverine.Nats": "Debug",
      "NATS": "Debug"
    }
  }
}
```

### Diagnostic Tools
```bash
# NATS CLI tools
nats --help
nats server check --help
nats stream --help
nats consumer --help

# Monitoring tools  
nats top           # Real-time server stats
nats bench         # Performance testing
nats latency       # Latency testing
```

### Common Log Messages

**Connection Issues:**
- `"Connection refused"` - Server not running or wrong port
- `"Authentication failed"` - Invalid credentials
- `"Connection timeout"` - Network or firewall issues

**JetStream Issues:**
- `"Stream not found"` - Stream doesn't exist or wrong name
- `"Consumer not found"` - Consumer not created or wrong name
- `"Subject already in use"` - Subject conflict between streams

**Processing Issues:**
- `"Handler not found"` - No registered handler for message type
- `"Serialization failed"` - Message format issues
- `"Consumer lag exceeded"` - Processing too slow

This troubleshooting guide covers the most common issues and their solutions. For complex problems, enable debug logging and use the NATS CLI tools to gather detailed diagnostic information.