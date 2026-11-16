/*
 * Author: Adomas Vensas
 * Assignment: 3, problem 4 (student id 2525104 -> options 4, 5, 6)
 *
 * DESCRIPTION
 *   Generation of prime numbers by the Sieve of Eratosthenes.
 *
 * PARALLELIZATION
 *   - Base primes up to sqrt(N) are computed sequentially (small part of the work).
 *   - The odd numbers in [1, N] are split into fixed-size segments that fit in the CPU cache.
 *   - Threads take segments dynamically through a shared atomic counter (Interlocked.Increment),
 *     which balances the load: low segments have more work than high ones.
 *   - Each thread keeps its own segment buffer and a local prime counter; the only shared writes
 *     are the segment counter and one Interlocked.Add per thread at the end. Join waits for all threads.
 *
 * HOW TO RUN
 *   dotnet run -c Release -- debug
 *       Slowed down (sleep) run that prints a protocol of what every worker thread does
 *       (N = 200, segment = 16, all cores).
 *   dotnet run -c Release -- fast
 *       No artificial delays, prints the number of primes and the execution time
 *       (N = 1,000,000,000, default segment, all cores).
 *   dotnet run -c Release -- bench
 *       Speedup experiment: time vs threads for each N, and time vs segment (grain) size for the largest N.
 *       Results are printed and written to results.csv (for graphs).
 *
 *   On the MIF cluster reserve one node with enough cores first, e.g.: srun --pty -N1 -c24 $SHELL
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

public static class Program
{
    // Help text printed for an unknown mode.
    private const string Usage = "Usage: dotnet run -c Release -- debug | fast | bench";

    public static void Main(string[] args)
    {
        // First argument selects the mode; with no arguments run the benchmark.
        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "bench";
        switch (mode)
        {
            // Slowed down run with a protocol of every thread's actions.
            case "debug":
                RunOnce(debug: true);
                break;
            // Single run at full speed, prints only the result and time.
            case "fast":
                RunOnce(debug: false);
                break;
            // Full speedup / grain size experiment.
            case "bench":
                Benchmark();
                break;
            // Unknown mode: show how to use the program.
            default:
                Console.WriteLine(Usage);
                break;
        }
    }

    private static void RunOnce(bool debug)
    {
        // Number of worker threads: all available cores.
        var threads = Environment.ProcessorCount;
        // Workload: find primes <= N (small in debug mode so the protocol stays readable).
        long n = debug ? 200 : 1_000_000_000;
        // Grain size: how many odd numbers one segment (unit of work) contains.
        var segmentSize = debug ? 16 : PrimeSieve.DefaultSegmentSize;

        // Print the run parameters.
        Console.WriteLine($"=== Sieve of Eratosthenes, {(debug ? "debug" : "fast")} mode: N = {n:N0}, threads = {threads}, segment = {segmentSize:N0} ===");
        // Start timing.
        var stopwatch = Stopwatch.StartNew();
        // Run the parallel sieve.
        var primes = PrimeSieve.CountPrimes(n, threads, segmentSize, debug);
        // Stop timing.
        stopwatch.Stop();
        // Print the result and how long it took.
        Console.WriteLine($"Primes <= {n:N0}: {primes:N0}");
        Console.WriteLine($"Execution time: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
    }

    private static void Benchmark()
    {
        // Largest thread count to test: all available cores.
        var maxThreads = Environment.ProcessorCount;
        // Each measurement is repeated this many times and the best time is kept (reduces noise).
        const int repeats = 3;
        // Problem sizes N for experiment 1.
        long[] sizes = [10_000_000, 100_000_000, 1_000_000_000];
        // Segment (grain) sizes for experiment 2. Range goes from 4 KB to 16 MB
        int[] segmentSizes = [4_096, 16_384, 65_536, 262_144, 1_048_576, 4_194_304, 16_777_216];

        // Thread counts to test: 1, 2, 4, 8, ... maxThreads.
        var threadCounts = ThreadCounts(maxThreads);

        // Describe the test system (needed for the report).
        Console.WriteLine("=== Parallel Sieve of Eratosthenes: benchmark ===");
        Console.WriteLine($"Logical processors: {Environment.ProcessorCount}, OS: {Environment.OSVersion}, .NET: {Environment.Version}");
        Console.WriteLine($"Threads: {string.Join(", ", threadCounts)}; repeats: {repeats} (best time is used)");

        // Self-check against the classic sieve, also warms up the JIT.
        const int checkN = 1_000_000;
        // Correct answer from the simple sequential sieve.
        var expected = PrimeSieve.SimpleSieve(checkN).Length;
        foreach (var t in threadCounts)
        {
            // Also test an odd segment size (7) to catch boundary errors.
            foreach (var segment in segmentSizes.Append(7))
            {
                // The parallel version must give exactly the same count.
                if (PrimeSieve.CountPrimes(checkN, t, segment) != expected)
                {
                    throw new InvalidOperationException($"Wrong prime count for N={checkN}, {t} threads, segment {segment}");
                }
            }
        }

        // Collected results as CSV lines (header first), used later to draw graphs.
        var csv = new List<string> { "experiment,N,segment,threads,time_ms,speedup,efficiency,primes" };

        // Experiment 1: how speedup depends on problem size N (segment size fixed).
        Console.WriteLine();
        Console.WriteLine($"--- Experiment 1: problem size, segment = {PrimeSieve.DefaultSegmentSize:N0} ---");
        foreach (var n in sizes)
        {
            RunSeries("size", n, PrimeSieve.DefaultSegmentSize, threadCounts, repeats, csv);
        }

        // Experiment 2: how speedup depends on segment (grain) size (largest N fixed).
        Console.WriteLine();
        Console.WriteLine($"--- Experiment 2: grain (segment) size, N = {sizes.Max():N0} ---");
        foreach (var segment in segmentSizes)
        {
            RunSeries("grain", sizes.Max(), segment, threadCounts, repeats, csv);
        }

        // Save all results to a file in the current directory.
        File.WriteAllLines("results.csv", csv);
        Console.WriteLine();
        Console.WriteLine($"Results written to {Path.GetFullPath("results.csv")}");
    }

    private static void RunSeries(string experiment, long n, int segment, int[] threadCounts, int repeats, List<string> csv)
    {
        // Table header for this (N, segment) combination.
        Console.WriteLine();
        Console.WriteLine($"N = {n:N0}, segment = {segment:N0}");
        Console.WriteLine($"{"Threads",7} {"Time, ms",12} {"Speedup",9} {"Efficiency",11} {"Primes",14}");

        // Time with 1 thread; speedup is measured relative to it.
        double baseline = 0;
        // Prime count from the previous thread count, to check all runs agree.
        long? primes = null;
        foreach (var t in threadCounts)
        {
            // Run the sieve and take the best time over the repeats.
            var (time, count) = Measure(n, t, segment, repeats);
            // Different thread counts must give the same answer, otherwise there is a bug (e.g. a data race).
            if (primes != null && primes != count)
            {
                throw new InvalidOperationException($"Inconsistent prime count for N={n} with {t} threads");
            }

            primes = count;
            // threadCounts always starts with 1, so the baseline is set first.
            if (t == 1)
            {
                baseline = time;
            }

            // Speedup S = T(1) / T(t).
            var speedup = baseline / time;
            // Efficiency E = S / t (1.0 = perfect linear speedup).
            var efficiency = speedup / t;
            // Print one table row.
            Console.WriteLine($"{t,7} {time,12:F1} {speedup,9:F2} {efficiency,11:P0} {count,14:N0}");
            // Store the same row in CSV format (invariant culture -> '.' as decimal separator).
            csv.Add(string.Join(",", experiment, n, segment, t,
                time.ToString("F3", CultureInfo.InvariantCulture),
                speedup.ToString("F3", CultureInfo.InvariantCulture),
                efficiency.ToString("F3", CultureInfo.InvariantCulture),
                count));
        }
    }

    private static (double TimeMs, long Primes) Measure(long n, int threads, int segment, int repeats)
    {
        // Best (smallest) time seen so far.
        var best = double.MaxValue;
        // Prime count from the last run.
        long primes = 0;
        for (var r = 0; r < repeats; r++)
        {
            // Time one full run of the parallel sieve.
            var stopwatch = Stopwatch.StartNew();
            primes = PrimeSieve.CountPrimes(n, threads, segment);
            stopwatch.Stop();
            // Keep the fastest run: slower runs are caused by outside noise (OS, other processes).
            best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
        }

        return (best, primes);
    }

    // 1, 2, 4, 8, ... up to maxThreads, always including maxThreads itself.
    private static int[] ThreadCounts(int maxThreads)
    {
        // SortedSet keeps the values ordered and removes duplicates.
        var counts = new SortedSet<int> { maxThreads };
        // Add powers of two below maxThreads.
        for (var t = 1; t < maxThreads; t *= 2)
        {
            counts.Add(t);
        }

        return [.. counts];
    }
}
