# Jules AI Agent Configuration

## Environment Setup

This is a .NET 9 project that requires Redis for full functionality.

### Quick Setup
Run the following command to set up the development environment:
```bash
bash jules-setup.sh
```

### Manual Setup
If automatic setup fails, use these commands:

```bash
# Install .NET 9 SDK
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet

# Install Redis
sudo apt-get update && sudo apt-get install -y redis-server
sudo service redis-server start

# Build and test
dotnet restore
dotnet build
dotnet test --filter "Category!=Integration"
```

## Project Structure

- **RedisKit/**: Main library implementation
- **RedisKit.Tests/**: Unit and integration tests
- **RedisKit.Example/**: Example usage application
- **RedisKit.Benchmarks/**: Performance benchmarks

## Common Tasks

### Running Tests
```bash
# Unit tests only (no Redis required)
dotnet test --filter "Category!=Integration"

# All tests (requires Redis)
dotnet test
```

### Building
```bash
dotnet build
```

### Creating NuGet Package
```bash
dotnet pack -c Release
```

## Key Information

- **Framework**: .NET 9.0
- **Dependencies**: StackExchange.Redis, MessagePack, System.Text.Json
- **Testing**: xUnit with NSubstitute for mocking
- **Required Services**: Redis 5.0+ (6.0+ recommended)

## Notes for AI Agents

1. Always check if Redis is running before running integration tests
2. Use `Category!=Integration` filter for tests if Redis is not available
3. The project uses source-generated logging for performance
4. All services use dependency injection and should be registered via `AddRedisKit()`
5. The library supports both JSON and MessagePack serialization