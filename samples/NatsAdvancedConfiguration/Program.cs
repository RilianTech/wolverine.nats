using Wolverine;
using Wolverine.Configuration;
using Wolverine.Nats;
using Wolverine.Nats.Internal;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWolverine(opts =>
{
    // Basic NATS configuration
    opts.UseNats("nats://localhost:4222");

    // Example 1: Using ConfigureListeners and ConfigureSenders
    opts.UseNats()
        .ConfigureListeners(listener =>
        {
            listener
                .UseJetStream()
                .UseQueueGroup($"{Environment.MachineName}-workers");
        })
        .ConfigureSenders(sender =>
        {
            sender.UseJetStream();
        });

    // Example 2: Adding a custom endpoint policy for more control
    opts.Policies.Add(new NatsJetStreamPolicy());
    
    // Example 3: Lambda endpoint policy for specific scenarios
    opts.Policies.Add(new LambdaEndpointPolicy<NatsEndpoint>((endpoint, runtime) =>
    {
        // Apply JetStream to all application endpoints
        if (endpoint.Role == EndpointRole.Application)
        {
            endpoint.UseJetStream = true;
            
            // Use service name in consumer name for better observability
            if (endpoint.IsListener)
            {
                endpoint.ConsumerName = $"{runtime.ServiceName}-{endpoint.Subject.Replace(".", "-")}";
            }
        }
        
        // High-priority subjects get different configuration
        if (endpoint.Subject.StartsWith("priority."))
        {
            endpoint.MaxDeliveryAttempts = 5;
            endpoint.Mode = EndpointMode.Inline; // Process synchronously
        }
        
        // Audit subjects don't need dead letter queues
        if (endpoint.Subject.StartsWith("audit."))
        {
            endpoint.DeadLetterQueueEnabled = false;
        }
    }));

    // Example 4: Environment-specific configuration
    if (builder.Environment.IsDevelopment())
    {
        opts.Policies.Add(new DevelopmentNatsPolicy());
    }
    else
    {
        opts.Policies.Add(new ProductionNatsPolicy());
    }

    // Define endpoints
    opts.ListenToNatsSubject("orders.created");
    opts.ListenToNatsSubject("priority.alerts");
    opts.ListenToNatsSubject("audit.events");
    
    opts.PublishAllMessages()
        .ToNatsSubject(type => type.Name.ToLower().Replace(".", "_"));
});

var app = builder.Build();
app.Run();

// Custom policy example
public class NatsJetStreamPolicy : IEndpointPolicy
{
    public void Apply(Endpoint endpoint, IWolverineRuntime runtime)
    {
        if (endpoint is NatsEndpoint natsEndpoint)
        {
            // Only apply to application endpoints, not system ones
            if (endpoint.Role != EndpointRole.Application) return;
            
            // Enable JetStream for all NATS endpoints
            natsEndpoint.UseJetStream = true;
            
            // Set stream name based on subject hierarchy
            var parts = natsEndpoint.Subject.Split('.');
            if (parts.Length > 0)
            {
                natsEndpoint.StreamName = $"{parts[0].ToUpper()}_STREAM";
            }
            
            // Configure consumer settings
            if (natsEndpoint.IsListener)
            {
                natsEndpoint.ConsumerName = $"{runtime.ServiceName}-{Environment.MachineName}";
                natsEndpoint.MaxDeliveryAttempts = 3;
            }
        }
    }
}

// Development-specific policy
public class DevelopmentNatsPolicy : IEndpointPolicy
{
    public void Apply(Endpoint endpoint, IWolverineRuntime runtime)
    {
        if (endpoint is NatsEndpoint natsEndpoint)
        {
            // In development, use shorter timeouts and more verbose logging
            endpoint.ExecutionOptions.CancellationTimeout = TimeSpan.FromSeconds(5);
            
            // Disable dead letter queues in development for easier debugging
            natsEndpoint.DeadLetterQueueEnabled = false;
        }
    }
}

// Production-specific policy
public class ProductionNatsPolicy : IEndpointPolicy
{
    public void Apply(Endpoint endpoint, IWolverineRuntime runtime)
    {
        if (endpoint is NatsEndpoint natsEndpoint)
        {
            // Production settings
            endpoint.ExecutionOptions.CancellationTimeout = TimeSpan.FromSeconds(30);
            
            // Ensure dead letter queues are enabled
            if (natsEndpoint.IsListener)
            {
                natsEndpoint.DeadLetterQueueEnabled = true;
                natsEndpoint.MaxDeliveryAttempts = 5;
                
                // Use a standard dead letter subject pattern
                if (string.IsNullOrEmpty(natsEndpoint.DeadLetterSubject))
                {
                    natsEndpoint.DeadLetterSubject = $"dead-letter.{natsEndpoint.Subject}";
                }
            }
        }
    }
}