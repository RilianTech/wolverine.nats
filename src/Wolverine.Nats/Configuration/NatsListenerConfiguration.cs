using Wolverine.Configuration;
using Wolverine.Nats.Internals;

namespace Wolverine.Nats.Configuration;

/// <summary>
/// Configuration for NATS message listeners
/// </summary>
public class NatsListenerConfiguration
    : ListenerConfiguration<NatsListenerConfiguration, NatsEndpoint>
{
    public NatsListenerConfiguration(NatsEndpoint endpoint)
        : base(endpoint) { }

    /// <summary>
    /// Use JetStream for durable messaging
    /// </summary>
    public NatsListenerConfiguration UseJetStream(
        string? streamName = null,
        string? consumerName = null
    )
    {
        add(endpoint =>
        {
            endpoint.UseJetStream = true;
            endpoint.StreamName = streamName ?? endpoint.Subject.Replace(".", "_").ToUpper();
            endpoint.ConsumerName = consumerName;
        });

        return this;
    }

    /// <summary>
    /// Use a queue group for load balancing (Core NATS only)
    /// </summary>
    public NatsListenerConfiguration UseQueueGroup(string queueGroup)
    {
        add(endpoint =>
        {
            endpoint.QueueGroup = queueGroup;
        });

        return this;
    }

    /// <summary>
    /// Configure dead letter queue settings for this NATS listener
    /// </summary>
    public NatsListenerConfiguration ConfigureDeadLetterQueue(
        Action<NatsDeadLetterConfiguration> configure
    )
    {
        var dlqConfig = new NatsDeadLetterConfiguration();
        configure(dlqConfig);

        add(endpoint =>
        {
            endpoint.DeadLetterQueueEnabled = dlqConfig.Enabled;
            endpoint.DeadLetterSubject = dlqConfig.DeadLetterSubject;
            endpoint.MaxDeliveryAttempts = dlqConfig.MaxDeliveryAttempts;
        });

        return this;
    }

    /// <summary>
    /// Disable dead letter queue handling for this listener
    /// </summary>
    public NatsListenerConfiguration DisableDeadLetterQueueing()
    {
        add(endpoint =>
        {
            endpoint.DeadLetterQueueEnabled = false;
        });

        return this;
    }

    /// <summary>
    /// Configure the dead letter subject for failed messages
    /// </summary>
    public NatsListenerConfiguration DeadLetterTo(string deadLetterSubject)
    {
        add(endpoint =>
        {
            endpoint.DeadLetterSubject = deadLetterSubject;
        });

        return this;
    }
}
