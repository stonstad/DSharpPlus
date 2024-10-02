using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Portability.Cpu;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace DSharpPlus.Tools.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        Type[] types = typeof(Program).Assembly.GetExportedTypes().Where(type => type.Namespace!.StartsWith("DSharpPlus.Tools.Benchmarks.Cases", StringComparison.OrdinalIgnoreCase)).ToArray();
        IConfig config = ManualConfig.CreateMinimumViable().AddColumn([StatisticColumn.Max, StatisticColumn.Min]).AddDiagnoser([new MemoryDiagnoser(new())]).WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));
#if DEBUG
        Summary[] summaries = BenchmarkRunner.Run(types, config.AddJob(JobMode<Job>.Default.WithToolchain(new InProcessEmitToolchain(TimeSpan.FromHours(1.0), logOutput: true))).WithOptions(ConfigOptions.DisableOptimizationsValidator).AddJob(Job.Dry), args);
#else
        Summary[] summaries = BenchmarkRunner.Run(types, config, args: args);
#endif

        Summary firstSummary = summaries[0];
        File.WriteAllText("benchmark-results.md", string.Empty);

        using FileStream fileStream = File.OpenWrite("benchmark-results.md");
        fileStream.Write("### Machine Information:\nBenchmarkDotNet v"u8);
        fileStream.Write(Encoding.UTF8.GetBytes(firstSummary.HostEnvironmentInfo.BenchmarkDotNetVersion));
        fileStream.Write(", "u8);
        fileStream.Write(Encoding.UTF8.GetBytes(firstSummary.HostEnvironmentInfo.OsVersion.Value));
        fileStream.Write("\n- "u8);
        fileStream.Write(Encoding.UTF8.GetBytes(CpuInfoFormatter.Format(firstSummary.HostEnvironmentInfo.CpuInfo.Value)));
        fileStream.Write("\n- Hardware Intrinsics: "u8);
        fileStream.Write(Encoding.UTF8.GetBytes(firstSummary.Reports[0].GetHardwareIntrinsicsInfo()?.Replace(",", ", ") ?? "Not supported."));
        fileStream.Write("\n- "u8);
        fileStream.Write(Encoding.UTF8.GetBytes(firstSummary.HostEnvironmentInfo.RuntimeVersion));
        fileStream.Write(", "u8);
        fileStream.Write(Encoding.UTF8.GetBytes(firstSummary.HostEnvironmentInfo.Architecture.ToLowerInvariant()));
        fileStream.Write(", "u8);
        fileStream.Write(Encoding.UTF8.GetBytes(firstSummary.HostEnvironmentInfo.JitInfo));
        fileStream.Write("\n- Total Execution Time: "u8);
        fileStream.Write(Encoding.UTF8.GetBytes(GetHumanizedNanoSeconds(summaries.Sum(summary => summary.TotalTime.TotalNanoseconds))));

        foreach (Summary summary in summaries.OrderBy(summary => summary.BenchmarksCases.Length))
        {
            fileStream.Write("\n\n## "u8);
            fileStream.Write(Encoding.UTF8.GetBytes(summary.BenchmarksCases[0].Descriptor.Type.Name));
            fileStream.Write("\nExecution Time: "u8);
            fileStream.Write(Encoding.UTF8.GetBytes(GetHumanizedNanoSeconds(summary.TotalTime.TotalNanoseconds)));

            // baseline first, then order by success, then by name
            BenchmarkReport[] array = [.. summary.Reports.OrderBy(report => !summary.IsBaseline(report.BenchmarkCase)).ThenBy(report => !report.Success).ThenBy(report => report.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo)];
            for (int i = 0; i < array.Length; i++)
            {
                BenchmarkReport report = array[i];
                fileStream.Write("\n### "u8);
                fileStream.Write(Encoding.UTF8.GetBytes(report.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo));
                if (summary.IsBaseline(report.BenchmarkCase))
                {
                    fileStream.Write(", Baseline"u8);
                }

                if (!report.Success)
                {
                    fileStream.Write(", Failed"u8);
                }

                if (report.ResultStatistics is null)
                {
                    fileStream.Write(":\nNo results."u8);
                    continue;
                }

                fileStream.Write(":"u8);

                string mean = GetHumanizedNanoSeconds(report.ResultStatistics.Mean);
                string standardError = GetHumanizedNanoSeconds(report.ResultStatistics.StandardError);
                string standardDeviation = GetHumanizedNanoSeconds(report.ResultStatistics.StandardDeviation);

                // Calculate the ratio compared to the baseline (if not the baseline itself)
                if (!summary.IsBaseline(report.BenchmarkCase))
                {
                    BenchmarkReport? baselineReport = summary.BenchmarksCases.Select(benchmarkCase => summary.Reports.FirstOrDefault(r => r.BenchmarkCase == benchmarkCase)).FirstOrDefault(r => summary.IsBaseline(r!.BenchmarkCase));
                    if (baselineReport is not null && baselineReport.ResultStatistics is not null)
                    {
                        double ratio = baselineReport.ResultStatistics.Mean / report.ResultStatistics.Mean;
                        double percentage = -(1 - ratio) * 100;
                        fileStream.Write("\nRatio: "u8);
                        fileStream.Write(Encoding.UTF8.GetBytes(percentage.ToString("N2", CultureInfo.InvariantCulture)));
                        fileStream.Write("% "u8);
                        fileStream.Write(double.IsPositive(percentage) ? "faster"u8 : "slower"u8);
                    }
                }

                fileStream.Write("\nMean: "u8);
                fileStream.Write(Encoding.UTF8.GetBytes(mean));
                fileStream.Write("\nError: "u8);
                fileStream.Write(Encoding.UTF8.GetBytes(standardError));
                fileStream.Write("\nStdDev: "u8);
                fileStream.Write(Encoding.UTF8.GetBytes(standardDeviation));
                fileStream.Write("\nMax per second: "u8);
                fileStream.Write(Encoding.UTF8.GetBytes((1_000_000_000 / report.ResultStatistics.Mean).ToString("N2", CultureInfo.InvariantCulture)));
                fileStream.Write(" (1,000,000,000ns / "u8);
                fileStream.Write(Encoding.UTF8.GetBytes(report.ResultStatistics.Mean.ToString("N2", CultureInfo.InvariantCulture)));
                fileStream.Write("ns)"u8);
            }
        }
    }

    private static string GetHumanizedNanoSeconds(double nanoSeconds) => nanoSeconds switch
    {
        < 1_000 => nanoSeconds.ToString("N0", CultureInfo.InvariantCulture) + "ns",
        < 1_000_000 => (nanoSeconds / 1_000).ToString("N2", CultureInfo.InvariantCulture) + "μs",
        < 1_000_000_000 => (nanoSeconds / 1_000_000).ToString("N2", CultureInfo.InvariantCulture) + "ms",
        _ => GetHumanizedExecutionTime(nanoSeconds / 1_000_000_000)
    };

    private static string GetHumanizedExecutionTime(double seconds)
    {
        StringBuilder stringBuilder = new();
        if (seconds >= 60)
        {
            stringBuilder.Append((seconds / 60).ToString("N0", CultureInfo.InvariantCulture));
            stringBuilder.Append("m and ");
            seconds %= 60;
        }

        stringBuilder.Append(seconds.ToString("N3", CultureInfo.InvariantCulture));
        stringBuilder.Append('s');
        return stringBuilder.ToString();
    }
}
