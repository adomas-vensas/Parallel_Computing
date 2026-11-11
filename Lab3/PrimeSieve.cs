using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

/// <summary>
/// Parallel segmented Sieve of Eratosthenes. Only odd numbers are stored: index i stands for number 2i+1.
/// </summary>
public static class PrimeSieve
{
    // Default number of odd numbers per segment (262,144 bytes as bool[] - fits in the CPU L2 cache).
    public const int DefaultSegmentSize = 1 << 18;
    // Artificial pause after each segment in debug mode, so the thread interleaving is visible.
    private const int DebugDelayMs = 300;
    // In debug mode print at most this many primes per segment.
    private const int DebugMaxPrimesShown = 15;

    /// <summary>
    /// Classic sequential sieve. Returns all primes &lt;= limit.
    /// </summary>
    public static int[] SimpleSieve(int limit)
    {
        // composite[x] == true means x has been crossed out (it is not prime).
        var composite = new bool[limit + 1];
        // Primes found so far, in increasing order.
        var primes = new List<int>();
        // Walk over every candidate starting from the first prime, 2.
        for (var i = 2; i <= limit; i++)
        {
            // Already crossed out by a smaller prime -> not prime, skip it.
            if (composite[i])
            {
                continue;
            }

            // Not crossed out -> i is prime.
            primes.Add(i);
            // Cross out multiples of i; smaller multiples were already crossed out by smaller primes, so start at i*i.
            for (var j = (long)i * i; j <= limit; j += i)
            {
                composite[j] = true;
            }
        }

        // Return the primes as an array (faster to iterate in the hot loop).
        return primes.ToArray();
    }

    /// <summary>
    /// Counts primes &lt;= n using <paramref name="threadCount"/> threads.
    /// Base primes up to sqrt(n) are found sequentially, then segments of the odd numbers are
    /// handed out to threads dynamically through a shared atomic counter.
    /// In debug mode every step is logged and workers sleep after each segment.
    /// </summary>
    public static long CountPrimes(long n, int threadCount, int segmentSize = DefaultSegmentSize, bool debug = false)
    {
        // There are no primes below 2.
        if (n < 2)
        {
            return 0;
        }

        // Every composite number <= n has a prime factor <= sqrt(n), so these primes are enough to sieve everything.
        var basePrimes = SimpleSieve(IntSqrt(n));
        // Number of odd numbers in [1, n]; only those are stored (index i = number 2i+1).
        var oddCount = (n + 1) / 2;
        // Number of segments, rounded up so the last, possibly shorter, segment is included.
        var segmentCount = (oddCount + segmentSize - 1) / segmentSize;
        // Shared "next segment to take" counter; starts at -1 because Interlocked.Increment returns the new value (first is 0).
        long nextSegment = -1;
        // Shared prime counter; starts at 1 because prime 2 is not stored in the odd-only array.
        long total = 1;

        // Debug protocol: show the sequential preparation step.
        if (debug)
        {
            Console.WriteLine($"[Main]     base primes <= {IntSqrt(n)}: {string.Join(", ", basePrimes)}");
            Console.WriteLine($"[Main]     {oddCount} odd numbers split into {segmentCount} segments of {segmentSize}; prime 2 counted directly");
        }

        // Worker threads; their number is a program parameter.
        var threads = new Thread[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            // Each worker runs the same code: take a segment, sieve it, repeat until no segments are left.
            threads[t] = new Thread(() =>
            {
                // Thread name for the debug protocol (e.g. "Worker-2").
                var name = Thread.CurrentThread.Name;
                // Private buffer for one segment, allocated once per thread and reused (no sharing, no GC churn).
                var composite = new bool[segmentSize];
                // Private prime counter, so threads do not fight over the shared total for every segment.
                long local = 0;
                // Index of the segment this thread is currently working on.
                long segment;
                if (debug)
                {
                    Console.WriteLine($"[{name}] started");
                }

                // Dynamic work distribution: atomically grab the next free segment until all are taken.
                while ((segment = Interlocked.Increment(ref nextSegment)) < segmentCount)
                {
                    // First odd-number index in this segment.
                    var low = segment * segmentSize;
                    // Segment length; the last segment may be shorter than segmentSize.
                    var length = (int)Math.Min(segmentSize, oddCount - low);
                    if (debug)
                    {
                        Console.WriteLine($"[{name}] took segment {segment}: odd numbers {2 * low + 1}..{2 * (low + length - 1) + 1}");
                    }

                    // Cross out composites in this segment and count what is left (the primes).
                    var found = SieveSegment(low, length, basePrimes, composite);
                    // Add to this thread's private count.
                    local += found;
                    if (debug)
                    {
                        Console.WriteLine($"[{name}] segment {segment} done: {found} primes [{SegmentPrimes(low, length, composite)}], local total {local}");
                        // Slow down so the protocol shows how threads alternate.
                        Thread.Sleep(DebugDelayMs);
                    }
                }

                // No segments left: add the private count to the shared total atomically (once per thread).
                Interlocked.Add(ref total, local);
                if (debug)
                {
                    Console.WriteLine($"[{name}] no segments left, added {local} to shared total");
                }
            }) { Name = $"Worker-{t + 1}" };
            // Start the worker; it runs in parallel with the others.
            threads[t].Start();
        }

        // Wait until every worker has finished (Join is the final synchronization point).
        foreach (var thread in threads)
        {
            thread.Join();
        }

        // All workers have added their counts, so total is now complete.
        return total;
    }

