# Wolverine.Nats Makefile
# Provides convenient commands for building, testing, and managing the development environment
#
# Configuration:
# - Applications use appsettings.json for NATS configuration (under Wolverine:Nats section)
# - NATS_URL environment variable overrides appsettings.json (useful for CI/CD)
# - Tests use NATS_URL environment variable directly
# - Default connection: nats://localhost:4222

.PHONY: all help check-docker nats-start nats-stop nats-clean nats-status nats-logs \
        restore build test test-integration test-all test-specific clean clean-all \
        dev-setup ci watch format run-sample list-samples pack nats-alt info

# Variables
NATS_CONTAINER_NAME = wolverine-nats-dev
NATS_PORT = 4222
NATS_MONITOR_PORT = 8222
NATS_IMAGE = nats:latest
DOTNET_CONFIG = Release
DOTNET_FRAMEWORK = net9.0

# Default target
.DEFAULT_GOAL := help

# Help command
.PHONY: help
help: ## Show this help message
	@echo "Wolverine.Nats Development Commands"
	@echo "==================================="
	@echo ""
	@echo "Usage: make [target]"
	@echo ""
	@echo "Targets:"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  %-20s %s\n", $$1, $$2}'

# Check if Docker is running
.PHONY: check-docker
check-docker:
	@if ! docker info > /dev/null 2>&1; then \
		echo "Error: Docker is not running. Please start Docker first."; \
		exit 1; \
	fi

# Start NATS container using docker-compose
.PHONY: nats-compose
nats-compose: check-docker ## Start NATS server using docker-compose with JetStream enabled
	@echo "Starting NATS server with docker-compose..."
	@docker compose up -d
	@echo "Waiting for NATS to be ready..."
	@sleep 2
	@echo "NATS server is running on port $(NATS_PORT)"
	@echo "NATS monitoring UI available at http://localhost:$(NATS_MONITOR_PORT)"

# Start NATS container
.PHONY: nats-start
nats-start: check-docker ## Start NATS server in Docker with JetStream enabled
	@echo "Starting NATS server..."
	@if docker ps -a --format '{{.Names}}' | grep -q "^$(NATS_CONTAINER_NAME)$$"; then \
		echo "NATS container already exists. Starting it..."; \
		docker start $(NATS_CONTAINER_NAME); \
	else \
		echo "Creating new NATS container..."; \
		docker run -d \
			--name $(NATS_CONTAINER_NAME) \
			-p $(NATS_PORT):4222 \
			-p $(NATS_MONITOR_PORT):8222 \
			-v $$(pwd)/nats.conf:/etc/nats/nats.conf \
			-v $$(pwd)/nats-data:/data \
			$(NATS_IMAGE) \
			-c /etc/nats/nats.conf; \
	fi
	@echo "Waiting for NATS to be ready..."
	@sleep 2
	@echo "NATS server is running on port $(NATS_PORT)"
	@echo "NATS monitoring UI available at http://localhost:$(NATS_MONITOR_PORT)"

# Stop NATS container using docker compose
.PHONY: nats-compose-down
nats-compose-down: ## Stop NATS server started with docker compose
	@echo "Stopping NATS server (docker compose)..."
	@docker compose down

# Stop NATS container
.PHONY: nats-stop
nats-stop: ## Stop NATS server
	@echo "Stopping NATS server..."
	@docker stop $(NATS_CONTAINER_NAME) 2>/dev/null || true

# Remove NATS container
.PHONY: nats-clean
nats-clean: nats-stop ## Stop and remove NATS container and data
	@echo "Removing NATS container..."
	@docker rm $(NATS_CONTAINER_NAME) 2>/dev/null || true
	@echo "Cleaning NATS data..."
	@rm -rf nats-data

# Check NATS status
.PHONY: nats-status
nats-status: ## Check if NATS is running
	@if nc -z localhost $(NATS_PORT) 2>/dev/null; then \
		echo "✓ NATS is available on port $(NATS_PORT)"; \
		if docker ps --format '{{.Names}}' | grep -q "^$(NATS_CONTAINER_NAME)$$"; then \
			echo "  Running in container: $(NATS_CONTAINER_NAME)"; \
		else \
			echo "  Running outside of this Makefile's control"; \
		fi \
	else \
		echo "✗ NATS is not available on port $(NATS_PORT)"; \
		echo "Run 'make nats-start' to start NATS"; \
	fi

# View NATS logs
.PHONY: nats-logs
nats-logs: ## View NATS server logs
	@docker logs -f $(NATS_CONTAINER_NAME)

# Restore NuGet packages
.PHONY: restore
restore: ## Restore NuGet packages
	@echo "Restoring NuGet packages..."
	@dotnet restore

# Build the project
.PHONY: build
build: restore ## Build the project
	@echo "Building Wolverine.Nats..."
	@dotnet build --configuration $(DOTNET_CONFIG) --no-restore

# Run unit tests
.PHONY: test
test: build ## Run unit tests (non-integration tests)
	@echo "Running unit tests..."
	@dotnet test --configuration $(DOTNET_CONFIG) --no-build --filter "Category!=Integration" --framework $(DOTNET_FRAMEWORK)

