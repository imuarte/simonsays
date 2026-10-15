using System.Globalization;
using System.Numerics;
using OpenKogama.Api;

namespace SimonSays;

public sealed class SimonSaysPlugin : IPlugin
{
    const string TaskColor = "yellow";
    const string DoneColor = "lime";
    const string LostColor = "red";
    const string EndColor = "red";
    const int Countdown = 10;
    const long Pause = 10_000;
    const long Calm = 5_000;
    const long Rise = 7_500;
    const float RiseHeight = 12f;
    const float EbbSeconds = 2f;
    const long ClientDelay = 600;

    enum Goal { Wave, Jump, DropWeapon, PickUp, Water }

    static readonly (string Task, double Seconds, Goal Goal)[] Tasks =
    [
        ("/wave", 30, Goal.Wave),
        ("jump", 15, Goal.Jump),
        ("drop your weapon", 10, Goal.DropWeapon),
        ("pick up a weapon", 10, Goal.PickUp),
        ("don't touch the water", (Calm + Rise) / 1000.0, Goal.Water),
    ];
    static readonly int[] Marks = [.. Enumerable.Range(1, Countdown).Reverse().Select(second => second * 1000), 750, 500, 250];

    readonly object _sync = new();
    readonly HashSet<IPlayer> _done = [];
    readonly HashSet<IPlayer> _wet = [];
    IWorld _world = null!;
    int _round;
    int _next = -1;
    Goal _goal;
    long _deadline;
    long _pauseUntil;
    int _mark;
    int _water = -1;
    bool _addedWater;
    Vector3 _waterHome;
    long _riseAt;
    bool _rising;

    public void Load(IServer server)
    {
        server.AddCommand("simonsays", Start);
        server.Update += Update;
        server.PlayerGestured += (player, gesture) => Check(player, Goal.Wave, gesture == Gesture.Wave);
        server.PlayerAnimated += (player, animation) => Check(player, Goal.Jump, animation == AvatarAnimation.Jump);
        server.PlayerSwitchedItem += (player, item) => Check(player, Goal.DropWeapon, EmptyHanded(item));
        server.PlayerPickedUp += (player, _) => Check(player, Goal.PickUp, true);
    }

    public void Unload()
    {
        lock (_sync)
        {
            Drain(0);
            _next = -1;
        }
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
            _world = player.World;
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
            _world.Broadcast("Simon Says is over", EndColor);
            return;
        }

        (string task, double seconds, _goal) = Tasks[_next++];
        _round++;
        long now = Environment.TickCount64;
        _deadline = now + (long)(seconds * 1000);
        _mark = 0;
        _done.Clear();
        _wet.Clear();
        string text = $"Round {_round}: Simon says {task}";
        _world.Broadcast(text, TaskColor);
        _world.Notify(text);
        _world.Notify($"{seconds.ToString(CultureInfo.InvariantCulture)} seconds", brief: true);

        if (_goal == Goal.DropWeapon)
            foreach (IPlayer player in _world.Players.Where(player => EmptyHanded(player.Holding)))
                Done(player);
        if (_goal == Goal.Water) FindWater(now);
    }

    void FindWater(long now)
    {
        _riseAt = now + Calm;
        _rising = false;
        if (_world.Objects.FirstOrDefault(obj => obj.Type == "WaterPlane") is { } water)
        {
            (_water, _waterHome, _addedWater) = (water.Id, water.Position, false);
            return;
        }

        Vector3 lowest = _world.Players.Select(player => player.Position).OfType<Vector3>().OrderBy(position => position.Y).FirstOrDefault();
        _waterHome = lowest - Vector3.UnitY;
        (_water, _addedWater) = (_world.Add("WaterPlane", _waterHome).Id, true);
    }

    void Flood(long now)
    {
        if (!_rising && now >= _riseAt - ClientDelay)
        {
            _rising = true;
            _world.Move(_water, _waterHome + Vector3.UnitY * RiseHeight, seconds: Rise / 1000f);
            _world.Notify("The water is rising!", brief: true);
        }
        if (now < _riseAt) return;

        float level = _waterHome.Y + RiseHeight * Math.Min(1f, (now - _riseAt) / (float)Rise);
        foreach (IPlayer player in _world.Players)
            if (player.Position is { } position && position.Y < level && _wet.Add(player))
            {
                player.Notify("You touched the water!", brief: true);
                _world.Broadcast($"{player.Name} touched the water", LostColor);
            }
    }

    void Drain(float seconds)
    {
        if (_water < 0) return;
        if (_addedWater) _world.Remove(_water);
        else _world.Move(_water, _waterHome, seconds: seconds);
        _water = -1;
    }

    void Check(IPlayer player, Goal goal, bool met)
    {
        lock (_sync)
            if (met && goal == _goal && player.World == _world) Done(player);
    }

    void Done(IPlayer player)
    {
        if (_next < 0 || _pauseUntil > 0 || !_done.Add(player)) return;
        player.Notify("Done!", brief: true);
        _world.Broadcast($"{player.Name} done", DoneColor);
        if (_world.Players.All(_done.Contains)) EndRound(Environment.TickCount64);
    }

    void EndRound(long now)
    {
        _world.Notify($"Round {_round} is over");
        Summary();
        Drain(EbbSeconds);
        if (_next < Tasks.Length) _pauseUntil = now + Pause;
        else Give();
    }

    void Summary()
    {
        bool water = _goal == Goal.Water;
        (string did, string nobody, string missed) = water
            ? ("Survived", "Nobody survived", "Touched the water")
            : ("Did it", "Nobody did it", "Missed");
        ILookup<bool, string> names = _world.Players.ToLookup(player => water ? !_wet.Contains(player) : _done.Contains(player), player => player.Name);
        _world.Broadcast(names[true].Any() ? $"{did}: {string.Join(", ", names[true])}" : nobody, TaskColor);
        if (names[false].Any()) _world.Broadcast($"{missed}: {string.Join(", ", names[false])}", TaskColor);
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

            if (_goal == Goal.Water) Flood(now);
            long left = _deadline - now;
            if (left <= 0)
            {
                EndRound(now);
                return;
            }
            if (_mark == Marks.Length || left > Marks[_mark]) return;

            while (_mark + 1 < Marks.Length && left <= Marks[_mark + 1]) _mark++;
            int mark = Marks[_mark++];
            _world.Notify(mark switch
            {
                1000 => "1 second left",
                > 1000 => $"{mark / 1000} seconds left",
                _ => $"{(mark / 1000.0).ToString("0.00", CultureInfo.InvariantCulture)} seconds left",
            }, brief: true);
        }
    }
}
