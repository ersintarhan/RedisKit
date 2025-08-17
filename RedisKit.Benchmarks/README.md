# RedisLib Performance Benchmarks

This project contains performance benchmarks for the RedisLib library using BenchmarkDotNet.

## Benchmark Categories

### 1. **SerializerBenchmarks**

Tests the performance of different serialization methods:

- **JSON vs MessagePack** serialization/deserialization
- **Small, Large, and Array** object scenarios
- **Sync vs Async** operations
- **Memory allocation** analysis

### 2. **CacheBenchmarks**

Tests cache operation performance:

- **Single vs Batch** operations
- **Set/Get/Delete** operations
- **Pipeline** effectiveness
- **TTL handling** performance

### 3. **PubSubBenchmarks**

Tests pub/sub operation performance:

- **Single vs Multiple** channel publishing
- **Pattern subscriptions** performance
- **Subscribe/Unsubscribe** overhead
- **Parallel vs Sequential** publishing

## Running Benchmarks

### Prerequisites

- .NET 9.0 SDK
- Redis server running (for integration benchmarks)

### Commands

```bash
# Run all benchmarks
dotnet run -c Release

# Run specific benchmark class
dotnet run -c Release --filter "*SerializerBenchmarks*"

# Run specific benchmark method
dotnet run -c Release --filter "*Json_Serialize_Small*"

# Generate detailed reports
dotnet run -c Release --exporters json,html,csv
```

### Example Commands

```bash
# Serializer performance comparison
dotnet run -c Release --filter "*SerializerBenchmarks*"

# Cache operation performance
dotnet run -c Release --filter "*CacheBenchmarks*"

# Pub/Sub performance
dotnet run -c Release --filter "*PubSubBenchmarks*"
```

## Benchmark Results

### Expected Performance Characteristics

#### **Serialization Performance**

- **MessagePack**: ~2-5x faster than JSON, ~60% smaller payload
- **JSON**: More readable, better tooling support
- **Memory**: MessagePack generates less garbage

#### **Cache Operations**

- **Batch operations**: ~5-10x more efficient than individual operations
- **Pipeline**: Reduces network round trips significantly
- **Memory**: Efficient object pooling reduces allocations

#### **Pub/Sub Operations**

- **Pattern subscriptions**: Slightly higher overhead than direct subscriptions
- **Parallel publishing**: Better throughput for multiple channels
- **Subscribe/Unsubscribe**: Low overhead for dynamic subscriptions

## Benchmark Environment

The benchmarks test the library components in isolation where possible, using mocked Redis connections for pure library performance testing. For end-to-end performance testing with real Redis, see the integration test suite.

## Interpreting Results

BenchmarkDotNet provides several metrics:

- **Mean**: Average execution time
- **Error**: Half of the 99% confidence interval
- **StdDev**: Standard deviation of all measurements
- **Median**: Value separating the higher half from lower half
- **Ratio**: Ratio to baseline (if specified)
- **Gen 0/1/2**: Number of GC collections per 1000 operations
- **Allocated**: Allocated memory per operation

### Performance Targets

- **Serialization**: < 1ms for typical objects
- **Cache Get/Set**: < 0.5ms for single operations
- **Batch Operations**: < 5ms for 100 items
- **Pub/Sub**: < 0.5ms for message publishing
- **Memory**: < 1KB allocation per operation

## Configuration

Benchmarks use production-like configuration:

- **Release build** for accurate performance measurement
- **MessagePack** serialization for optimal performance
- **Connection pooling** enabled
- **Logging** minimized to reduce overhead

## CI Integration

These benchmarks can be integrated into CI/CD pipelines to:

- **Track performance regressions** over time
- **Compare branches** for performance impact
- **Generate performance reports** for releases
- **Set performance gates** for pull requests