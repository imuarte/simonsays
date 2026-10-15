using System.Globalization;
using System.Numerics;
using OpenKogama.Api;

namespace SimonSays;

public sealed class SimonSaysPlugin : IPlugin
{
    const int LobbyCountdown = 10;
    const int FinalCount = 3;
    const long Pause = 10_000;
    const long Rest = 5_000;
    const long Calm = 5_000;
    const long Rise = 7_500;
    const float RiseHeight = 12f;
    const float EbbSeconds = 2f;
    const long ClientDelay = 600;
    const long StartDelay = 3_000;
    const long ColossusWarmup = 2_000;
    const float ColossusCharge = 1.3f;
    const long ColossusAimEvery = 500;
    const float ColossusTrackSpeed = 150f;
    const float ColossusSweepSpeed = 20f;
    const float ColossusOnTarget = 8f;
    const float SkyPitchMin = 15f;
    const float SkyPitchMax = 55f;
    const int SkyMoveMin = 3_000;
    const int SkyMoveMax = 6_000;
    const int SkyBlastMin = 8_000;
    const int SkyBlastMax = 15_000;
    const long ColossusRest = 1_500;
    const long ColossusLookAgain = 500;
    const float ColossusHealthPerPlayer = 250f;
    const float ColossusDamage = 40f;
    const float ColossusPush = 12f;
    const long ColossusFall = 3_000;
    const ColossusSize LairSize = ColossusSize.Medium;
    const string LairMarker = "colossus";
    const string WorldKey = "world";
    const string MinKey = "min";
    const string StateKey = "state";
    const string PlateKey = "plate";
    const long StateSettle = 3_000;
    const string Usage = "Use !simonsays to join, or !simonsays start, stop or config";
    const int Light = 0xFFF1D6;
    const int Sand = 0xD9B98C;
    const int Gold = 0xFFD166;
    const int Bright = 0xFFE680;
    const int Ember = 0xFF8C5A;
    static readonly Vector3 Chest = Vector3.UnitY;
    static readonly Version FirstColossusClient = new(1, 39);
    static readonly Version LastColossusClient = new(2, 4);
    static readonly int[] Heat = [0xFF7A45, 0xFFA04D, 0xFFC15E, 0xFFE08A];
    static readonly string Title = Bold(Tint("Simon Says", Gold));

    enum Goal { Wave, Jump, DropWeapon, PickUp, Water, Colossus }

    static readonly (string Task, double Seconds, Goal Goal)[] Tasks =
    [
        ("/wave", 30, Goal.Wave),
        ("jump", 15, Goal.Jump),
        ("drop your weapon", 10, Goal.DropWeapon),
        ("pick up a weapon", 10, Goal.PickUp),
        ("don't touch the water", (Calm + Rise) / 1000.0, Goal.Water),
        ("kill the Colossus", 0, Goal.Colossus),
    ];
    static readonly int[] Marks = [5000, 3000, 2000, 1000];

    sealed class Giant(INpc npc, IWorldObject? trigger, long readyAt)
    {
        public INpc Npc => npc;
        public IWorldObject? Trigger => trigger;
        public long ReadyAt => readyAt;
        public bool Awake { get; set; }
        public bool Down { get; set; }
        public long DownAt { get; set; }
        public IPlayer? Target { get; set; }
        public int LastTarget { get; set; } = -1;
        public bool Charging { get; set; }
        public long ChargeEnd { get; set; }
        public long NextAim { get; set; }
        public long NextShot { get; set; }
        public Vector3 Aim { get; set; } = Vector3.UnitZ;
        public Vector3 Sky { get; set; } = Vector3.UnitY;
        public long NextSky { get; set; }
        public long NextSkyBlast { get; set; }
        public long LastTick { get; set; }
    }

