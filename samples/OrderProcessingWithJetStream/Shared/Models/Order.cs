namespace Shared.Models;

public class Order
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public OrderStatus Status { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public ShippingInfo? ShippingInfo { get; set; }
    public PaymentInfo? PaymentInfo { get; set; }
}

public class OrderItem
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice => Quantity * UnitPrice;
}

public class ShippingInfo
{
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string? TrackingNumber { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? EstimatedDelivery { get; set; }
}

public class PaymentInfo
{
    public string PaymentMethod { get; set; } = string.Empty;
    public string? TransactionId { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public PaymentStatus Status { get; set; }
}

public enum OrderStatus
{
    Pending,
    InventoryReserved,
    PaymentProcessing,
    PaymentCompleted,
    Preparing,
    Shipped,
    Delivered,
    Cancelled,
    Failed
}

public enum PaymentStatus
{
    Pending,
    Authorized,
    Captured,
    Failed,
    Refunded
}