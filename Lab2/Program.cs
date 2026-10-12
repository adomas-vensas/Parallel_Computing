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
 *   These parts of a White and a Red thread never overlap in the output.
 *
 * HOW TO RUN
 *   dotnet run
 */
using System;
using System.Linq;
using System.Threading;

public static class Program
{
    private const int Iterations = 5;
    private const int ThreadsPerColor = 2;
    private static readonly WhiteRedSynchronizer Synchronizer = new();

    private static void Run(Color color)
    {
        var name = Thread.CurrentThread.Name;
        Console.WriteLine($"Thread {name} started");

        foreach (var _ in Enumerable.Range(0, Iterations))
        {
            Console.WriteLine($"Thread {name}: before calling Enter");
            Synchronizer.Enter(color);
            Console.WriteLine($"Thread {name}: called Enter successfully");

            Thread.Sleep(0);

            Console.WriteLine($"Thread {name}: before calling Leave");
            Synchronizer.Leave();
            Console.WriteLine($"Thread {name}: called Leave successfully");
        }

        Console.WriteLine($"Thread {name} completed");
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

        Console.WriteLine("=== WhiteRed completed ===");
    }
}
