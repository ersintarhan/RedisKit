# Integration Tests

This directory contains integration tests that require a real Redis instance.

## Prerequisites

- Redis server running on localhost:6379
- Redis 7.0+ for Redis Functions and Sharded Pub/Sub tests

## Running Integration Tests

```bash
# Run all integration tests
dotnet test --filter "FullyQualifiedName~Integration"

# Run specific service tests
dotnet test --filter "FullyQualifiedName~RedisFunctionIntegrationTests"
dotnet test --filter "FullyQualifiedName~RedisShardedPubSubIntegrationTests"
```

## Test Categories

### Redis Functions Tests

- Requires Redis 7.0+ with FUNCTION command support
- Tests will be skipped if Redis Functions are not available
- Verifies function loading, calling, and management

### Sharded Pub/Sub Tests

- Requires Redis 7.0+ with Sharded Pub/Sub support
- Tests message publishing and subscription across shards
- Pattern subscriptions are not supported (Redis limitation)

### Core Service Tests

- Cache operations
- Regular Pub/Sub
- Streams
- Distributed locking

## Known Issues

1. **Redis Functions Detection**: Some Redis installations may not properly expose the FUNCTION command even on Redis 7.0+. This can be due to:
    - ACL restrictions
    - Redis configuration
    - Module loading issues

2. **Test Isolation**: Tests use database 15 by default and unique key prefixes to avoid conflicts

## Configuration

Tests use the following defaults:

- Connection: localhost:6379
- Database: 15
- Key Prefix: `test:{guid}:`

These can be overridden in the `IntegrationTestBase` class.