using Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Pinger;

public class PingWorker : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly ILogger<PingWorker> _logger;
    private int _pingNumber = 0;

    public PingWorker(IMessageBus bus, ILogger<PingWorker> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit for everything to start up
        await Task.Delay(3000, stoppingToken);

        _logger.LogInformation("Starting to send ping messages");

        while (!stoppingToken.IsCancellationRequested)
        {
            var ping = new Ping { Number = ++_pingNumber, SentAt = DateTime.UtcNow };

            await _bus.PublishAsync(ping);
            _logger.LogInformation("Sent Ping #{Number} at {SentAt}", ping.Number, ping.SentAt);

            await Task.Delay(1000, stoppingToken);
        }
    }
}
