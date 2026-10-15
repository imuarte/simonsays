using System.Globalization;
using OpenKogama.Api;

namespace SimonSays;

public sealed class SimonSaysPlugin : IPlugin
{
    const string TaskColor = "yellow";
    const string EndColor = "red";
    const int Countdown = 10;

    static readonly (string Task, int Seconds)[] Tasks = [("/wave", 30)];
    static readonly int[] Marks = [.. Enumerable.Range(1, Countdown).Reverse().Select(second => second * 1000), 750, 500, 250];

    readonly object _sync = new();
    IServer _server = null!;
    int _round;
    int _next = -1;
    long _deadline;
    int _mark;

    public void Load(IServer server)
    {
        _server = server;
        server.AddCommand("simonsays", Start);
        server.Update += Update;
    }

    void Start(IPlayer player, string[] arguments)
    {
        lock (_sync)
        {
            if (_next >= 0)
            {
                player.Message("Simon Says is already running");
                return;
            }
            _next = 0;
            Give();
        }
    }

    void Give()
    {
        if (_next == Tasks.Length)
        {
            _next = -1;
            _server.Broadcast("Simon Says is over", EndColor);
            return;
        }

        (string task, int seconds) = Tasks[_next++];
        _round++;
        _deadline = Environment.TickCount64 + seconds * 1000L;
        _mark = 0;
        string text = $"Round {_round}: Simon says {task}";
        _server.Broadcast(text, TaskColor);
        _server.Notify($"{text} ({seconds} seconds)");
    }

    void Update(float elapsed)
    {
        lock (_sync)
        {
            if (_next < 0) return;
            long left = _deadline - Environment.TickCount64;
            if (left <= 0)
            {
                _server.Notify($"Round {_round} is over");
                Give();
                return;
            }
            if (_mark == Marks.Length || left > Marks[_mark]) return;

            while (_mark + 1 < Marks.Length && left <= Marks[_mark + 1]) _mark++;
            int mark = Marks[_mark++];
            _server.Notify(mark switch
            {
                1000 => "1 second left",
                > 1000 => $"{mark / 1000} seconds left",
                _ => $"{(mark / 1000.0).ToString("0.00", CultureInfo.InvariantCulture)} seconds left",
            }, brief: true);
        }
    }
}
