using JasperFx.Core;
using Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Pinger;

// IHostedService that publishes a new Ping message every second
public class PingerService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PingerService> _logger;

    public PingerService(IServiceProvider serviceProvider, ILogger<PingerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var count = 0;

        // Wait a bit for everything to spin up
        await Task.Delay(3000, cancellationToken);
        
        _logger.LogInformation("Starting to send ping messages");

        await using var scope = _serviceProvider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var message = new Ping
                {
                    Number = ++count,
                    SentAt = DateTime.UtcNow
                };

                await bus.SendAsync(message);
                _logger.LogInformation("Sent Ping #{Number}", message.Number);

                await Task.Delay(1.Seconds(), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("Pinger service stopping");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending ping message");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Pinger service stopped");
        return Task.CompletedTask;
    }
}