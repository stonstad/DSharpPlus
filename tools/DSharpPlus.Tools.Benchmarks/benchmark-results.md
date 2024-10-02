### Machine Information:
BenchmarkDotNet v0.14.0, Void Linux
- AMD Ryzen 7 6800H with Radeon Graphics, 1 CPU, 16 logical and 8 physical cores
- Hardware Intrinsics: AVX2, AES, BMI1, BMI2, FMA, LZCNT, PCLMUL, POPCNT VectorSize=256
- .NET 9.0.0 (9.0.24.43107), x64, RyuJIT
- Total Execution Time: 3m and 54.079s

## TextCommandMapExecutionBenchmarks
Execution Time: 3m and 54.079s
### ExecuteEightArgsParamsCommandAsync:
Mean: 4.22μs
Error: 12ns
StdDev: 48ns
Max per second: 236,758.63 (1,000,000,000ns / 4,223.71ns)
### ExecuteFiveArgsParamsCommandAsync:
Mean: 4.35μs
Error: 22ns
StdDev: 100ns
Max per second: 229,931.64 (1,000,000,000ns / 4,349.12ns)
### ExecuteFourArgsParamsCommandAsync:
Mean: 4.39μs
Error: 11ns
StdDev: 42ns
Max per second: 227,894.62 (1,000,000,000ns / 4,387.99ns)
### ExecuteNineArgsParamsCommandAsync:
Mean: 4.17μs
Error: 10ns
StdDev: 38ns
Max per second: 239,648.19 (1,000,000,000ns / 4,172.78ns)
### ExecuteNoArgsParamsCommandAsync:
Mean: 815ns
Error: 4ns
StdDev: 15ns
Max per second: 1,226,558.32 (1,000,000,000ns / 815.29ns)
### ExecuteOneArgParamsCommandAsync:
Mean: 4.30μs
Error: 7ns
StdDev: 28ns
Max per second: 232,447.02 (1,000,000,000ns / 4,302.06ns)
### ExecuteSevenArgsParamsCommandAsync:
Mean: 4.17μs
Error: 9ns
StdDev: 33ns
Max per second: 239,568.48 (1,000,000,000ns / 4,174.17ns)
### ExecuteSixArgsParamsCommandAsync:
Mean: 4.42μs
Error: 23ns
StdDev: 109ns
Max per second: 226,173.53 (1,000,000,000ns / 4,421.38ns)
### ExecuteTenArgsParamsCommandAsync:
Mean: 4.26μs
Error: 8ns
StdDev: 33ns
Max per second: 234,538.58 (1,000,000,000ns / 4,263.69ns)
### ExecuteThreeArgsParamsCommandAsync:
Mean: 4.36μs
Error: 21ns
StdDev: 85ns
Max per second: 229,307.00 (1,000,000,000ns / 4,360.97ns)
### ExecuteTwoArgsParamsCommandAsync:
Mean: 4.17μs
Error: 10ns
StdDev: 38ns
Max per second: 239,676.35 (1,000,000,000ns / 4,172.29ns)