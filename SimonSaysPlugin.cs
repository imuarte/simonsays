using OpenKogama.Api;

namespace SimonSays;

public sealed class SimonSaysPlugin : IPlugin
{
    const string TaskColor = "yellow";
    const string EndColor = "red";
    const int Warning = 10;

    static readonly (string Task, int Seconds)[] Tasks = [("/wave", 30)];

    readonly object _sync = new();
    IServer _server = null!;
    int _round;
    int _next = -1;
    long _deadline;
    bool _warned;

    public void Load(IServer server)
    {
        _server = server;
        server.AddCommand("simonsays", Start);
        server.Tick += Tick;
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
        _warned = false;
        string text = $"Round {_round}: Simon says {task}";
        _server.Notify(text);
        _server.Broadcast($"{text} ({seconds} seconds)", TaskColor);
    }

    void Tick()
    {
        lock (_sync)
        {
            if (_next < 0) return;
            long left = (_deadline - Environment.TickCount64 + 999) / 1000;
            if (left <= 0)
            {
                _server.Broadcast($"Round {_round} is over", TaskColor);
                Give();
            }
            else if (left <= Warning && !_warned)
            {
                _warned = true;
                _server.Broadcast($"{left} seconds left", TaskColor);
            }
        }
    }
}
