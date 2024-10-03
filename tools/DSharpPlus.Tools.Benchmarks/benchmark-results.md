### Machine Information:
BenchmarkDotNet v0.14.0, Void Linux
- AMD Ryzen 7 6800H with Radeon Graphics, 1 CPU, 16 logical and 8 physical cores
- Hardware Intrinsics: AVX2, AES, BMI1, BMI2, FMA, LZCNT, PCLMUL, POPCNT VectorSize=256
- .NET 9.0.0 (9.0.24.43107), x64, RyuJIT
- Total Execution Time: 4m and 51.144s

## EnumConverterBenchmarks
Execution Time: 4m and 51.144s
### ConvertEnumAsync:
Mean: 61ns
Error: 0ns
StdDev: 2ns
Max per second: 16,385,408.52 (1,000,000,000ns / 61.03ns)
### ConvertEnumFailAsync:
Mean: 51ns
Error: 0ns
StdDev: 1ns
Max per second: 19,649,567.78 (1,000,000,000ns / 50.89ns)
### ConvertGenericEnumAsync:
Mean: 36ns
Error: 0ns
StdDev: 0ns
Max per second: 27,775,292.56 (1,000,000,000ns / 36.00ns)
### ConvertGenericEnumFailAsync:
Mean: 33ns
Error: 0ns
StdDev: 0ns
Max per second: 30,568,683.88 (1,000,000,000ns / 32.71ns)
### ConvertGenericIntAsync:
Mean: 31ns
Error: 0ns
StdDev: 1ns
Max per second: 31,875,507.57 (1,000,000,000ns / 31.37ns)
### ConvertGenericIntFailAsync:
Mean: 23ns
Error: 0ns
StdDev: 0ns
Max per second: 43,096,057.22 (1,000,000,000ns / 23.20ns)
### ConvertIntAsync:
Mean: 53ns
Error: 0ns
StdDev: 1ns
Max per second: 18,982,821.18 (1,000,000,000ns / 52.68ns)
### ConvertIntFailAsync:
Mean: 41ns
Error: 0ns
StdDev: 0ns
Max per second: 24,683,564.41 (1,000,000,000ns / 40.51ns)