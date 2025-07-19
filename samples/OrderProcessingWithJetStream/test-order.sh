#!/bin/bash

# Create a test order
echo "Creating a test order..."

curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "CUST-12345",
    "items": [
      {
        "productId": "PROD-001",
        "productName": "Widget",
        "quantity": 2,
        "unitPrice": 19.99
      },
      {
        "productId": "PROD-002",
        "productName": "Gadget",
        "quantity": 1,
        "unitPrice": 49.99
      }
    ]
  }'

echo ""
echo "Order created! Check the service logs to see the event flow."