    readonly object _sync = new();
    readonly HashSet<IPlayer> _done = [];
    readonly HashSet<IPlayer> _lost = [];
    readonly HashSet<IPlayer> _out = [];
    readonly HashSet<IPlayer> _playing = [];
    readonly HashSet<IPlayer> _respawning = [];
    readonly HashSet<IPlayer> _contestants = [];
    readonly List<Giant> _giants = [];
    readonly List<(INpc Npc, long RemoveAt)> _fallen = [];
    IServer _server = null!;
    IPluginStorage _store = null!;
    IWorld _world = null!;
    IWorld? _lobby;
    int? _autoWorld;
    int? _min;
    int? _stateBox;
    int? _statePlate;
    long _settleUntil;
    bool _seen;
    int _announced = -1;
    long _startAt;
    int _shown;
    long _restUntil;
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
        _server = server;
        _store = server.Storage("simonsays");
        _autoWorld = Stored(WorldKey);
        _min = Stored(MinKey);
        _stateBox = Stored(StateKey);
        _statePlate = Stored(PlateKey);
        server.AddCommand("simonsays", Command);
        server.Update += Update;
        server.PlayerJoined += Joined;
        server.PlayerGestured += (player, gesture) => Check(player, Goal.Wave, gesture == Gesture.Wave);
        server.PlayerAnimated += (player, animation) => Check(player, Goal.Jump, animation == AvatarAnimation.Jump);
        server.PlayerSwitchedItem += (player, item) => Check(player, Goal.DropWeapon, EmptyHanded(item));
        server.PlayerPickedUp += (player, _) => Check(player, Goal.PickUp, true);
        server.PlayerKilledByNpc += KilledByNpc;
    }

    public void Unload()
    {
        lock (_sync)
        {
            if (_next >= 0)
            {
                foreach (IPlayer player in Remaining().ToList()) ToLobby(player);
                SetState(_world, false);
            }
            Drain(0);
            foreach (Giant giant in _giants) giant.Npc.Remove();
            foreach ((INpc npc, _) in _fallen) npc.Remove();
            _giants.Clear();
            _fallen.Clear();
            _next = -1;
        }
    }

    static bool EmptyHanded(AvatarItem item) => item is AvatarItem.Hand or AvatarItem.LaserPointer;

    void Command(IPlayer player, string[] arguments)
    {
        lock (_sync)
        {
            string action = arguments.Length > 0 ? arguments[0].ToLowerInvariant() : "";
            if (action == "") Join(player);
            else if (action == "start") Start(player, arguments[1..]);
            else if (action == "stop") Halt(player);
            else if (action == "config") Configure(player, arguments[1..]);
            else player.Message(Usage);
        }
    }

    void Join(IPlayer player)
    {
        if (_next >= 0 && _contestants.Contains(player) && !_out.Contains(player))
        {
            player.Message(Tint("You're already playing", Light));
            return;
        }
        if (!player.SetTeam(Team.Red))
        {
            player.Message(Tint("This world has no lobby (red team)", Ember));
            return;
        }
        player.Message(Tint(player.World.Id == _autoWorld && _min is int min
            ? $"You're in the lobby, the game starts with {PlayerCount(min)}"
            : "You're in the lobby", Light));
    }

    void Joined(IPlayer player)
    {
        lock (_sync)
        {
            if (player.World.Id != _autoWorld) return;
            ToLobby(player);
            if (_next >= 0 && player.World == _world) player.Message(Tint("A game is on, you'll play in the next one", Light));
        }
    }

    void Start(IPlayer player, string[] arguments)
    {
        if (_next >= 0)
        {
            player.Message("A game is already on");
            return;
        }
        int first = 0;
        if (arguments.Length > 0)
        {
            if (TaskIndex(string.Join(' ', arguments)) is not int chosen)
            {
                player.Message($"Pick a round from 1 to {Tasks.Length}, or a name like jump or water");
                return;
            }
            first = chosen;
        }
        if (!Begin(player.World, first)) player.Message("This world has no game area (blue team)");
    }

    void Halt(IPlayer player)
    {
        if (_next < 0) player.Message("No game is on");
        else Stop(Environment.TickCount64);
    }

    void Configure(IPlayer player, string[] arguments)
    {
        IWorld world = player.World;
        string setting = arguments.Length > 0 ? arguments[0].ToLowerInvariant() : "";
        string value = arguments.Length > 1 ? arguments[1].ToLowerInvariant() : "";
        switch (setting)
        {
            case "":
                player.Message(Describe());
                break;
            case "off":
                _min = null;
                _startAt = 0;
                Save(MinKey, null);
                player.Message("Auto start off");
                break;
            case "min" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int min) && min >= 1:
                Claim(world);
                _min = min;
                _announced = -1;
                Save(MinKey, min);
                player.Message($"Auto start on, with {PlayerCount(min)}");
                break;
            case "state" when value == "off":
                _stateBox = null;
                Save(StateKey, null);
                player.Message("Round state off");
                break;
            case "state" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int box):
                if (world.Toggled(box) is null)
                {
                    player.Message($"{box} isn't a Toggle Box in this world");
                    break;
                }
                Claim(world);
                _stateBox = box;
                Save(StateKey, box);
                if (_next >= 0 && _world == world) SetState(world, true);
                else _lobby = null;
                player.Message($"Round state: Toggle Box {box}");
                break;
            case "plate" when value == "off":
                _statePlate = null;
                Save(PlateKey, null);
                player.Message("Plate off");
                break;
            case "plate" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int plate):
                if (world.Find(plate)?.Type is not ("PressurePlate" or "TriggerCube" or "TriggerBox"))
                {
                    player.Message($"{plate} isn't a Pressure Plate in this world");
                    break;
                }
                Claim(world);
                _statePlate = plate;
                Save(PlateKey, plate);
                player.Message($"Plate {plate} switches the round state");
                break;
            default:
                player.Message("Use config, config min <players>, config state <id>, config plate <id>, or off after any of them");
                break;
        }
    }

    void Claim(IWorld world)
    {
        if (_autoWorld == world.Id) return;
        _autoWorld = world.Id;
        _min = null;
        _stateBox = null;
        _statePlate = null;
        _lobby = null;
        Save(WorldKey, world.Id);
        Save(MinKey, null);
        Save(StateKey, null);
        Save(PlateKey, null);
    }

    string Describe()
    {
        if (_autoWorld is not int id) return "Not set up yet, try !simonsays config min 4";
        string start = _min is int min ? $"on, with {PlayerCount(min)}" : "off";
        string state = _stateBox is int box ? $"Toggle Box {box}" : "off";
        string plate = _statePlate is int pressed ? $"plate {pressed}" : "plate off";
        return $"World {id}: auto start {start}, round state {state}, {plate}";
    }

    int? Stored(string key) => int.TryParse(_store.Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    void Save(string key, int? value) => _store.Set(key, value?.ToString(CultureInfo.InvariantCulture));

    static int? TaskIndex(string text)
    {
        if (int.TryParse(text, out int number)) return number >= 1 && number <= Tasks.Length ? number - 1 : null;
        int index = Array.FindIndex(Tasks, task => task.Goal.ToString().Equals(text, StringComparison.OrdinalIgnoreCase)
            || task.Task.Contains(text, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : null;
    }

    bool Begin(IWorld world, int first)
    {
        _world = world;
        _startAt = 0;
        _out.Clear();
        _respawning.Clear();
        _contestants.Clear();
        foreach (IPlayer contestant in world.Players)
            if (contestant.SetTeam(Team.Blue)) _contestants.Add(contestant);
        if (_contestants.Count == 0)
        {
            SetState(world, false);
            return false;
        }
        _next = first;
        _round = first;
        _pauseUntil = Environment.TickCount64 + StartDelay;
        SetState(world, true);
        world.Broadcast($"{Title} {Bold(Tint("go!", Bright))}");
        return true;
    }

    void Lobby(long now)
    {
        if (now < _restUntil || _autoWorld is not int id) return;
        IWorld? world = _server.Worlds.Where(open => open.Id == id).MaxBy(open => open.Players.Count);
        if (world is null)
        {
            _lobby = null;
            return;
        }
        if (world != _lobby) Open(world);

        bool on = StateBox(world) is int box && world.Toggled(box) == true;
        if (now >= _settleUntil)
        {
            if (on && !_seen)
            {
                Launch(world, now);
                return;
            }
            _seen = on;
        }
        if (_min is not int min) return;

        int count = world.Players.Count;
        if (count != _announced) Announce(world, count, min, now);
        if (_startAt == 0) return;

        long left = _startAt - now;
        if (left <= 0)
        {
            Launch(world, now);
            return;
        }
        int seconds = (int)((left + 999) / 1000);
        if (seconds >= _shown) return;
        _shown = seconds;
        if (seconds <= FinalCount) world.Broadcast(Count(seconds));
    }

    void Open(IWorld world)
    {
        _lobby = world;
        _announced = -1;
        _startAt = 0;
        SetState(world, false);
        foreach (IPlayer player in world.Players) ToLobby(player);
    }

    void Announce(IWorld world, int count, int min, long now)
    {
        _announced = count;
        if (count < min)
        {
            _startAt = 0;
            world.Broadcast(Waiting(count, min));
            return;
        }
        if (_startAt > 0) return;
        _startAt = now + LobbyCountdown * 1000L;
        _shown = LobbyCountdown;
        world.Broadcast(Starting());
    }

    void Launch(IWorld world, long now)
    {
        if (Begin(world, 0)) return;
        world.Broadcast(Tint("This world has no game area (blue team)", Ember));
        _restUntil = now + Rest;
        _announced = -1;
    }

    void SetState(IWorld world, bool on)
    {
        if (world == _lobby) _seen = on;
        if (StateBox(world) is not int box || world.Toggled(box) == on) return;
        _settleUntil = Environment.TickCount64 + StateSettle;
        if (StatePlate(world) is not int plate || !world.Press(plate)) world.SetToggled(box, on);
    }

    int? StateBox(IWorld world) => world.Id == _autoWorld ? _stateBox : null;

    int? StatePlate(IWorld world) => world.Id == _autoWorld ? _statePlate : null;

    void Give()
    {
        _playing.Clear();
        _playing.UnionWith(Remaining());
        while (_next < Tasks.Length && Tasks[_next].Goal == Goal.Colossus && !ColossusFits())
        {
            _world.Broadcast(Tint("Skipping the Colossus round", Sand));
            _next++;
        }
        if (_next == Tasks.Length || _playing.Count == 0)
        {
            _world.Broadcast(_playing.Count == 0
                ? $"{Tint("Nobody won", Ember)} {Title}"
                : $"{Bold(Tint(Names(_playing), Bright))} {Tint(_playing.Count == 1 ? "wins" : "win", Light)} {Title}{Tint("!", Light)}");
            foreach (IPlayer winner in _playing) ToLobby(winner);
            Finish(Environment.TickCount64);
            return;
        }

        (string task, double seconds, _goal) = Tasks[_next++];
        _round++;
        long now = Environment.TickCount64;
        _deadline = _goal == Goal.Colossus ? long.MaxValue : now + (long)(seconds * 1000);
        _mark = 0;
        _done.Clear();
        _lost.Clear();
        _world.Broadcast(Tint($"{Bold($"Round {_round}")}: Simon says {Bold(task)}!", Gold));
        _world.Notify($"Round {_round}: Simon says {task}!");

        if (_goal == Goal.DropWeapon)
            foreach (IPlayer player in _playing.Where(player => EmptyHanded(player.Holding)).ToList())
                Done(player);
        if (_goal == Goal.Water) FindWater(now);
        if (_goal == Goal.Colossus) Summon(now);
    }

    bool ColossusFits() => _playing.All(player => Version.TryParse(player.ClientVersion, out Version? version)
        && version >= FirstColossusClient && version < LastColossusClient);

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
        foreach (IPlayer player in Playing())
            if (player.Position is { } position && position.Y < level && _lost.Add(player))
                Lose(player, "You touched the water!");
    }

    void Drain(float seconds)
    {
        if (_water < 0) return;
        if (_addedWater) _world.Remove(_water);
        else _world.Move(_water, _waterHome, seconds: seconds);
        _water = -1;
    }

    void Summon(long now)
    {
        float health = ColossusHealthPerPlayer * Math.Max(1, _playing.Count);
        foreach ((Vector3 spot, IWorldObject? trigger) in Lairs())
        {
            INpc npc = _world.SpawnNpc("Colossus", spot, 0f);
            npc.Speed = 0f;
            npc.MaxHealth = health;
            _giants.Add(new Giant(npc, trigger, now + ColossusWarmup));
        }
    }

    IEnumerable<(Vector3 Spot, IWorldObject? Trigger)> Lairs()
    {
        IReadOnlyList<IWorldObject> markers = _world.Markers(LairMarker);
        if (markers.Count > 0) return markers.Select(marker => (marker.Position, (IWorldObject?)null));
        List<IWorldObject> triggers = [.. _world.Objects.Where(obj => obj.Type == "GodzillaTrigger")];
        if (triggers.Count > 0) return triggers.Select(trigger => (trigger.Position, (IWorldObject?)trigger));

        Vector3[] spots = [.. Playing().Select(player => player.Position).OfType<Vector3>()];
        if (spots.Length == 0) return [];
        Vector3 middle = spots.Aggregate(Vector3.Zero, (sum, spot) => sum + spot) / spots.Length;
        return [(spots.MinBy(spot => Vector3.DistanceSquared(spot, middle)), null)];
    }

    void Hunt(long now)
    {
        foreach (Giant giant in _giants.Where(giant => !giant.Down))
        {
            if (!giant.Npc.Exists || !giant.Npc.Alive)
            {
                giant.Down = true;
                giant.DownAt = now;
                _world.Broadcast(Bold(Tint("The Colossus is down!", Bright)));
                continue;
            }
            if (!giant.Awake)
            {
                if (now < giant.ReadyAt) continue;
                bool risen = giant.Trigger is { } trigger ? giant.Npc.BecomeColossus(trigger) : giant.Npc.BecomeColossus(LairSize);
                if (!risen)
                {
                    giant.Npc.Remove();
                    giant.Down = true;
                    continue;
                }
                giant.Awake = true;
                giant.NextShot = now + ColossusWarmup;
                giant.NextSkyBlast = now + Random.Shared.Next(SkyBlastMin, SkyBlastMax);
                giant.LastTick = now;
                continue;
            }
            Act(giant, now);
        }

        if (_giants.All(giant => giant.Down) || !Playing().Any(player => !_out.Contains(player))) EndRound(now);
    }

    void Act(Giant giant, long now)
    {
        float seconds = (now - giant.LastTick) / 1000f;
        giant.LastTick = now;
        Vector3 eye = giant.Npc.EyePosition;

        if (giant.Target is { } lost && !giant.Charging && !Targetable(lost)) giant.Target = null;
        if (giant.Target is null && !giant.Charging && now >= giant.NextShot)
        {
            if (NextTarget(giant) is { } next)
            {
                giant.Target = next;
                giant.LastTarget = next.Actor;
            }
            else giant.NextShot = now + ColossusLookAgain;
        }

        Vector3 desired;
        float speed;
        if (giant.Target?.Position is { } spot)
        {
            desired = Vector3.Normalize(spot + Chest - eye);
            speed = ColossusTrackSpeed;
        }
        else
        {
            if (now >= giant.NextSky || Angle(giant.Aim, giant.Sky) < 2f)
            {
                giant.Sky = SkyDirection();
                giant.NextSky = now + Random.Shared.Next(SkyMoveMin, SkyMoveMax);
            }
            desired = giant.Sky;
            speed = ColossusSweepSpeed;
        }
        giant.Aim = Turn(giant.Aim, desired, speed * seconds);
        if (giant.Target is not null) giant.Npc.Face(MathF.Atan2(giant.Aim.X, giant.Aim.Z) * 180f / MathF.PI);
        if (now >= giant.NextAim)
        {
            giant.Npc.Aim(giant.Aim);
            giant.NextAim = Math.Max(giant.NextAim + ColossusAimEvery, now + ColossusAimEvery / 2);
        }

        if (!giant.Charging && (giant.Target is not null ? Angle(giant.Aim, desired) <= ColossusOnTarget : now >= giant.NextSkyBlast))
        {
            giant.Charging = true;
            giant.ChargeEnd = now + (long)(ColossusCharge * 1000);
            if (giant.Target is null) giant.NextSkyBlast = now + Random.Shared.Next(SkyBlastMin, SkyBlastMax);
            giant.Npc.Shoot(giant.Aim, ColossusCharge);
        }
        if (!giant.Charging || now < giant.ChargeEnd) return;

        giant.Charging = false;
        giant.NextShot = now + ColossusRest;
        if (giant.Target is { } target) Fire(giant, target);
        giant.Target = null;
    }

    void Fire(Giant giant, IPlayer target)
    {
        Vector3 eye = giant.Npc.EyePosition;
        if (!Targetable(target) || target.Position is not { } spot || !_world.LineOfSight(eye, spot + Chest)) return;
        target.Damage(ColossusDamage, giant.Npc, Vector3.Normalize(spot + Chest - eye) * ColossusPush + Vector3.UnitY * 4f);
    }

    bool Targetable(IPlayer player) => _playing.Contains(player) && !_out.Contains(player) && player.Alive && player.Position is not null;

    static float Angle(Vector3 a, Vector3 b) => MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1f, 1f)) * 180f / MathF.PI;

    static Vector3 Turn(Vector3 from, Vector3 to, float maxDegrees)
    {
        float angle = Angle(from, to);
        if (angle <= maxDegrees) return to;
        Vector3 axis = Vector3.Cross(from, to);
        if (axis.LengthSquared() < 1e-8f) axis = Vector3.Cross(from, Vector3.UnitY);
        if (axis.LengthSquared() < 1e-8f) axis = Vector3.UnitX;
        return Vector3.Normalize(Vector3.Transform(from, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), maxDegrees * MathF.PI / 180f)));
    }

    static Vector3 SkyDirection()
    {
        float yaw = Random.Shared.NextSingle() * MathF.Tau;
        float pitch = (SkyPitchMin + Random.Shared.NextSingle() * (SkyPitchMax - SkyPitchMin)) * MathF.PI / 180f;
        return new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));
    }

    void KilledByNpc(IPlayer victim, INpc killer)
    {
        lock (_sync)
        {
            if (!_giants.Any(giant => giant.Npc.Id == killer.Id) || !_playing.Contains(victim) || !Out(victim, "The Colossus got you!")) return;
            _lost.Add(victim);
            _respawning.Add(victim);
        }
    }

    void Respawned()
    {
        if (_respawning.Count == 0) return;
        IReadOnlyList<IPlayer> present = _world.Players;
        foreach (IPlayer player in _respawning.ToList())
        {
            if (!present.Contains(player)) _respawning.Remove(player);
            else if (player.Alive && _respawning.Remove(player)) ToLobby(player);
        }
    }

    IPlayer? NextTarget(Giant giant)
    {
        IPlayer[] candidates = [.. Playing().Where(Targetable).OrderBy(player => player.Actor)];
        if (candidates.Length == 0) return null;
        int start = Array.FindIndex(candidates, player => player.Actor > giant.LastTarget);
        if (start < 0) start = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            IPlayer player = candidates[(start + i) % candidates.Length];
            if (_world.LineOfSight(giant.Npc.EyePosition, player.Position!.Value + Chest)) return player;
        }
        return null;
    }

    void Dismiss(long now)
    {
        foreach (Giant giant in _giants)
            _fallen.Add((giant.Npc, (giant.Down ? giant.DownAt : now) + ColossusFall));
        _giants.Clear();
    }

    void Bury(long now)
    {
        for (int i = _fallen.Count - 1; i >= 0; i--)
            if (now >= _fallen[i].RemoveAt)
            {
                _fallen[i].Npc.Remove();
                _fallen.RemoveAt(i);
            }
    }

    void Check(IPlayer player, Goal goal, bool met)
    {
        lock (_sync)
            if (met && goal == _goal && _playing.Contains(player)) Done(player);
    }

    void Done(IPlayer player)
    {
        if (_next < 0 || _pauseUntil > 0 || !_done.Add(player)) return;
        player.Notify("Done!", brief: true);
        if (Playing().All(_done.Contains)) EndRound(Environment.TickCount64);
    }

    void EndRound(long now)
    {
        _world.Notify($"Round {_round} over", brief: true);
        Summary();
        Drain(EbbSeconds);
        Dismiss(now);
        if (_next < Tasks.Length) _pauseUntil = now + Pause;
        else Give();
    }

    void Stop(long now)
    {
        _world.Broadcast($"{Title} {Tint("stopped", Ember)}");
        Drain(EbbSeconds);
        Dismiss(now);
        foreach (IPlayer player in Remaining().ToList()) ToLobby(player);
        Finish(now);
    }

    void Finish(long now)
    {
        _next = -1;
        _pauseUntil = 0;
        _restUntil = now + Rest;
        _announced = -1;
        SetState(_world, false);
    }

    void Summary()
    {
        bool survival = _goal is Goal.Water or Goal.Colossus;
        List<IPlayer> fallen = [.. Playing().Where(player => survival ? _lost.Contains(player) : !_done.Contains(player))];
        if (!survival)
            foreach (IPlayer player in fallen)
                Lose(player, "You're out!");
        if (fallen.Count > 0) _world.Broadcast(Tint($"Out: {Names(fallen)}", Ember));
    }

    IEnumerable<IPlayer> Playing() => _world.Players.Where(_playing.Contains);

    IEnumerable<IPlayer> Remaining() => _world.Players.Where(player => _contestants.Contains(player) && !_out.Contains(player));

    static string Names(IEnumerable<IPlayer> players)
    {
        string[] names = [.. players.Select(player => player.Name)];
        return names.Length < 2 ? string.Concat(names) : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }

    void Lose(IPlayer player, string message)
    {
        if (Out(player, message)) ToLobby(player);
    }

    bool Out(IPlayer player, string message)
    {
        if (!_out.Add(player)) return false;
        player.Notify(message, brief: true);
        return true;
    }

    static void ToLobby(IPlayer player) => player.SetTeam(Team.Red);

    static string PlayerCount(int count) => count == 1 ? "1 player" : $"{count} players";

    static string Waiting(int count, int min) =>
        $"{Tint("Waiting for players", Light)} {Bold(Tint($"{count}/{min}", Blend(Heat, count / (float)min)))}";

    static string Starting() => $"{Tint("Game starts in", Light)} {Bold(Tint($"{LobbyCountdown}", Gold))}";

    static string Count(int seconds) => Bold(Tint($"{seconds}", Heat[Math.Clamp(seconds - 1, 0, Heat.Length - 1)]));

    static string Bold(string text) => $"<b>{text}</b>";

    static string Tint(string text, int rgb) => $"<color=#{rgb:X6}>{text}</color>";

    static int Blend(int[] stops, float t)
    {
        float at = Math.Clamp(t, 0f, 1f) * (stops.Length - 1);
        int i = Math.Min((int)at, stops.Length - 2);
        return Mix(stops[i], stops[i + 1], at - i);
    }

    static int Mix(int from, int to, float t)
    {
        int Channel(int shift)
        {
            int a = (from >> shift) & 0xFF;
            int b = (to >> shift) & 0xFF;
            return (int)MathF.Round(a + (b - a) * t);
        }
        return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    void Update(float elapsed)
    {
        lock (_sync)
        {
            long now = Environment.TickCount64;
            Respawned();
            Bury(now);
            if (_next < 0)
            {
                Lobby(now);
                return;
            }
            if (now >= _settleUntil && StateBox(_world) is int box && _world.Toggled(box) == false)
            {
                Stop(now);
                return;
            }
            if (_pauseUntil > 0)
            {
                if (now < _pauseUntil) return;
                _pauseUntil = 0;
                Give();
                return;
            }

            if (_goal == Goal.Colossus)
            {
                Hunt(now);
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
            _world.Broadcast(Count(Marks[_mark++] / 1000));
        }
    }
}
