#!/bin/bash

echo "Starting OrderProcessing with JetStream sample..."
echo ""

# Start NATS server with docker-compose
echo "Starting NATS server with JetStream enabled..."
cd ../.. # Go to root directory where docker-compose.yml is
docker compose up -d
cd samples/OrderProcessingWithJetStream

# Wait for NATS to be ready
echo "Waiting for NATS server to be ready..."
sleep 5

echo "NATS server started!"
echo ""

# Function to run a service in the background
run_service() {
    local service=$1
    echo "Starting $service..."
    cd $service
    dotnet run &
    cd ..
}

# Start all services
run_service "OrderService"
sleep 2
run_service "InventoryService"
sleep 2
run_service "PaymentService"

echo ""
echo "All services started!"
echo "OrderService API is available at: http://localhost:5000/swagger"
echo ""
echo "To test, send a POST request to http://localhost:5000/api/orders with:"
echo '{'
echo '  "customerId": "12345",'
echo '  "items": ['
echo '    {'
echo '      "productId": "PROD-001",'
echo '      "productName": "Widget",'
echo '      "quantity": 2,'
echo '      "unitPrice": 19.99'
echo '    }'
echo '  ]'
echo '}'
echo ""
echo "Press Ctrl+C to stop all services"

# Cleanup function
cleanup() {
    echo ""
    echo "Stopping services..."
    # Kill all background jobs
    jobs -p | xargs -r kill
    
    echo "Stopping NATS server..."
    cd ../..
    docker compose down
    cd samples/OrderProcessingWithJetStream
    
    echo "Cleanup complete!"
    exit 0
}

# Set up trap to call cleanup on Ctrl+C
trap cleanup INT

# Wait for Ctrl+C
wait