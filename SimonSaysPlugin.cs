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
    const long Calm = 8_000;
    const long Rise = 7_500;
    const long FreezeDelay = 6_000;
    const long Freeze = 7_000;
    const float MoveTolerance = 1f;
    const long BoostEvery = 6_000;
    const int TntWaves = 3;
    const long TntGap = 7_000;
    const long TntWire = 1_000;
    const long TntFuse = 4_000;
    const long TntSettle = 2_500;
    const float TntSpread = 3f;
    const float TntDepth = 200f;
    const long DrainDelay = 5_000;
    const long DrainTime = 10_000;
    const long DrainEvery = 200;
    const float DrainTotal = 110f;
    const float MedkitSpread = 8f;
    const string SniperMarker = "sniper";
    const string LandMarker = "land";
    const long LandHold = 1_500;
    const long PortalGrace = 6_000;
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
    const string NightKey = "night";
    const long StateSettle = 3_000;
    const long SkyGap = 1_000;
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

    enum Goal { Wave, Jump, DropWeapon, PickUp, Water, Underwater, Freeze, Seek, Tnt, Colossus, Drain, Leap, TakeSword, DontJump }

    static readonly (string Task, double Seconds, Goal Goal)[] Tasks =
    [
        ("/wave", 30, Goal.Wave),
        ("jump", 15, Goal.Jump),
        ("drop your weapon", 10, Goal.DropWeapon),
        ("pick up a weapon", 10, Goal.PickUp),
        ("don't touch the water", (Calm + Rise) / 1000.0, Goal.Water),
        ("go underwater", 15, Goal.Underwater),
        ("don't move", (FreezeDelay + Freeze) / 1000.0, Goal.Freeze),
        ("hide", 20, Goal.Seek),
        ("survive the TNT", (TntGap * (TntWaves - 1) + TntFuse + TntSettle) / 1000.0, Goal.Tnt),
        ("kill the Colossus", 0, Goal.Colossus),
        ("pick up health packs", (DrainDelay + DrainTime + 1_000) / 1000.0, Goal.Drain),
        ("jump onto the bounce pad", 20, Goal.Leap),
        ("take a sword", 15, Goal.TakeSword),
        ("don't jump", 10, Goal.DontJump),
    ];
    static readonly int[] Marks = [5000, 4000, 3000, 2000, 1000];

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
    readonly Dictionary<IPlayer, Vector3> _spots = [];
    readonly Dictionary<IPlayer, (bool Night, long At)> _skies = [];
    (string Task, double Seconds, Goal Goal)[] _shuffledTasks = [];
    IServer _server = null!;
    IPluginStorage _store = null!;
    IWorld _world = null!;
    IWorld? _lobby;
    int? _autoWorld;
    int? _min;
    int? _stateBox;
    int? _statePlate;
    int? _nightPlate;
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
    long _freezeAt;
    long _dontJumpActiveAt;
    IPlayer? _seeker;
    readonly List<int> _tnts = [];
    int _tntPlate = -1;
    int _wavesLeft;
    long _nextWave;
    long _wireAt;
    long _blastAt;
    long _blastEnd;
    readonly List<int> _medkits = [];
    long _drainFrom;
    long _nextDrain;
    long _drainUntil;
    readonly HashSet<IPlayer> _aloft = [];
    readonly Dictionary<IPlayer, long> _landed = [];
    long _graceUntil;
    long _nextBoost;

    public void Load(IServer server)
    {
        _server = server;
        _store = server.Storage("simonsays");
        _autoWorld = Stored(WorldKey);
        _min = Stored(MinKey);
        _stateBox = Stored(StateKey);
        _statePlate = Stored(PlateKey);
        _nightPlate = Stored(NightKey);
        server.AddCommand("simonsays", Command);
        server.Update += Update;
        server.PlayerJoined += Joined;
        server.PlayerLeft += Left;
        server.PlayerGestured += (player, gesture) => Check(player, Goal.Wave, gesture == Gesture.Wave);
              server.PlayerAnimated += (player, animation) => 
        {
            // dude wtf im doing wrong
            if (_next >= 0 && _goal == Goal.DontJump && Environment.TickCount64 < _deadline && animation == AvatarAnimation.Jump && _playing.Contains(player))
            {
                if (Environment.TickCount64 >= _dontJumpActiveAt)
                {
                    if (_lost.Add(player)) Lose(player, "You jumped after the countdown!");
                }
            }
            Check(player, Goal.Jump, animation == AvatarAnimation.Jump);
        };
        server.PlayerSwitchedItem += (player, item) => {
            Check(player, Goal.DropWeapon, EmptyHanded(item));
            Check(player, Goal.TakeSword, item == AvatarItem.Sword);
        };
        server.PlayerPickedUp += (player, item) => {
            Check(player, Goal.PickUp, true);
        };
        server.PlayerKilledByNpc += KilledByNpc;
        server.PlayerHit += Hit;
    }

    public void Unload()
    {
        lock (_sync)
        {
            if (_next >= 0)
            {
                Unseek();
                ClearTnt();
                ClearMedkits();
                foreach (IPlayer player in Remaining().ToList()) ToLobby(player);
                SetState(_world, false);
            }
            Drain(0);
            foreach (Giant giant in _giants) giant.Npc.Remove();
            foreach ((INpc npc, _) in _fallen) npc.Remove();
            _giants.Clear();
            _fallen.Clear();
            _next = -1;
            ClearSkies();
        }
    }

    void Left(IPlayer player)
    {
        lock (_sync) _skies.Remove(player);
    }

    void Skies(long now)
    {
        if (_autoWorld is not int id || _nightPlate is not int plate) return;
        foreach (IWorld world in _server.Worlds.Where(open => open.Id == id))
            foreach (IPlayer player in world.Players)
            {
                if (player.Position is null) continue;
                bool night = Dark;
                (bool shown, long at) = _skies.GetValueOrDefault(player);
                if (shown == night || now - at < SkyGap) continue;
                if (player.PressLocally(plate)) _skies[player] = (night, now);
            }
    }

    void ClearSkies()
    {
        if (_nightPlate is int plate)
            foreach ((IPlayer player, (bool night, _)) in _skies)
                if (night) player.PressLocally(plate);
        _skies.Clear();
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
            case "night" when value == "off":
                ClearSkies();
                _nightPlate = null;
                Save(NightKey, null);
                player.Message("Night off");
                break;
            case "night" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int night):
                if (world.Find(night)?.Type is not ("PressurePlate" or "TriggerCube" or "TriggerBox"))
                {
                    player.Message($"{night} isn't a Pressure Plate in this world");
                    break;
                }
                Claim(world);
                ClearSkies();
                _nightPlate = night;
                Save(NightKey, night);
                player.Message($"Plate {night} switches night, on for the red team and off for the blue team");
                break;
            default:
                player.Message("Use config, config min <players>, config state <id>, config plate <id>, config night <id>, or off after any of them");
                break;
        }
    }

    void Claim(IWorld world)
    {
        if (_autoWorld == world.Id) return;
        ClearSkies();
        _autoWorld = world.Id;
        _min = null;
        _stateBox = null;
        _statePlate = null;
        _nightPlate = null;
        _lobby = null;
        Save(WorldKey, world.Id);
        Save(MinKey, null);
        Save(StateKey, null);
        Save(PlateKey, null);
        Save(NightKey, null);
    }

    string Describe()
    {
        if (_autoWorld is not int id) return "Not set up yet, try !simonsays config min 4";
        string start = _min is int min ? $"on, with {PlayerCount(min)}" : "off";
        string state = _stateBox is int box ? $"Toggle Box {box}" : "off";
        string plate = _statePlate is int pressed ? $"plate {pressed}" : "plate off";
        string night = _nightPlate is int sky ? $"night plate {sky}" : "night off";
        return $"World {id}: auto start {start}, round state {state}, {plate}, {night}";
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

        _shuffledTasks = [.. Tasks];
        for (int i = _shuffledTasks.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_shuffledTasks[i], _shuffledTasks[j]) = (_shuffledTasks[j], _shuffledTasks[i]);
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
        while (_next < _shuffledTasks.Length && Skipped(_shuffledTasks[_next].Goal) is { } round)
        {
            _world.Broadcast(Tint($"Skipping the {round} round", Sand));
            _next++;
        }
        if (_next == _shuffledTasks.Length || _playing.Count == 0)
        {
            _world.Broadcast(_playing.Count == 0
                ? $"{Tint("Nobody won", Ember)} {Title}"
                : $"{Bold(Tint(Names(_playing), Bright))} {Tint(_playing.Count == 1 ? "wins" : "win", Light)} {Title}{Tint("!", Light)}");
            foreach (IPlayer winner in _playing) ToLobby(winner);
            Finish(Environment.TickCount64);
            return;
        }

        (string task, double seconds, _goal) = _shuffledTasks[_next++];
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
        if (_goal == Goal.TakeSword)
            foreach (IPlayer player in _playing.Where(player => player.Holding == AvatarItem.Sword).ToList())
                Done(player);
        if (_goal == Goal.Water) FindWater(now);
        if (_goal == Goal.Freeze)
        {
            _spots.Clear();
            _freezeAt = now + FreezeDelay;
        }
        if (_goal == Goal.Seek) Choose(now);
        if (_goal == Goal.Tnt)
        {
            _wavesLeft = TntWaves;
            _nextWave = now;
        }
                if (_goal == Goal.Colossus)
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
        if (_goal == Goal.Drain)
        {
            _drainFrom = now + DrainDelay;
            _nextDrain = _drainFrom;
            _drainUntil = _drainFrom + DrainTime;
        }
        if (_goal == Goal.Leap) Lift(now);
                if (_goal == Goal.DontJump)
        {
            _dontJumpActiveAt = now + 5_000; // Ловушка включится ровно через 5 секунд кулдауна
        }
    }

    string? Skipped(Goal goal) => goal switch
    {
        Goal.Colossus when !ColossusFits() => "Colossus",
        Goal.Underwater when WaterLevel() is null => "underwater",
        Goal.Seek when _playing.Count < 2 => "hide and seek",
        Goal.Leap when _world.Markers(SniperMarker).Count == 0 || _world.Markers(LandMarker).Count == 0 => "bounce pad",
        _ => null,
    };

    float? WaterLevel() => _world.Objects.FirstOrDefault(obj => obj.Type == "WaterPlane")?.Position.Y;

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

        float level = Level(now);
        foreach (IPlayer player in Playing())
            if (player.Position is { } position && position.Y < level && _lost.Add(player))
                Lose(player, "You touched the water!");
    }

    float Level(long now) => _waterHome.Y + RiseHeight * Math.Clamp((now - _riseAt) / (float)Rise, 0f, 1f);

    void Soak()
    {
        if (WaterLevel() is not float level) return;
        foreach (IPlayer player in Playing())
            if (player.Position is { } position && position.Y + Chest.Y >= level && _lost.Add(player))
                Lose(player, "You're not underwater!");
    }

    void Watch(long now)
    {
        if (_freezeAt > 0)
        {
            if (now < _freezeAt) return;
            _freezeAt = 0;
            foreach (IPlayer player in Playing())
                if (player.Position is { } position) _spots[player] = position;
            _world.Notify("Freeze!", brief: true);
            return;
        }
        foreach ((IPlayer player, Vector3 spot) in _spots)
            if (player.Position is { } position && Vector3.Distance(position, spot) > MoveTolerance && _lost.Add(player))
                Lose(player, "You moved!");
    }

    void Drain(float seconds)
    {
    if (_water < 0) return;
    
    if (_addedWater) 
    {
        _world.Remove(_water); 
    }
    else 
    {
        _world.Move(_water, _waterHome, seconds: seconds);
    }
    
    _water = -1;
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

    void Choose(long now)
    {
        IPlayer[] players = [.. Playing()];
        _seeker = players[Random.Shared.Next(players.Length)];
        _nextBoost = now;
        _seeker.Equip(AvatarItem.Sword);
        _seeker.Notify("You're the seeker, hit everyone!", brief: true);
        _world.Broadcast($"{Bold(Tint(_seeker.Name, Bright))} {Tint("is the seeker!", Light)}");
    }

    void Chase(long now)
    {
        if (_seeker is null || now < _nextBoost) return;
        _seeker.GiveLightningSpeed();
        _nextBoost = now + BoostEvery;
    }

    void Hit(IPlayer victim, IPlayer attacker)
    {
        lock (_sync)
        {
            if (_goal != Goal.Seek || _seeker is null || attacker != _seeker || victim == _seeker || _pauseUntil > 0) return;
            if (_playing.Contains(victim) && _lost.Add(victim)) Lose(victim, "The seeker got you!");
        }
    }

    void Blast(long now)
    {
        if (_wavesLeft > 0 && now >= _nextWave) Plant(now);
        if (_wireAt > 0 && now >= _wireAt)
        {
            _wireAt = 0;
            foreach (int tnt in _tnts) _world.Link(_tntPlate, tnt);
        }
        if (_blastAt > 0 && now >= _blastAt)
        {
            _blastAt = 0;
            _blastEnd = now + TntSettle;
            _world.Press(_tntPlate);
        }
        if (_blastEnd == 0) return;
        foreach (IPlayer player in Playing())
            if (!player.Alive && _lost.Add(player) && Out(player, "Boom! You're out!"))
                _respawning.Add(player);
        if (now >= _blastEnd) ClearTnt();
    }

    bool Dark => _next >= 0 && _goal == Goal.Colossus && _pauseUntil == 0;

    void Scatter()
    {
        foreach (IPlayer player in Playing())
        {
            if (player.Position is not { } spot) continue;
            float angle = Random.Shared.NextSingle() * MathF.Tau;
            Vector3 near = spot + new Vector3(MathF.Cos(angle) * MedkitSpread, 0.5f, MathF.Sin(angle) * MedkitSpread);
            _medkits.Add(_world.Add("PickupItemSpawner", near, new Dictionary<string, object> { ["itemType"] = (int)AvatarItem.Health }).Id);
        }
    }

    void Sap(long now)
    {
        if (_drainFrom > 0)
        {
            if (now < _drainFrom) return;
            _drainFrom = 0;
            Scatter();
            _world.Notify("Your health is draining!", brief: true);
        }
        while (now >= _nextDrain && _nextDrain < _drainUntil)
        {
            _nextDrain += DrainEvery;
            foreach (IPlayer player in Playing())
                if (!_lost.Contains(player) && player.Alive) player.Damage(DrainTotal * DrainEvery / DrainTime);
        }
        foreach (IPlayer player in Playing())
            if (!player.Alive && _lost.Add(player) && Out(player, "You ran out of health!"))
                _respawning.Add(player);
    }

    void Lift(long now)
    {
        _aloft.Clear();
        _landed.Clear();
        _graceUntil = now + PortalGrace;
        IReadOnlyList<IWorldObject> bases = _world.Markers(SniperMarker);
        foreach (IPlayer player in Playing())
        {
            player.Equip(AvatarItem.Hand);
            player.Teleport(bases[Random.Shared.Next(bases.Count)].Position, TeleportMethod.Teleporter);
        }
    }

    bool Fall(long now)
    {
        if (_world.Markers(LandMarker) is not [var land, ..]) return false;
        foreach (IPlayer player in Playing())
        {
            if (_done.Contains(player) || _lost.Contains(player)) continue;
            if (now >= _graceUntil && _aloft.Contains(player) && !player.Alive)
            {
                _landed.Remove(player);
                if (_lost.Add(player) && Out(player, "You died in the fall!")) _respawning.Add(player);
                continue;
            }
            if (player.Position is not { } spot) continue;
            if (spot.Y > land.Position.Y)
            {
                _aloft.Add(player);
                _landed.Remove(player);
            }
                        else if (_aloft.Contains(player) && player.Alive)
            {
                // Без хардкода пж
                if (_done.Add(player)) player.Notify("Done!", brief: true);
            }
        }
        if (!Playing().All(player => _done.Contains(player) || _lost.Contains(player))) return false;
        EndRound(now);
        return true;
    }

    void ClearMedkits()
    {
        foreach (int medkit in _medkits) _world.Remove(medkit);
        _medkits.Clear();
    }

    void Plant(long now)
    {
        ClearTnt();
        _wavesLeft--;
        _nextWave = now + TntGap;
        Vector3[] spots = [.. Playing().Where(player => !_lost.Contains(player) && player.Alive).Select(player => player.Position).OfType<Vector3>()];
        if (spots.Length == 0) return;
        _tntPlate = _world.Add("PressurePlate", spots[0] with { Y = spots.Min(spot => spot.Y) - TntDepth }, new Dictionary<string, object> { ["hide"] = true }).Id;
        foreach (Vector3 spot in spots)
        {
            float angle = Random.Shared.NextSingle() * MathF.Tau;
            _tnts.Add(_world.Add("Explosives", spot + new Vector3(MathF.Cos(angle) * TntSpread, 0.5f, MathF.Sin(angle) * TntSpread)).Id);
        }
        _wireAt = now + TntWire;
        _blastAt = now + TntFuse;
    }

    void ClearTnt()
    {
        foreach (int tnt in _tnts) _world.Remove(tnt);
        _tnts.Clear();
        if (_tntPlate >= 0) _world.Remove(_tntPlate);
        _tntPlate = -1;
        _wireAt = _blastAt = _blastEnd = 0;
    }

    void Unseek()
    {
        _seeker?.Equip(AvatarItem.Hand);
        _seeker = null;
    }

void EndRound(long now)
{
    _deadline = long.MaxValue; 

    _world.Notify($"Round {_round} over", brief: true);
    
    if (_goal == Goal.Underwater) Soak();
    if (_goal == Goal.Seek) Unseek();
    if (_goal == Goal.Tnt) ClearTnt();
    if (_goal == Goal.Drain) ClearMedkits();
    
    Summary();
    
    Drain(EbbSeconds); 
    
    Dismiss(now);
    
    if (_next < _shuffledTasks.Length) 
        _pauseUntil = now + Pause;
    else 
        Give();
}

    void Stop(long now)
    {
        _world.Broadcast($"{Title} {Tint("stopped", Ember)}");
        Unseek();
        ClearTnt();
        ClearMedkits();
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
        bool survival = _goal is Goal.Water or Goal.Underwater or Goal.Freeze or Goal.Seek or Goal.Tnt or Goal.Colossus or Goal.Drain or Goal.DontJump;
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
            Skies(now);
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
            if (_goal == Goal.Freeze) Watch(now);
            if (_goal == Goal.Seek) Chase(now);
            if (_goal == Goal.Tnt) Blast(now);
            if (_goal == Goal.Drain) Sap(now);
            if (_goal == Goal.Leap && Fall(now)) return;
            if (_goal == Goal.DontJump) {
                // temp
            }
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
