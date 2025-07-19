#!/bin/bash
echo "Testing InventoryService in isolation..."
echo "Make sure NATS is running with: docker-compose up -d"
echo ""

cd "$(dirname "$0")"
dotnet run --project InventoryService/InventoryService.csproj