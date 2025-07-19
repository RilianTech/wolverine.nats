using Microsoft.Extensions.Logging;

namespace InventoryService;

public interface IInventoryRepository
{
    Task<int> GetAvailableQuantityAsync(string productId);
    Task<bool> ReserveAsync(string productId, int quantity, Guid orderId);
    Task ReleaseAsync(string productId, int quantity, Guid orderId);
    Task<Dictionary<string, int>> GetInventoryLevelsAsync(IEnumerable<string> productIds);
}

public class InMemoryInventoryRepository : IInventoryRepository
{
    private readonly Dictionary<string, InventoryItem> _inventory = new();
    private readonly Dictionary<string, List<Reservation>> _reservations = new();
    private readonly ILogger<InMemoryInventoryRepository> _logger;
    private readonly object _lock = new();

    public InMemoryInventoryRepository(ILogger<InMemoryInventoryRepository> logger)
    {
        _logger = logger;
    }

    public void InitializeInventory(Dictionary<string, int> initialStock)
    {
        lock (_lock)
        {
            foreach (var (productId, quantity) in initialStock)
            {
                _inventory[productId] = new InventoryItem
                {
                    ProductId = productId,
                    TotalQuantity = quantity,
                    AvailableQuantity = quantity
                };
                _reservations[productId] = new List<Reservation>();
            }
        }
        _logger.LogInformation("Initialized inventory with {Count} products", initialStock.Count);
    }

    public Task<int> GetAvailableQuantityAsync(string productId)
    {
        lock (_lock)
        {
            if (_inventory.TryGetValue(productId, out var item))
            {
                return Task.FromResult(item.AvailableQuantity);
            }
            return Task.FromResult(0);
        }
    }

    public Task<bool> ReserveAsync(string productId, int quantity, Guid orderId)
    {
        lock (_lock)
        {
            if (!_inventory.TryGetValue(productId, out var item))
            {
                _logger.LogWarning("Product {ProductId} not found in inventory", productId);
                return Task.FromResult(false);
            }

            if (item.AvailableQuantity < quantity)
            {
                _logger.LogWarning("Insufficient inventory for {ProductId}. Available: {Available}, Requested: {Requested}",
                    productId, item.AvailableQuantity, quantity);
                return Task.FromResult(false);
            }

            // Reserve the quantity
            item.AvailableQuantity -= quantity;
            _reservations[productId].Add(new Reservation
            {
                OrderId = orderId,
                Quantity = quantity,
                ReservedAt = DateTime.UtcNow
            });

            _logger.LogInformation("Reserved {Quantity} of {ProductId} for order {OrderId}. Remaining: {Available}",
                quantity, productId, orderId, item.AvailableQuantity);

            return Task.FromResult(true);
        }
    }

    public Task ReleaseAsync(string productId, int quantity, Guid orderId)
    {
        lock (_lock)
        {
            if (!_inventory.TryGetValue(productId, out var item))
            {
                _logger.LogWarning("Product {ProductId} not found in inventory", productId);
                return Task.CompletedTask;
            }

            var reservation = _reservations[productId].FirstOrDefault(r => r.OrderId == orderId);
            if (reservation != null)
            {
                item.AvailableQuantity += reservation.Quantity;
                _reservations[productId].Remove(reservation);
                
                _logger.LogInformation("Released {Quantity} of {ProductId} from order {OrderId}. Available: {Available}",
                    reservation.Quantity, productId, orderId, item.AvailableQuantity);
            }
        }
        
        return Task.CompletedTask;
    }

    public Task<Dictionary<string, int>> GetInventoryLevelsAsync(IEnumerable<string> productIds)
    {
        lock (_lock)
        {
            var levels = new Dictionary<string, int>();
            foreach (var productId in productIds)
            {
                if (_inventory.TryGetValue(productId, out var item))
                {
                    levels[productId] = item.AvailableQuantity;
                }
                else
                {
                    levels[productId] = 0;
                }
            }
            return Task.FromResult(levels);
        }
    }
}

public class InventoryItem
{
    public string ProductId { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public int AvailableQuantity { get; set; }
}

public class Reservation
{
    public Guid OrderId { get; set; }
    public int Quantity { get; set; }
    public DateTime ReservedAt { get; set; }
}