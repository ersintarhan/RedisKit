```

BenchmarkDotNet v0.14.0, macOS Sequoia 15.5 (24F74) [Darwin 24.5.0]
Apple M1 Max, 1 CPU, 10 logical and 10 physical cores
.NET SDK 9.0.200
  [Host]     : .NET 9.0.2 (9.0.225.6610), Arm64 RyuJIT AdvSIMD
  DefaultJob : .NET 9.0.2 (9.0.225.6610), Arm64 RyuJIT AdvSIMD


```

| Method                             |        Mean |     Error |    StdDev | Ratio | RatioSD |   Gen0 | Allocated | Alloc Ratio |
|------------------------------------|------------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Json_Serialize_Small               |    331.8 ns |   0.71 ns |   0.63 ns |  1.00 |    0.00 | 0.0854 |     536 B |        1.00 |
| MessagePack_Serialize_Small        |    143.2 ns |   0.22 ns |   0.21 ns |  0.43 |    0.00 | 0.0153 |      96 B |        0.18 |
| Json_Serialize_Large               |  3,569.1 ns |  71.11 ns |  94.93 ns | 10.76 |    0.28 | 1.6365 |   10288 B |       19.19 |
| MessagePack_Serialize_Large        |  1,940.7 ns |  26.77 ns |  25.04 ns |  5.85 |    0.07 | 1.5373 |    9664 B |       18.03 |
| Json_Serialize_Array               | 28,143.8 ns | 555.47 ns | 594.35 ns | 84.82 |    1.75 | 2.9602 |   18640 B |       34.78 |
| MessagePack_Serialize_Array        | 11,556.8 ns |  22.25 ns |  19.73 ns | 34.83 |    0.09 | 0.9155 |    5800 B |       10.82 |
| Json_Deserialize_Small             |    628.0 ns |   2.22 ns |   2.08 ns |  1.89 |    0.01 | 0.1688 |    1064 B |        1.99 |
| MessagePack_Deserialize_Small      |    256.5 ns |   1.03 ns |   0.91 ns |  0.77 |    0.00 | 0.0801 |     504 B |        0.94 |
| Json_SerializeAsync_Small          |    355.9 ns |   3.26 ns |   3.05 ns |  1.07 |    0.01 | 0.1082 |     680 B |        1.27 |
| MessagePack_SerializeAsync_Small   |    173.8 ns |   1.19 ns |   1.11 ns |  0.52 |    0.00 | 0.0381 |     240 B |        0.45 |
| Json_DeserializeAsync_Small        |    823.8 ns |   1.33 ns |   1.25 ns |  2.48 |    0.01 | 0.2022 |    1272 B |        2.37 |
| MessagePack_DeserializeAsync_Small |    290.0 ns |   0.83 ns |   0.69 ns |  0.87 |    0.00 | 0.1030 |     648 B |        1.21 |
