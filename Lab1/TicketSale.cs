/*
 * Author: Adomas Vensas
 * Assignment: 1
 *
 * DESCRIPTION
 *   Concert ticket sale. There exists TOTAL_TICKETS seats. A few cashiers
 *   are selling tickets at the same time from the shared counter ticketsLeft.
 *   Their objective is to sell all tickets until none are left.
 *
 * WHICH OUTPUT IS CORRECT?
 *   All cashiers together sell exactly TOTAL_TICKETS tickets and ticketsLeft == 0.
 *
 * WHICH SCENARIO GIVES A WRONG OUTPUT?
 *   The one in unsynchronized mode. For example, 
 *   both cashiers see the last ticket (1), both sell it and the counter becomes -1.
 *
 * WHERE IS THE CRITICAL SECTION?
 *   In Sell() method.
 *
 * CAN THE CRITICAL SECTION BE SMALLER?
 *   No. The check (ticketsLeft <= 0) and the decrement (ticketsLeft--) must happen together
 *   and atomically. If we split them into two separate sections, two cashiers could still
 *   both pass the check with the last ticket.
 *
 * HOW TO RUN
 *   dotnet run            - unsynchronized mode (output is often wrong)
 *   dotnet run -- sync    - synchronized mode (output is always correct)
 */
using System;
using System.Globalization;
using System.Linq;
using System.Threading;

public class TicketSale
{
    private const int TotalTickets = 1_000_000;
    private const int Cashiers = 4;
    private static readonly object Lock = new();
    private static int _ticketsLeft = TotalTickets;
    private static bool _sync;

    private static bool Sell()
    {
        // ===================== CRITICAL SECTION START =====================
        if (_ticketsLeft <= 0)
        {
            return false;
        }

        _ticketsLeft--;
        return true;
        // ===================== CRITICAL SECTION END =======================
    }

    private static bool TrySell()
    {
        if (_sync)
        {
            lock (Lock)
            {
                return Sell();
            } 
        }
        return Sell();
    }

    private static long SellAll()
    {
        return Enumerable.Repeat(0, int.MaxValue)
        .Select(_ => TrySell())
        .TakeWhile(ok => ok)
        .LongCount();
    }

    private static string Format(long number)
    {
        return number.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');
    }

    public static void Main(string[] args)
    {
        _sync = args.Length > 0 && args[0] == "sync";

        var sold = new long[Cashiers];
        var cashiers = Enumerable.Range(0, Cashiers)
                .Select(i => new Thread(() => sold[i] = SellAll()))
                .ToArray();

        foreach (var cashier in cashiers)
        {
            cashier.Start();
        }

        foreach (var cashier in cashiers)
        {
            cashier.Join();
        }

        var totalSold = sold.Sum();

        var mode = _sync ? "synchronized" : "unsynchronized";

        Console.WriteLine($"Mode: {mode}");

        foreach (var i in Enumerable.Range(0, Cashiers))
        {
            Console.WriteLine($"Cashier {i + 1} sold: {Format(sold[i])}");
        }

        Console.WriteLine($"Sold: {Format(totalSold)} of {Format(TotalTickets)}, left: {Format(_ticketsLeft)}");
        Console.WriteLine(totalSold == TotalTickets && _ticketsLeft == 0
                ? "CORRECT: sold exactly as many tickets as there are seats"
                : "ERROR: number of sold tickets does not match the number of seats");
    }
}
