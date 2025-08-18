#!/bin/bash
# Jules Environment Setup Script for RedisKit
# This script sets up .NET 9 SDK and Redis for development

set -e  # Exit on error

echo "========================================="
echo "RedisKit Development Environment Setup"
echo "========================================="

# 1. Install .NET 9 SDK
echo "📦 Installing .NET 9 SDK..."
wget -q https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
rm dotnet-install.sh

# Verify .NET installation
echo "✅ .NET SDK installed:"
dotnet --version
dotnet --list-sdks

# 2. Install Redis Server (latest stable)
echo "📦 Installing Redis Server..."
sudo apt-get update -qq
sudo apt-get install -y redis-server redis-tools

# Start Redis service
echo "🚀 Starting Redis..."
sudo service redis-server start

# Verify Redis is running
redis-cli ping && echo "✅ Redis is running"

# 3. Restore NuGet packages
echo "📦 Restoring NuGet packages..."
dotnet restore

# 4. Build the solution
echo "🔨 Building solution..."
dotnet build --no-restore

# 5. Run unit tests (skip integration tests that need Redis)
echo "🧪 Running unit tests..."
dotnet test --no-build --filter "Category!=Integration" || true

# 6. Display environment summary
echo ""
echo "========================================="
echo "✅ Environment Setup Complete!"
echo "========================================="
echo ".NET Version: $(dotnet --version)"
echo "Redis Version: $(redis-server --version | head -n1)"
echo "Redis Status: $(redis-cli ping)"
echo ""
echo "Available commands:"
echo "  dotnet build         - Build the solution"
echo "  dotnet test          - Run all tests"
echo "  dotnet run           - Run the example project"
echo "  redis-cli            - Connect to Redis"
echo "========================================="