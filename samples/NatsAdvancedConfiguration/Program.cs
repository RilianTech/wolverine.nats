using Wolverine;
using Wolverine.Configuration;
using Wolverine.Nats;
using Wolverine.Nats.Internal;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWolverine(opts =>
{
    opts.UseNats("nats://localhost:4222");

    opts.UseNats()
        .ConfigureListeners(listener =>
        {
            listener.UseJetStream().UseQueueGroup($"{Environment.MachineName}-workers");
        })
        .ConfigureSenders(sender =>
        {
            sender.UseJetStream();
        });

    opts.Policies.Add(new NatsJetStreamPolicy());

    opts.Policies.Add(
        new LambdaEndpointPolicy<NatsEndpoint>(
            (endpoint, runtime) =>
            {
                if (endpoint.Role == EndpointRole.Application)
                {
                    endpoint.UseJetStream = true;

                    if (endpoint.IsListener)
                    {
                        endpoint.ConsumerName =
                            $"{runtime.ServiceName}-{endpoint.Subject.Replace(".", "-")}";
                    }
                }

                if (endpoint.Subject.StartsWith("priority."))
                {
                    endpoint.MaxDeliveryAttempts = 5;
                    endpoint.Mode = EndpointMode.Inline;
                }

                if (endpoint.Subject.StartsWith("audit."))
                {
                    endpoint.DeadLetterQueueEnabled = false;
                }
            }
        )
    );

    if (builder.Environment.IsDevelopment())
    {
        opts.Policies.Add(new DevelopmentNatsPolicy());
    }
    else
    {
        opts.Policies.Add(new ProductionNatsPolicy());
    }

    opts.ListenToNatsSubject("orders.created");
    opts.ListenToNatsSubject("priority.alerts");
    opts.ListenToNatsSubject("audit.events");

    opts.PublishAllMessages().ToNatsSubject(type => type.Name.ToLower().Replace(".", "_"));
});

var app = builder.Build();
app.Run();

public class NatsJetStreamPolicy : IEndpointPolicy
{
    public void Apply(Endpoint endpoint, IWolverineRuntime runtime)
    {
        if (endpoint is NatsEndpoint natsEndpoint)
        {
            if (endpoint.Role != EndpointRole.Application)
                return;

            natsEndpoint.UseJetStream = true;

            var parts = natsEndpoint.Subject.Split('.');
            if (parts.Length > 0)
            {
                natsEndpoint.StreamName = $"{parts[0].ToUpper()}_STREAM";
            }

            if (natsEndpoint.IsListener)
            {
                natsEndpoint.ConsumerName = $"{runtime.ServiceName}-{Environment.MachineName}";
                natsEndpoint.MaxDeliveryAttempts = 3;
            }
        }
    }
}

public class DevelopmentNatsPolicy : IEndpointPolicy
{
    public void Apply(Endpoint endpoint, IWolverineRuntime runtime)
    {
        if (endpoint is NatsEndpoint natsEndpoint)
        {
            endpoint.ExecutionOptions.CancellationTimeout = TimeSpan.FromSeconds(5);
            natsEndpoint.DeadLetterQueueEnabled = false;
        }
    }
}

public class ProductionNatsPolicy : IEndpointPolicy
{
    public void Apply(Endpoint endpoint, IWolverineRuntime runtime)
    {
        if (endpoint is NatsEndpoint natsEndpoint)
        {
            endpoint.ExecutionOptions.CancellationTimeout = TimeSpan.FromSeconds(30);

            if (natsEndpoint.IsListener)
            {
                natsEndpoint.DeadLetterQueueEnabled = true;
                natsEndpoint.MaxDeliveryAttempts = 5;

                if (string.IsNullOrEmpty(natsEndpoint.DeadLetterSubject))
                {
                    natsEndpoint.DeadLetterSubject = $"dead-letter.{natsEndpoint.Subject}";
                }
            }
        }
    }
}
