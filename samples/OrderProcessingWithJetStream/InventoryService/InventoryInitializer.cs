using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InventoryService;

public class InventoryInitializer : IHostedService
{
    private readonly IInventoryRepository _repository;
    private readonly ILogger<InventoryInitializer> _logger;

    public InventoryInitializer(
        IInventoryRepository repository,
        ILogger<InventoryInitializer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing inventory with sample data");

        // Initialize with some sample products
        var initialStock = new Dictionary<string, int>
        {
            ["LAPTOP-001"] = 50,      // 50 laptops in stock
            ["MOUSE-001"] = 200,      // 200 mice in stock
            ["KEYBOARD-001"] = 150,   // 150 keyboards in stock
            ["MONITOR-001"] = 75,     // 75 monitors in stock
            ["HEADSET-001"] = 100,    // 100 headsets in stock
            ["WEBCAM-001"] = 30,      // 30 webcams in stock
            ["DESK-001"] = 20,        // 20 desks in stock
            ["CHAIR-001"] = 25,       // 25 chairs in stock
            ["USB-HUB-001"] = 300,    // 300 USB hubs in stock
            ["CABLE-HDMI-001"] = 500  // 500 HDMI cables in stock
        };

        if (_repository is InMemoryInventoryRepository inMemoryRepo)
        {
            inMemoryRepo.InitializeInventory(initialStock);
        }

        _logger.LogInformation("Inventory initialized with {Count} product types", 
            initialStock.Count);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}