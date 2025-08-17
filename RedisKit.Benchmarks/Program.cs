using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Environments;

namespace RedisKit.Benchmarks;

public static class Program
{
   
    public static void Main(string[] args)
    {
        var config = DefaultConfig.Instance;

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}