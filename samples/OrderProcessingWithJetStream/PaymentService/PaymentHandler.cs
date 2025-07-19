using Microsoft.Extensions.Logging;
using Shared.Events;
using Wolverine;

namespace PaymentService;

public class PaymentHandler
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<PaymentHandler> _logger;
    private static readonly Random _random = new();

    public PaymentHandler(IMessageBus messageBus, ILogger<PaymentHandler> logger)
    {
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task Handle(PaymentRequested paymentRequested)
    {
        _logger.LogInformation("Processing payment for order {OrderId}, amount: ${Amount:F2}",
            paymentRequested.OrderId, paymentRequested.Amount);

        // Simulate payment processing delay
        await Task.Delay(TimeSpan.FromSeconds(_random.Next(1, 3)));

        // Simulate 90% success rate
        var success = _random.Next(100) < 90;

        if (success)
        {
            var transactionId = $"TXN-{Guid.NewGuid():N}".Substring(0, 16).ToUpper();
            
            await _messageBus.PublishAsync(new PaymentCompleted(
                paymentRequested.OrderId,
                transactionId,
                paymentRequested.Amount,
                DateTime.UtcNow
            ));
            
            _logger.LogInformation("Payment completed for order {OrderId}. Transaction: {TransactionId}",
                paymentRequested.OrderId, transactionId);
        }
        else
        {
            // Simulate different failure reasons
            var failureReasons = new[]
            {
                "Insufficient funds",
                "Card declined",
                "Payment gateway timeout",
                "Invalid payment method",
                "Fraud detection triggered"
            };
            
            var reason = failureReasons[_random.Next(failureReasons.Length)];
            
            await _messageBus.PublishAsync(new PaymentFailed(
                paymentRequested.OrderId,
                reason,
                DateTime.UtcNow
            ));
            
            _logger.LogWarning("Payment failed for order {OrderId}. Reason: {Reason}",
                paymentRequested.OrderId, reason);
        }
    }
}

public class PaymentProcessingException : Exception
{
    public PaymentProcessingException(string message) : base(message) { }
}