    /// <summary>
    /// Sieves odd-number indices [low, low + length) and returns the number of primes among them.
    /// </summary>
    private static int SieveSegment(long low, int length, int[] basePrimes, bool[] composite)
    {
        // Reset the reused buffer: nothing is crossed out yet in this segment.
        Array.Clear(composite, 0, length);
        // Smallest odd number in this segment.
        var lowNumber = 2 * low + 1;
        // Largest odd number in this segment.
        var highNumber = 2 * (low + length - 1) + 1;

        // Cross out multiples of every base prime.
        foreach (var p in basePrimes)
        {
            // Even numbers are not stored, so prime 2 has nothing to cross out.
            if (p == 2)
            {
                continue;
            }

            // Smaller multiples of p have a smaller prime factor and are crossed out by that prime.
            var start = (long)p * p;
            // p*p is already past this segment, and so are all larger primes - stop.
            if (start > highNumber)
            {
                break;
            }

            // p*p is before this segment: find the first multiple of p inside it instead.
            if (start < lowNumber)
            {
                // Round lowNumber up to the nearest multiple of p.
                start = (lowNumber + p - 1) / p * p;
                // Even multiples are not stored; the next multiple (start + p) is odd.
                if ((start & 1) == 0)
                {
                    start += p;
                }
            }

            // Convert the number to an index inside the segment, then step over odd multiples:
            // consecutive odd multiples differ by 2p, i.e. by p in index space.
            for (var i = (start - 1) / 2 - low; i < length; i += p)
            {
                composite[i] = true;
            }
        }

        // Count the numbers that were never crossed out - those are primes.
        var count = 0;
        for (var i = 0; i < length; i++)
        {
            if (!composite[i])
            {
                count++;
            }
        }

        // Index 0 is number 1, which is not prime.
        return low == 0 ? count - 1 : count;
    }

    /// <summary>
    /// Debug helper: lists the primes found in a sieved segment (at most DebugMaxPrimesShown).
    /// </summary>
    private static string SegmentPrimes(long low, int length, bool[] composite)
    {
        // Skip index 0 in the first segment, because it stands for number 1.
        var primes = Enumerable.Range(low == 0 ? 1 : 0, Math.Max(0, length - (low == 0 ? 1 : 0)))
            // Keep indices that were not crossed out.
            .Where(i => !composite[i])
            // Convert segment index back to the actual odd number.
            .Select(i => 2 * (low + i) + 1)
            .ToList();
        // Join the first few primes into one string.
        var shown = string.Join(", ", primes.Take(DebugMaxPrimesShown));
        // Add "..." if some primes were left out.
        return primes.Count > DebugMaxPrimesShown ? shown + ", ..." : shown;
    }

    /// <summary>
    /// Exact integer square root: largest r with r*r &lt;= n.
    /// </summary>
    private static int IntSqrt(long n)
    {
        // Floating-point estimate; may be off by one for large n because of rounding.
        var r = (long)Math.Sqrt(n);
        // Estimate too big -> decrease.
        while (r * r > n)
        {
            r--;
        }

        // Estimate too small -> increase.
        while ((r + 1) * (r + 1) <= n)
        {
            r++;
        }

        return (int)r;
    }
}
