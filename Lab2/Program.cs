/*
 * Author: Adomas Vensas
 * Assignment: 2, option 4
 *
 * DESCRIPTION
 *   "White - Red" synchronizer. At the same time only threads of one color
 *   may be active. Several White and Red threads visit the shared area many times.
 *
 * WHICH OUTPUT IS CORRECT?
 *   A thread is active between "called Enter successfully" and "before calling Leave".
 *   White and Red threads are never active together (Active never shows both > 0, violations == 0),
 *   and several threads of the same color are active together (max active > 1).
 *
 * HOW TO RUN
 *   dotnet run
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public static class Program
{
    private const int Iterations = 5;
    private const int ThreadsPerColor = 2;
    private static readonly WhiteRedSynchronizer Synchronizer = new();
    private static readonly object LogLock = new();
    private static readonly Dictionary<Color, int> Active = Enum.GetValues<Color>().ToDictionary(color => color, _ => 0);
    private static readonly Dictionary<Color, int> MaxActive = Enum.GetValues<Color>().ToDictionary(color => color, _ => 0);
    private static int _violations;

    private static void Log(Color color, string message, int activeChange = 0)
    {
        lock (LogLock)
        {
            Active[color] += activeChange;
            MaxActive[color] = Math.Max(MaxActive[color], Active[color]);
            _violations += Active.Values.All(count => count > 0) ? 1 : 0;
            var prefix = $"[{Thread.CurrentThread.Name}]:";
            Console.WriteLine($"{prefix,-10} {message,-25} | Active: White={Active[Color.White]}, Red={Active[Color.Red]}");
        }
    }

    private static void Run(Color color)
    {
        Log(color, "started");

        foreach (var _ in Enumerable.Range(0, Iterations))
        {
            Log(color, "before calling Enter");
            Synchronizer.Enter(color);
            Log(color, "called Enter successfully", 1);

            Thread.Sleep(Random.Shared.Next(10, 50));

            Log(color, "before calling Leave", -1);
            Synchronizer.Leave();
            Log(color, "called Leave successfully");
        }

        Log(color, "completed");
    }

    public static void Main()
    {
        Console.WriteLine("=== WhiteRed started ===");

        var threads = Enumerable.Range(1, ThreadsPerColor)
            .SelectMany(id => Enum.GetValues<Color>()
                .Select(color =>
                    new Thread(() => Run(color)) { Name = $"{color}-{id}" }))
            .ToArray();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        Console.WriteLine("=== Summary ===");
        Console.WriteLine($"Visits:              {threads.Length * Iterations}");
        Console.WriteLine($"Max active together: White={MaxActive[Color.White]}, Red={MaxActive[Color.Red]}");
        Console.WriteLine($"Violations:          {_violations}");
        Console.WriteLine(_violations == 0
            ? "CORRECT: White and Red threads were never active together"
            : "ERROR: White and Red threads were active together");
        Console.WriteLine("=== WhiteRed completed ===");
    }
}
