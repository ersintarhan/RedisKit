## Environment Setup

This is a .NET 9 project that requires Redis for full functionality.

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

## Code Quality Standards

### Architecture Patterns
- All Redis operations use `RedisOperationExecutor` for centralized exception handling
- Services follow dependency injection pattern with interface segregation
- Circuit breaker pattern implemented for fault tolerance
- Async/await patterns throughout with proper ConfigureAwait(false)

### Testing Standards
- Unit tests use NSubstitute for mocking Redis dependencies
- Integration tests marked with `[Category("Integration")]` attribute
- All test methods should have proper assertions (SonarCloud compliant)
- Test data builders used for complex scenarios

### Code Review Guidelines
- Check for consistent use of RedisOperationExecutor in all Redis services
- Verify proper exception handling and logging
- Ensure thread-safety in concurrent operations
- Validate serialization/deserialization patterns
- Review performance implications of Redis operations

## Notes for AI Agents

1. Always check if Redis is running before running integration tests
2. Use `Category!=Integration` filter for tests if Redis is not available
3. The project uses source-generated logging for performance
4. All services use dependency injection and should be registered via `AddRedisKit()`
5. The library supports both JSON and MessagePack serialization
6. RedisOperationExecutor provides centralized exception handling - all new Redis operations should use it
7. Circuit breaker pattern protects against Redis failures - don't bypass it
8. All Redis keys should be validated using ValidateRedisKey() method