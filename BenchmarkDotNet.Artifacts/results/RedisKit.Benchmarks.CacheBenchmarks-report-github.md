```

BenchmarkDotNet v0.15.2, macOS Sequoia 15.6 (24G84) [Darwin 24.6.0]
Apple M1 Max, 1 CPU, 10 logical and 10 physical cores
.NET SDK 9.0.200
  [Host]     : .NET 9.0.2 (9.0.225.6610), Arm64 RyuJIT AdvSIMD
  Job-FEWCWF : .NET 9.0.2 (9.0.225.6610), Arm64 RyuJIT AdvSIMD

InvocationCount=1  IterationCount=3  UnrollFactor=1  
WarmupCount=1  

```
| Method                   | Mean     | Error      | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------- |---------:|-----------:|----------:|------:|--------:|----------:|------------:|
| Set_Single_Object        | 234.5 μs |   318.0 μs |  17.43 μs |  1.00 |    0.09 |    1552 B |        1.00 |
| Get_Single_Object        | 323.9 μs | 2,101.5 μs | 115.19 μs |  1.39 |    0.44 |    1352 B |        0.87 |
| Set_Many_Objects_10      | 628.9 μs | 3,653.9 μs | 200.28 μs |  2.69 |    0.77 |   17048 B |       10.98 |
| Get_Many_Objects_10      | 279.6 μs |   704.1 μs |  38.60 μs |  1.20 |    0.16 |   12856 B |        8.28 |
| Set_With_Expiry          | 233.2 μs |   147.1 μs |   8.06 μs |  1.00 |    0.07 |    1096 B |        0.71 |
| Delete_Single            | 194.6 μs |   536.7 μs |  29.42 μs |  0.83 |    0.12 |     680 B |        0.44 |
| Exists_Check             | 203.1 μs |   535.3 μs |  29.34 μs |  0.87 |    0.12 |     696 B |        0.45 |
| Set_And_Get_Pipeline     | 426.3 μs | 1,741.8 μs |  95.47 μs |  1.82 |    0.38 |    2288 B |        1.47 |
| ExecuteBatch_Get_And_Set | 236.8 μs |   962.4 μs |  52.75 μs |  1.01 |    0.21 |    2784 B |        1.79 |
| SetBytes_And_GetBytes    | 312.5 μs |   561.3 μs |  30.77 μs |  1.34 |    0.15 |    1432 B |        0.92 |
