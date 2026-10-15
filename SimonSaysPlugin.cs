using System.Globalization;
using OpenKogama.Api;

namespace SimonSays;

public sealed class SimonSaysPlugin : IPlugin
{
    const string TaskColor = "yellow";
    const string EndColor = "red";
    const int Countdown = 10;
    const long Pause = 10_000;

    static readonly (string Task, int Seconds, Gesture? Gesture, AvatarAnimation? Animation)[] Tasks = [("/wave", 30, Gesture.Wave, null), ("jump", 15, null, AvatarAnimation.Jump)];
    static readonly int[] Marks = [.. Enumerable.Range(1, Countdown).Reverse().Select(second => second * 1000), 750, 500, 250];

    readonly object _sync = new();
    readonly HashSet<IPlayer> _done = [];
    IServer _server = null!;
    int _round;
    int _next = -1;
    Gesture? _gesture;
    AvatarAnimation? _animation;
    long _deadline;
    long _pauseUntil;
    int _mark;

    public void Load(IServer server)
    {
        _server = server;
        server.AddCommand("simonsays", Start);
        server.Update += Update;
        server.PlayerGestured += Gestured;
        server.PlayerAnimated += Animated;
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
            _pauseUntil = 0;
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

        (string task, int seconds, _gesture, _animation) = Tasks[_next++];
        _round++;
        _deadline = Environment.TickCount64 + seconds * 1000L;
        _mark = 0;
        _done.Clear();
        string text = $"Round {_round}: Simon says {task}";
        _server.Broadcast(text, TaskColor);
        _server.Notify(text);
        _server.Notify($"{seconds} seconds", brief: true);
    }

    void Gestured(IPlayer player, Gesture gesture)
    {
        lock (_sync)
            if (gesture == _gesture) Done(player);
    }

    void Animated(IPlayer player, AvatarAnimation animation)
    {
        lock (_sync)
            if (animation == _animation) Done(player);
    }

    void Done(IPlayer player)
    {
        if (_next < 0 || _pauseUntil > 0 || !_done.Add(player)) return;
        player.Notify("Done!", brief: true);
    }

    void Summary()
    {
        if (_gesture is null && _animation is null) return;
        IReadOnlyList<IPlayer> everyone = _server.Players;
        string did = string.Join(", ", everyone.Where(_done.Contains).Select(player => player.Name));
        string missed = string.Join(", ", everyone.Where(player => !_done.Contains(player)).Select(player => player.Name));
        _server.Broadcast(did.Length > 0 ? $"Did it: {did}" : "Nobody did it", TaskColor);
        if (missed.Length > 0) _server.Broadcast($"Missed: {missed}", TaskColor);
    }

    void Update(float elapsed)
    {
        lock (_sync)
        {
            if (_next < 0) return;
            long now = Environment.TickCount64;
            if (_pauseUntil > 0)
            {
                if (now < _pauseUntil) return;
                _pauseUntil = 0;
                Give();
                return;
            }

            long left = _deadline - now;
            if (left <= 0)
            {
                _server.Notify($"Round {_round} is over");
                Summary();
                if (_next < Tasks.Length) _pauseUntil = now + Pause;
                else Give();
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
