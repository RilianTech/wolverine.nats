using Messages;
using Microsoft.Extensions.Logging;

namespace Pinger;

public class PongHandler
{
    // Handle the Pong response
    public void Handle(Pong pong, ILogger<PongHandler> logger)
    {
        var roundTrip = DateTime.UtcNow - pong.ReceivedPingAt;
        logger.LogInformation(
            "Received Pong #{Number} - Round trip: {RoundTrip}ms",
            pong.Number,
            roundTrip.TotalMilliseconds
        );
    }
}