# Run integration tests
# Note: NATS_URL environment variable takes precedence over appsettings.json
# Examples:
#   make test-integration                    # Uses default port 4222
#   NATS_URL=nats://localhost:4223 make test-integration  # Use custom port
.PHONY: test-integration
test-integration: build ## Run integration tests (requires NATS)
	@if ! nc -z localhost $(NATS_PORT) 2>/dev/null; then \
		echo "NATS is not available on port $(NATS_PORT). Starting it..."; \
		$(MAKE) nats-start; \
	else \
		echo "✓ NATS is available on port $(NATS_PORT)"; \
	fi
	@echo "Running integration tests..."
	@if [ -n "$$NATS_URL" ]; then \
		echo "Using NATS_URL=$$NATS_URL"; \
		dotnet test --configuration $(DOTNET_CONFIG) --no-build --filter "Category=Integration" --framework $(DOTNET_FRAMEWORK); \
	else \
		NATS_URL=nats://localhost:$(NATS_PORT) dotnet test --configuration $(DOTNET_CONFIG) --no-build --filter "Category=Integration" --framework $(DOTNET_FRAMEWORK); \
	fi

# Run all tests
.PHONY: test-all
test-all: test test-integration ## Run all tests (unit and integration)

# Run a specific test
.PHONY: test-specific
test-specific: build ## Run a specific test (usage: make test-specific TEST=TestName)
	@if [ -z "$(TEST)" ]; then \
		echo "Error: Please specify a test name using TEST=TestName"; \
		exit 1; \
	fi
	@echo "Running test: $(TEST)..."
	@NATS_URL=nats://localhost:$(NATS_PORT) dotnet test --configuration $(DOTNET_CONFIG) --no-build --filter "FullyQualifiedName~$(TEST)" --framework $(DOTNET_FRAMEWORK)

# Clean build artifacts
.PHONY: clean
clean: ## Clean build artifacts
	@echo "Cleaning build artifacts..."
	@dotnet clean --configuration $(DOTNET_CONFIG)
	@find . -type d -name "bin" -o -name "obj" | xargs rm -rf

# Full clean (including NATS)
.PHONY: clean-all
clean-all: clean nats-clean ## Clean everything (build artifacts and NATS)

# Quick start for development
.PHONY: dev-setup
dev-setup: nats-start restore build ## Set up development environment (start NATS, restore, build)
	@echo ""
	@echo "✓ Development environment ready!"
	@echo ""
	@echo "NATS is running on port $(NATS_PORT)"
	@echo "Run 'make test-all' to run all tests"
	@echo "Run 'make nats-logs' to view NATS logs"

# CI simulation
.PHONY: ci
ci: ## Run CI pipeline locally (clean, build, test all)
	@echo "Running CI pipeline..."
	@$(MAKE) clean
	@$(MAKE) nats-start
	@$(MAKE) build
	@$(MAKE) test-all
	@echo ""
	@echo "✓ CI pipeline completed successfully!"

# Watch for changes and run tests
.PHONY: watch
watch: ## Watch for changes and run tests (requires watchexec)
	@if ! command -v watchexec > /dev/null; then \
		echo "Error: watchexec is not installed. Install it with: brew install watchexec"; \
		exit 1; \
	fi
	@echo "Watching for changes..."
	@watchexec -e cs -w src -w tests -- make test

# Format code
.PHONY: format
format: ## Format code using dotnet format
	@echo "Formatting code..."
	@dotnet format

# Run samples
.PHONY: run-sample
run-sample: build nats-status ## Run a sample (usage: make run-sample SAMPLE=PingPong)
	@if [ -z "$(SAMPLE)" ]; then \
		echo "Error: Please specify a sample name using SAMPLE=SampleName"; \
		echo "Available samples:"; \
		ls -1 samples/*/; \
		exit 1; \
	fi
	@if ! docker ps --format '{{.Names}}' | grep -q "^$(NATS_CONTAINER_NAME)$$"; then \
		echo "NATS is not running. Starting it..."; \
		$(MAKE) nats-start; \
	fi
	@echo "Running sample: $(SAMPLE)..."
	@cd samples/$(SAMPLE) && NATS_URL=nats://localhost:$(NATS_PORT) dotnet run

# List available samples
.PHONY: list-samples
list-samples: ## List available sample projects
	@echo "Available samples:"
	@ls -1 samples/*/

# Package NuGet
.PHONY: pack
pack: build ## Create NuGet package
	@echo "Creating NuGet package..."
	@dotnet pack --configuration $(DOTNET_CONFIG) --no-build --output ./artifacts

# Alternative NATS setup on different port
.PHONY: nats-alt
nats-alt: check-docker ## Start NATS on alternative port 4223 (for local dev alongside other NATS instances)
	@echo "Starting NATS server on alternative port 4223..."
	@docker run -d \
		--name $(NATS_CONTAINER_NAME)-alt \
		-p 4223:4222 \
		-p 8223:8222 \
		-v $$(pwd)/nats.conf:/etc/nats/nats.conf \
		-v $$(pwd)/nats-data-alt:/data \
		$(NATS_IMAGE) \
		-c /etc/nats/nats.conf
	@echo "Alternative NATS server is running on port 4223"
	@echo "Set NATS_URL=nats://localhost:4223 when running tests"

.PHONY: info
info: ## Show project information
	@echo "Wolverine.Nats Project Information"
	@echo "================================="
	@echo ""
	@echo "Configuration: $(DOTNET_CONFIG)"
	@echo "Target Framework: $(DOTNET_FRAMEWORK)"
	@echo "NATS Port: $(NATS_PORT)"
	@echo "NATS Container: $(NATS_CONTAINER_NAME)"
	@echo ""
	@dotnet --list-sdks
	@echo ""
	@dotnet --list-runtimes