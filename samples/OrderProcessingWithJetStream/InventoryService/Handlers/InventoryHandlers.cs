using Microsoft.Extensions.Logging;
using Shared.Events;
using Wolverine;

namespace InventoryService.Handlers;

public class OrderCreatedHandler
{
    private readonly IInventoryRepository _repository;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(
        IInventoryRepository repository,
        IMessageBus messageBus,
        ILogger<OrderCreatedHandler> logger)
    {
        _repository = repository;
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task Handle(OrderCreated orderCreated)
    {
        _logger.LogInformation("Processing OrderCreated event for order {OrderId} with {ItemCount} items",
            orderCreated.OrderId, orderCreated.Items.Count);

        var reservedItems = new Dictionary<string, int>();
        var failedItems = new List<string>();
        var allReserved = true;

        // Try to reserve each item
        foreach (var item in orderCreated.Items)
        {
            var reserved = await _repository.ReserveAsync(
                item.ProductId, 
                item.Quantity, 
                orderCreated.OrderId);

            if (reserved)
            {
                reservedItems[item.ProductId] = item.Quantity;
                _logger.LogDebug("Reserved {Quantity} of {ProductId} for order {OrderId}",
                    item.Quantity, item.ProductId, orderCreated.OrderId);
            }
            else
            {
                failedItems.Add($"{item.ProductName} (ID: {item.ProductId})");
                allReserved = false;
                _logger.LogWarning("Failed to reserve {ProductId} for order {OrderId}",
                    item.ProductId, orderCreated.OrderId);
            }
        }

        if (allReserved)
        {
            // All items reserved successfully
            await _messageBus.PublishAsync(new InventoryReserved(
                orderCreated.OrderId,
                reservedItems,
                DateTime.UtcNow
            ));
            
            _logger.LogInformation("Successfully reserved all items for order {OrderId}",
                orderCreated.OrderId);
        }
        else
        {
            // Some items failed - rollback all reservations
            foreach (var (productId, quantity) in reservedItems)
            {
                await _repository.ReleaseAsync(productId, quantity, orderCreated.OrderId);
            }

            await _messageBus.PublishAsync(new InventoryReservationFailed(
                orderCreated.OrderId,
                "Insufficient inventory for some items",
                failedItems,
                DateTime.UtcNow
            ));
            
            _logger.LogWarning("Failed to reserve inventory for order {OrderId}. Unavailable items: {Items}",
                orderCreated.OrderId, string.Join(", ", failedItems));
        }
    }
}

public class OrderCancelledHandler
{
    private readonly IInventoryRepository _repository;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<OrderCancelledHandler> _logger;

    public OrderCancelledHandler(
        IInventoryRepository repository,
        IMessageBus messageBus,
        ILogger<OrderCancelledHandler> logger)
    {
        _repository = repository;
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task Handle(OrderCancelled orderCancelled)
    {
        _logger.LogInformation("Processing OrderCancelled event for order {OrderId}",
            orderCancelled.OrderId);

        // In a real system, we'd need to look up what was reserved
        // For this demo, we'll publish an event indicating inventory was released
        await _messageBus.PublishAsync(new InventoryReleased(
            orderCancelled.OrderId,
            new Dictionary<string, int>(), // Would contain actual released items
            DateTime.UtcNow
        ));
    }
}

public class PaymentFailedHandler
{
    private readonly IInventoryRepository _repository;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<PaymentFailedHandler> _logger;

    public PaymentFailedHandler(
        IInventoryRepository repository,
        IMessageBus messageBus,
        ILogger<PaymentFailedHandler> logger)
    {
        _repository = repository;
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task Handle(PaymentFailed paymentFailed)
    {
        _logger.LogInformation("Processing PaymentFailed event for order {OrderId}",
            paymentFailed.OrderId);

        // Release inventory when payment fails
        // In a real system, we'd look up the reserved items
        await _messageBus.PublishAsync(new InventoryReleased(
            paymentFailed.OrderId,
            new Dictionary<string, int>(), // Would contain actual released items
            DateTime.UtcNow
        ));
    }
}