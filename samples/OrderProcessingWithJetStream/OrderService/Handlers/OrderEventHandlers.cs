using OrderService.Data;
using Shared.Events;
using Shared.Models;
using Wolverine;

namespace OrderService.Handlers;

public class InventoryEventHandler
{
    private readonly IOrderRepository _repository;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<InventoryEventHandler> _logger;

    public InventoryEventHandler(
        IOrderRepository repository,
        IMessageBus messageBus,
        ILogger<InventoryEventHandler> logger)
    {
        _repository = repository;
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task Handle(InventoryReserved inventoryReserved)
    {
        _logger.LogInformation("Handling InventoryReserved for order {OrderId}", 
            inventoryReserved.OrderId);

        var order = await _repository.GetByIdAsync(inventoryReserved.OrderId);
        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found", inventoryReserved.OrderId);
            return;
        }

        var oldStatus = order.Status;
        order.Status = OrderStatus.InventoryReserved;
        await _repository.UpdateAsync(order);

        // Publish status change
        await _messageBus.PublishAsync(new OrderStatusChanged(
            order.Id,
            oldStatus,
            order.Status,
            "Inventory reserved successfully",
            DateTime.UtcNow
        ));

        // Request payment
        await _messageBus.PublishAsync(new PaymentRequested(
            order.Id,
            order.TotalAmount,
            order.PaymentInfo!.PaymentMethod,
            DateTime.UtcNow
        ));

        _logger.LogInformation("Order {OrderId} inventory reserved, payment requested", 
            order.Id);
    }

    public async Task Handle(InventoryReservationFailed failed)
    {
        _logger.LogWarning("Inventory reservation failed for order {OrderId}: {Reason}", 
            failed.OrderId, failed.Reason);

        var order = await _repository.GetByIdAsync(failed.OrderId);
        if (order == null) return;

        var oldStatus = order.Status;
        order.Status = OrderStatus.Failed;
        await _repository.UpdateAsync(order);

        // Publish status change
        await _messageBus.PublishAsync(new OrderStatusChanged(
            order.Id,
            oldStatus,
            order.Status,
            $"Inventory reservation failed: {failed.Reason}",
            DateTime.UtcNow
        ));
    }
}

public class PaymentEventHandler
{
    private readonly IOrderRepository _repository;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<PaymentEventHandler> _logger;

    public PaymentEventHandler(
        IOrderRepository repository,
        IMessageBus messageBus,
        ILogger<PaymentEventHandler> logger)
    {
        _repository = repository;
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task Handle(PaymentCompleted paymentCompleted)
    {
        _logger.LogInformation("Payment completed for order {OrderId}, transaction {TransactionId}", 
            paymentCompleted.OrderId, paymentCompleted.TransactionId);

        var order = await _repository.GetByIdAsync(paymentCompleted.OrderId);
        if (order == null) return;

        var oldStatus = order.Status;
        order.Status = OrderStatus.PaymentCompleted;
        order.PaymentInfo!.TransactionId = paymentCompleted.TransactionId;
        order.PaymentInfo.ProcessedAt = paymentCompleted.OccurredAt;
        order.PaymentInfo.Status = PaymentStatus.Captured;
        
        await _repository.UpdateAsync(order);

        // Publish status change
        await _messageBus.PublishAsync(new OrderStatusChanged(
            order.Id,
            oldStatus,
            order.Status,
            "Payment completed successfully",
            DateTime.UtcNow
        ));

        // Request shipping
        await _messageBus.PublishAsync(new ShippingRequested(
            order.Id,
            order.ShippingInfo!,
            DateTime.UtcNow
        ));

        _logger.LogInformation("Order {OrderId} payment completed, shipping requested", 
            order.Id);
    }

    public async Task Handle(PaymentFailed failed)
    {
        _logger.LogWarning("Payment failed for order {OrderId}: {Reason}", 
            failed.OrderId, failed.Reason);

        var order = await _repository.GetByIdAsync(failed.OrderId);
        if (order == null) return;

        var oldStatus = order.Status;
        order.Status = OrderStatus.Failed;
        order.PaymentInfo!.Status = PaymentStatus.Failed;
        await _repository.UpdateAsync(order);

        // Publish status change
        await _messageBus.PublishAsync(new OrderStatusChanged(
            order.Id,
            oldStatus,
            order.Status,
            $"Payment failed: {failed.Reason}",
            DateTime.UtcNow
        ));

        // Release inventory
        var itemsToRelease = order.Items.ToDictionary(i => i.ProductId, i => i.Quantity);
        await _messageBus.PublishAsync(new InventoryReleased(
            order.Id,
            itemsToRelease,
            DateTime.UtcNow
        ));
    }
}

public class ShippingEventHandler
{
    private readonly IOrderRepository _repository;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ShippingEventHandler> _logger;

    public ShippingEventHandler(
        IOrderRepository repository,
        IMessageBus messageBus,
        ILogger<ShippingEventHandler> logger)
    {
        _repository = repository;
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task Handle(OrderShipped shipped)
    {
        _logger.LogInformation("Order {OrderId} shipped with tracking {TrackingNumber}", 
            shipped.OrderId, shipped.TrackingNumber);

        var order = await _repository.GetByIdAsync(shipped.OrderId);
        if (order == null) return;

        var oldStatus = order.Status;
        order.Status = OrderStatus.Shipped;
        order.ShippingInfo!.TrackingNumber = shipped.TrackingNumber;
        order.ShippingInfo.ShippedAt = shipped.OccurredAt;
        order.ShippingInfo.EstimatedDelivery = shipped.EstimatedDelivery;
        
        await _repository.UpdateAsync(order);

        // Publish status change
        await _messageBus.PublishAsync(new OrderStatusChanged(
            order.Id,
            oldStatus,
            order.Status,
            $"Order shipped. Tracking: {shipped.TrackingNumber}",
            DateTime.UtcNow
        ));
    }

    public async Task Handle(OrderDelivered delivered)
    {
        _logger.LogInformation("Order {OrderId} delivered at {DeliveredAt}", 
            delivered.OrderId, delivered.DeliveredAt);

        var order = await _repository.GetByIdAsync(delivered.OrderId);
        if (order == null) return;

        var oldStatus = order.Status;
        order.Status = OrderStatus.Delivered;
        
        await _repository.UpdateAsync(order);

        // Publish status change
        await _messageBus.PublishAsync(new OrderStatusChanged(
            order.Id,
            oldStatus,
            order.Status,
            $"Order delivered{(delivered.SignedBy != null ? $" (signed by {delivered.SignedBy})" : "")}",
            DateTime.UtcNow
        ));
    }
}