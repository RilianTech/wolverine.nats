using Wolverine.Configuration;
using Wolverine.Nats.Internals;

namespace Wolverine.Nats;

public static class NatsTransportExtensions
{
    /// <summary>
    /// Configure Wolverine to use NATS as a message transport
    /// </summary>
    public static void UseNats(this WolverineOptions options, string connectionString = "nats://localhost:4222")
    {
        var transport = options.Transports.GetOrCreate<NatsTransport>();
        transport.Configuration.ConnectionString = connectionString;
    }
}