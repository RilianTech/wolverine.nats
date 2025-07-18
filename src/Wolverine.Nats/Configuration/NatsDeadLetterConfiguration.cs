namespace Wolverine.Nats.Configuration;

public class NatsDeadLetterConfiguration
{
    /// <summary>
    /// Enable or disable dead letter queue support
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional subject to publish dead letter messages to
    /// </summary>
    public string? DeadLetterSubject { get; set; }

    /// <summary>
    /// Maximum delivery attempts before moving to DLQ (default: 5)
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>
    /// Enable monitoring of JetStream advisory subjects for DLQ events
    /// </summary>
    public bool MonitorAdvisories { get; set; } = false;
}
