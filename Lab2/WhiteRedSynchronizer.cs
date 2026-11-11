using System;
using System.Threading;

namespace Lab2;

/// <summary>
/// Thread color. Only threads of one color can be active at the same time.
/// </summary>
public enum Color
{
    White,
    Red
}

/// <summary>
/// Lets threads of only one color be active at a time. Many threads of the same color can be active together.
/// </summary>
public sealed class WhiteRedSynchronizer
{
    private readonly object _lock = new();
    private int _active;
    private Color _current;

    /// <summary>
    /// Waits while threads of the other color are active, then makes the calling thread active.
    /// </summary>
    /// <param name="color">Color of the calling thread.</param>
    public void Enter(Color color)
    {
        lock (_lock)
        {
            while (_active > 0 && _current != color)
            {
                var prefix = $"[{Thread.CurrentThread.Name}]:";
                Console.WriteLine($"{prefix,-10} suspend");
                Monitor.Wait(_lock);
            }

            _current = color;
            _active++;
        }
    }

    /// <summary>
    /// Ends the activity of the calling thread. The last active thread wakes up all waiting threads.
    /// </summary>
    public void Leave()
    {
        lock (_lock)
        {
            _active--;
            if (_active == 0)
            {
                Monitor.PulseAll(_lock);
            }
        }
    }
}