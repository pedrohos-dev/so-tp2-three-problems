using System.Globalization;

namespace MontanhaRussa;

/// <summary>Uma linha do arquivo de medidas.</summary>
public sealed record ExperimentRow(
    string Scenario,
    int M,
    int C,
    int N,
    int Rep,
    double TS,
    double Throughput,
    double LatencyMs,
    double CpuS,
    double? Speedup = null,
    double? Efficiency = null,
    double OverheadMs = 0)
{
    public const string Header =
        "scenario,m,C,n,rep,T_s,throughput,latency_ms,cpu_s,speedup,efficiency,overhead_ms";

    public string ToCsv() => string.Join(',',
        Scenario,
        M,
        C,
        N,
        Rep,
        TS.ToString("G17", CultureInfo.InvariantCulture),
        Throughput.ToString("G17", CultureInfo.InvariantCulture),
        LatencyMs.ToString("G17", CultureInfo.InvariantCulture),
        CpuS.ToString("G17", CultureInfo.InvariantCulture),
        Speedup?.ToString("G17", CultureInfo.InvariantCulture) ?? "",
        Efficiency?.ToString("G17", CultureInfo.InvariantCulture) ?? "",
        OverheadMs.ToString("G17", CultureInfo.InvariantCulture));
}
