```

BenchmarkDotNet v0.14.0, macOS Sequoia 15.5 (24F74) [Darwin 24.5.0]
Apple M1 Max, 1 CPU, 10 logical and 10 physical cores
.NET SDK 9.0.200
  [Host]     : .NET 9.0.2 (9.0.225.6610), Arm64 RyuJIT AdvSIMD
  DefaultJob : .NET 9.0.2 (9.0.225.6610), Arm64 RyuJIT AdvSIMD


```
| Method               | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| Set_Single_Object    | 121.8 μs | 0.65 μs | 0.58 μs |  1.00 |      - |     792 B |        1.00 |
| Get_Single_Object    | 120.8 μs | 0.81 μs | 0.76 μs |  0.99 |      - |    1224 B |        1.55 |
| Set_Many_Objects_10  | 148.3 μs | 0.52 μs | 0.43 μs |  1.22 | 0.9766 |    6712 B |        8.47 |
| Get_Many_Objects_10  | 133.5 μs | 1.05 μs | 0.98 μs |  1.10 | 1.2207 |    8553 B |       10.80 |
| Set_With_Expiry      | 121.9 μs | 0.75 μs | 0.70 μs |  1.00 |      - |     792 B |        1.00 |
| Delete_Single        | 120.4 μs | 0.85 μs | 0.80 μs |  0.99 |      - |     520 B |        0.66 |
| Exists_Check         | 120.1 μs | 0.56 μs | 0.44 μs |  0.99 |      - |     520 B |        0.66 |
| Set_And_Get_Pipeline | 240.7 μs | 0.83 μs | 0.77 μs |  1.98 |      - |    1857 B |        2.34 |
