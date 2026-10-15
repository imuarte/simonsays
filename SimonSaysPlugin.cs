using System.Globalization;
using OpenKogama.Api;

namespace SimonSays;

public sealed class SimonSaysPlugin : IPlugin
{
    const string TaskColor = "yellow";
    const string DoneColor = "lime";
    const string EndColor = "red";
    const int Countdown = 10;
    const long Pause = 10_000;

    enum Goal { Wave, Jump, DropWeapon, PickUp }

    static readonly (string Task, int Seconds, Goal Goal)[] Tasks =
    [
        ("/wave", 30, Goal.Wave),
        ("jump", 15, Goal.Jump),
        ("drop your weapon", 10, Goal.DropWeapon),
        ("pick up a weapon", 10, Goal.PickUp),
    ];
    static readonly int[] Marks = [.. Enumerable.Range(1, Countdown).Reverse().Select(second => second * 1000), 750, 500, 250];

    readonly object _sync = new();
    readonly HashSet<IPlayer> _done = [];
    IServer _server = null!;
    int _round;
    int _next = -1;
    Goal _goal;
    long _deadline;
    long _pauseUntil;
    int _mark;

    public void Load(IServer server)
    {
        _server = server;
        server.AddCommand("simonsays", Start);
        server.Update += Update;
        server.PlayerGestured += (player, gesture) => Check(player, Goal.Wave, gesture == Gesture.Wave);
        server.PlayerAnimated += (player, animation) => Check(player, Goal.Jump, animation == AvatarAnimation.Jump);
        server.PlayerSwitchedItem += (player, item) => Check(player, Goal.DropWeapon, EmptyHanded(item));
        server.PlayerPickedUp += (player, _) => Check(player, Goal.PickUp, true);
    }

    static bool EmptyHanded(AvatarItem item) => item is AvatarItem.Hand or AvatarItem.LaserPointer;

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

        (string task, int seconds, _goal) = Tasks[_next++];
        _round++;
        _deadline = Environment.TickCount64 + seconds * 1000L;
        _mark = 0;
        _done.Clear();
        string text = $"Round {_round}: Simon says {task}";
        _server.Broadcast(text, TaskColor);
        _server.Notify(text);
        _server.Notify($"{seconds} seconds", brief: true);

        if (_goal == Goal.DropWeapon)
            foreach (IPlayer player in _server.Players.Where(player => EmptyHanded(player.Holding)))
                Done(player);
    }

    void Check(IPlayer player, Goal goal, bool met)
    {
        lock (_sync)
            if (met && goal == _goal) Done(player);
    }

    void Done(IPlayer player)
    {
        if (_next < 0 || _pauseUntil > 0 || !_done.Add(player)) return;
        player.Notify("Done!", brief: true);
        _server.Broadcast($"{player.Name} done", DoneColor);
    }

    void Summary()
    {
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
