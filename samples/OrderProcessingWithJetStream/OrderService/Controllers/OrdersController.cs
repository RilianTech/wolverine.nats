using Microsoft.AspNetCore.Mvc;
using OrderService.Data;
using Shared.Events;
using Shared.Models;
using Wolverine;

namespace OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderRepository _repository;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IOrderRepository repository,
        IMessageBus messageBus,
        ILogger<OrdersController> logger)
    {
        _repository = repository;
        _messageBus = messageBus;
        _logger = logger;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrder(Guid id)
    {
        var order = await _repository.GetByIdAsync(id);
        if (order == null)
        {
            return NotFound();
        }
        return Ok(order);
    }

    [HttpGet("customer/{customerId}")]
    public async Task<ActionResult<List<Order>>> GetCustomerOrders(string customerId)
    {
        var orders = await _repository.GetByCustomerIdAsync(customerId);
        return Ok(orders);
    }

    [HttpPost]
    public async Task<ActionResult<Order>> CreateOrder(CreateOrderRequest request)
    {
        // Create the order
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            CreatedAt = DateTime.UtcNow,
            Status = OrderStatus.Pending,
            Items = request.Items,
            TotalAmount = request.Items.Sum(i => i.TotalPrice),
            ShippingInfo = request.ShippingInfo,
            PaymentInfo = new PaymentInfo
            {
                PaymentMethod = request.PaymentMethod,
                Status = PaymentStatus.Pending
            }
        };

        // Save to repository
        await _repository.CreateAsync(order);

        // Publish OrderCreated event to JetStream
        var orderCreated = new OrderCreated(
            order.Id,
            order.CustomerId,
            order.Items,
            order.TotalAmount,
            order.ShippingInfo,
            DateTime.UtcNow
        );

        await _messageBus.PublishAsync(orderCreated);
        
        _logger.LogInformation("Created order {OrderId} and published OrderCreated event", order.Id);

        // Also publish status change
        await _messageBus.PublishAsync(new OrderStatusChanged(
            order.Id,
            OrderStatus.Pending,
            OrderStatus.Pending,
            "Order created",
            DateTime.UtcNow
        ));

        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult> CancelOrder(Guid id, [FromBody] CancelOrderRequest request)
    {
        var order = await _repository.GetByIdAsync(id);
        if (order == null)
        {
            return NotFound();
        }

        if (order.Status >= OrderStatus.Shipped)
        {
            return BadRequest("Cannot cancel an order that has been shipped");
        }

        var oldStatus = order.Status;
        order.Status = OrderStatus.Cancelled;
        await _repository.UpdateAsync(order);

        // Publish cancellation event
        await _messageBus.PublishAsync(new OrderCancelled(
            order.Id,
            request.Reason,
            DateTime.UtcNow
        ));

        // Publish status change
        await _messageBus.PublishAsync(new OrderStatusChanged(
            order.Id,
            oldStatus,
            OrderStatus.Cancelled,
            $"Order cancelled: {request.Reason}",
            DateTime.UtcNow
        ));

        _logger.LogInformation("Cancelled order {OrderId} for reason: {Reason}", 
            order.Id, request.Reason);

        return NoContent();
    }
}

// Request DTOs
public class CreateOrderRequest
{
    public string CustomerId { get; set; } = string.Empty;
    public List<OrderItem> Items { get; set; } = new();
    public ShippingInfo ShippingInfo { get; set; } = new();
    public string PaymentMethod { get; set; } = string.Empty;
}

public class CancelOrderRequest
{
    public string Reason { get; set; } = string.Empty;
}