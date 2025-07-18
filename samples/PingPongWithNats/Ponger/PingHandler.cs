using Messages;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Ponger;

public class PingHandler
{
    // Handle the incoming Ping and send back a Pong
    public async Task<Pong> Handle(Ping ping, ILogger<PingHandler> logger, IMessageContext context)
    {
        logger.LogInformation("Received Ping #{Number}", ping.Number);

        var pong = new Pong
        {
            Number = ping.Number,
            ReceivedPingAt = ping.SentAt,
            SentAt = DateTime.UtcNow
        };

        // Explicitly publish to the pongs subject
        await context.PublishAsync(pong);

        return pong;
    }
}
