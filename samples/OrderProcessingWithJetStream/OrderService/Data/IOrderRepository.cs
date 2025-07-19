using Shared.Models;

namespace OrderService.Data;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id);
    Task<List<Order>> GetByCustomerIdAsync(string customerId);
    Task<Order> CreateAsync(Order order);
    Task<Order> UpdateAsync(Order order);
    Task<bool> ExistsAsync(Guid id);
}

public class InMemoryOrderRepository : IOrderRepository
{
    private readonly Dictionary<Guid, Order> _orders = new();
    private readonly ILogger<InMemoryOrderRepository> _logger;

    public InMemoryOrderRepository(ILogger<InMemoryOrderRepository> logger)
    {
        _logger = logger;
    }

    public Task<Order?> GetByIdAsync(Guid id)
    {
        _orders.TryGetValue(id, out var order);
        return Task.FromResult(order);
    }

    public Task<List<Order>> GetByCustomerIdAsync(string customerId)
    {
        var orders = _orders.Values
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToList();
        return Task.FromResult(orders);
    }

    public Task<Order> CreateAsync(Order order)
    {
        _orders[order.Id] = order;
        _logger.LogInformation("Created order {OrderId} for customer {CustomerId}", 
            order.Id, order.CustomerId);
        return Task.FromResult(order);
    }

    public Task<Order> UpdateAsync(Order order)
    {
        _orders[order.Id] = order;
        _logger.LogInformation("Updated order {OrderId} status to {Status}", 
            order.Id, order.Status);
        return Task.FromResult(order);
    }

    public Task<bool> ExistsAsync(Guid id)
    {
        return Task.FromResult(_orders.ContainsKey(id));
    }
}