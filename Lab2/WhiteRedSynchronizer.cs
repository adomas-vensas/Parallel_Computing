using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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
/// When the other color is waiting, new threads of the active color wait too, so neither color starves.
/// </summary>
public sealed class WhiteRedSynchronizer
{
    private readonly object _lock = new();
    private readonly Dictionary<Color, int> _waiting = Enum.GetValues<Color>().ToDictionary(color => color, _ => 0);
    private readonly Dictionary<Color, int> _admissions = Enum.GetValues<Color>().ToDictionary(color => color, _ => 0);
    private int _active;
    private Color _current;

    /// <summary>
    /// Waits until threads of this color are allowed in, then makes the calling thread active.
    /// </summary>
    /// <param name="color">Color of the calling thread.</param>
    public void Enter(Color color)
    {
        lock (_lock)
        {
            if ((_active == 0 || _current == color) && _waiting[Other(color)] == 0)
            {
                _current = color;
                _active++;
                return;
            }

            // Wait until Leave() lets this color in (it increases _admissions).
            var admission = _admissions[color];
            _waiting[color]++;
            _ = Enumerable.Repeat(_lock, int.MaxValue)
                .TakeWhile(_ => _admissions[color] == admission)
                .All(Suspend);
        }
    }

    /// <summary>
    /// Ends the activity of the calling thread. The last active thread lets all waiting threads of the other color in.
    /// </summary>
    public void Leave()
    {
        lock (_lock)
        {
            _active--;
            var next = Other(_current);
            if (_active > 0 || _waiting[next] == 0)
            {
                return;
            }

            // Last thread out: let all waiting threads of the other color in and wake them up.
            _current = next;
            _active += _waiting[next];
            _waiting[next] = 0;
            _admissions[next]++;
            Monitor.PulseAll(_lock);
        }
    }

    private static bool Suspend(object monitor)
    {
        Console.WriteLine($"Suspend: {Thread.CurrentThread.Name}");
        return Monitor.Wait(monitor);
    }

    private static Color Other(Color color)
    {
        return color == Color.White ? Color.Red : Color.White;
    }
}
