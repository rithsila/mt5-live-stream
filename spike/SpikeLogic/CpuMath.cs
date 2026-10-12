namespace SpikeLogic;

public static class CpuMath
{
    /// <summary>
    /// Process CPU time as a percentage of the whole machine:
    /// process CPU seconds delta / wall seconds delta / logical cores * 100.
    /// </summary>
    public static double ProcessCpuPercent(TimeSpan cpuDelta, TimeSpan wallDelta, int logicalCores)
    {
        if (wallDelta.TotalSeconds <= 0 || logicalCores <= 0)
            return 0;
        return cpuDelta.TotalSeconds / wallDelta.TotalSeconds / logicalCores * 100.0;
    }

    /// <summary>
    /// Machine busy % from GetSystemTimes deltas. Kernel time already includes
    /// idle time, so busy = (kernel - idle) + user over (kernel + user).
    /// All inputs are tick deltas (any unit, as long as they match).
    /// </summary>
    public static double MachineBusyPercent(ulong idleDelta, ulong kernelDelta, ulong userDelta)
    {
        ulong total = kernelDelta + userDelta;
        if (total == 0)
            return 0;
        ulong busyKernel = kernelDelta > idleDelta ? kernelDelta - idleDelta : 0;
        return (busyKernel + userDelta) * 100.0 / total;
    }

    /// <summary>Percentile of a sample set (linear interpolation, p in [0,100]).</summary>
    public static double Percentile(IReadOnlyList<double> samples, double p)
    {
        if (samples.Count == 0)
            return 0;
        var sorted = samples.OrderBy(x => x).ToArray();
        double rank = p / 100.0 * (sorted.Length - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        if (lo == hi)
            return sorted[lo];
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }
}
