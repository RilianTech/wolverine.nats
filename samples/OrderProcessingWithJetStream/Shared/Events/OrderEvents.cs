using Shared.Models;

namespace Shared.Events;

// Base event for all order-related events
public abstract record OrderEvent(Guid OrderId, DateTime OccurredAt);

// Order lifecycle events
public record OrderCreated(
    Guid OrderId, 
    string CustomerId,
    List<OrderItem> Items,
    decimal TotalAmount,
    ShippingInfo ShippingInfo,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

public record OrderCancelled(
    Guid OrderId,
    string Reason,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

// Inventory events
public record InventoryReserved(
    Guid OrderId,
    Dictionary<string, int> ReservedItems, // ProductId -> Quantity
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

public record InventoryReservationFailed(
    Guid OrderId,
    string Reason,
    List<string> UnavailableProducts,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

public record InventoryReleased(
    Guid OrderId,
    Dictionary<string, int> ReleasedItems,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

// Payment events
public record PaymentRequested(
    Guid OrderId,
    decimal Amount,
    string PaymentMethod,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

public record PaymentCompleted(
    Guid OrderId,
    string TransactionId,
    decimal Amount,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

public record PaymentFailed(
    Guid OrderId,
    string Reason,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

public record PaymentRefunded(
    Guid OrderId,
    string TransactionId,
    decimal Amount,
    string Reason,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

// Shipping events
public record ShippingRequested(
    Guid OrderId,
    ShippingInfo ShippingInfo,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

public record OrderShipped(
    Guid OrderId,
    string TrackingNumber,
    string Carrier,
    DateTime EstimatedDelivery,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

public record OrderDelivered(
    Guid OrderId,
    DateTime DeliveredAt,
    string? SignedBy,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

// Notification events
public record OrderStatusChanged(
    Guid OrderId,
    OrderStatus OldStatus,
    OrderStatus NewStatus,
    string? Message,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

// Command messages (not events, but commands that trigger workflows)
public record CreateOrder(
    string CustomerId,
    List<OrderItem> Items,
    ShippingInfo ShippingInfo,
    string PaymentMethod
);

public record CancelOrder(
    Guid OrderId,
    string Reason
);

// Saga/Process state events
public record OrderProcessingStarted(
    Guid OrderId,
    Guid ProcessId,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);

public record OrderProcessingCompleted(
    Guid OrderId,
    Guid ProcessId,
    bool Success,
    string? FailureReason,
    DateTime OccurredAt
) : OrderEvent(OrderId, OccurredAt);