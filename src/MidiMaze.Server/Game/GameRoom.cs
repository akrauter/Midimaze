namespace MidiMaze.Server.Game;

/// <summary>
/// One arena: players and bots, shots, and the round cycle (play -> results -> new maze -> play).
/// All state changes happen under <see cref="_gate"/>: hub calls (join, leave, input) come from
/// SignalR threads, <see cref="Tick"/> from the game loop.
/// </summary>
public sealed class GameRoom
{
    private enum Phase { Play, Over }

    private sealed class Player
    {
        public required int Id { get; init; }
        public string? ConnectionId { get; init; }
        public BotBrain? Brain { get; init; }
        public required string Name { get; init; }
        public required int Hue { get; init; }
        public int Team { get; init; }

        public double X, Y, A;
        public int Hp;
        public double RespawnIn;
        public double Cooldown;
        public int Kills, Hits, Deaths;
        public int Ack;
        public double InputBudget = GameConfig.InputBudgetMax;
        public readonly Queue<InputCommand> Inputs = new();

        public bool IsBot => Brain is not null;
        public bool Alive => Hp > 0;
        public int Score => Kills * 5 + Hits;
    }

    private sealed class Shot
    {
        public required int Id { get; init; }
        public required int OwnerId { get; init; }
        public required int OwnerTeam { get; init; }
        public double X, Y, A;
        public double Life = GameConfig.ShotLife;
    }

    private static readonly string[] BotNames =
    [
        "Grinsi", "Zack", "Blinky", "Pinky", "Clyde", "Smirk", "Kringel", "Zapp",
        "Bolzen", "Lulu", "Rumpel", "Fips", "Knuff", "Moppel", "Trixie", "Wusel",
    ];

    private readonly object _gate = new();
    private readonly Dictionary<int, Player> _players = new();
    private readonly Dictionary<string, int> _byConnection = new();
    private readonly List<Shot> _shots = new();
    private readonly List<HitEvent> _events = new();
    private readonly int[] _teamKills = new int[3]; // index = team (1 red, 2 blue)
    private readonly Random _rng;

    private int _nextPlayerId = 1;
    private int _nextShotId = 1;
    private long _tick;
    private Phase _phase = Phase.Play;
    private double _phaseLeft;
    private bool _mapDirty;
    private bool _rosterDirty;

    public string Id { get; }
    public RoomSettings Settings { get; }
    public MazeMap Map { get; private set; }

    public GameRoom(string id, RoomSettings settings, int? seed = null)
    {
        Id = id;
        Settings = settings;
        _rng = seed is { } s ? new Random(s) : new Random();
        Map = NewMap();
        _phaseLeft = settings.RoundSeconds;
    }

    /// <summary>Fixed arena, used by tests.</summary>
    public GameRoom(MazeMap map, int seed, RoomSettings? settings = null)
    {
        Id = "test";
        Settings = settings ?? new RoomSettings("Test", GameMode.FreeForAll, GameConfig.DefaultRoundSeconds, 0);
        _rng = new Random(seed);
        Map = map;
        _phaseLeft = Settings.RoundSeconds;
    }

    /// <summary>Human players only.</summary>
    public int HumanCount
    {
        get { lock (_gate) return _byConnection.Count; }
    }

    public int BotCount
    {
        get { lock (_gate) return _players.Count - _byConnection.Count; }
    }

    private string ModeName => Settings.Mode == GameMode.Teams ? "teams" : "ffa";

    private MazeMap NewMap() =>
        MazeMap.Generate(GameConfig.MazeCellsWide, GameConfig.MazeCellsHigh, _rng.Next());

    public RoomInfo GetInfo()
    {
        lock (_gate)
        {
            return new RoomInfo(Id, Settings.Name, ModeName, _byConnection.Count,
                _players.Count - _byConnection.Count, GameConfig.MaxPlayers,
                _phase == Phase.Play ? "play" : "over", Settings.RoundSeconds);
        }
    }

    // ---- lobby -------------------------------------------------------------------------------

    /// <summary>Returns null if the room is full or this connection has already joined.</summary>
    public Welcome? Join(string connectionId, string? name)
    {
        lock (_gate)
        {
            if (_byConnection.ContainsKey(connectionId) || _byConnection.Count >= GameConfig.MaxPlayers)
                return null;

            var id = _nextPlayerId++;
            var team = Settings.Mode == GameMode.Teams ? SmallerTeam() : 0;
            var player = new Player
            {
                Id = id,
                ConnectionId = connectionId,
                Name = CleanName(name, id),
                Hue = HueFor(id, team),
                Team = team,
            };
            _players[id] = player;
            _byConnection[connectionId] = id;
            Respawn(player);
            BalanceBots();
            _rosterDirty = true;

            return new Welcome(
                id, Id, Settings.Name, ModeName,
                Map.ToRows(),
                new ConfigDto(GameConfig.TickRate, GameConfig.MoveSpeed, GameConfig.TurnSpeed,
                    GameConfig.PlayerRadius, GameConfig.MaxHealth, GameConfig.RespawnDelay),
                RosterUnsafe());
        }
    }

    /// <summary>Returns true if a player was removed.</summary>
    public bool Leave(string connectionId)
    {
        lock (_gate)
        {
            if (!_byConnection.Remove(connectionId, out var id))
                return false;

            _players.Remove(id);
            _rosterDirty = true;

            if (_byConnection.Count == 0)
                ResetEmpty();
            else
                BalanceBots();
            return true;
        }
    }

    private int SmallerTeam()
    {
        var red = _players.Values.Count(p => p.Team == 1);
        var blue = _players.Values.Count(p => p.Team == 2);
        return red <= blue ? 1 : 2;
    }

    private static int HueFor(int id, int team) => team switch
    {
        1 => GameConfig.RedHue,
        2 => GameConfig.BlueHue,
        _ => (int)(id * 137.508 % 360),
    };

    /// <summary>Keeps the number of bots at the room's setting, giving up slots to humans.</summary>
    private void BalanceBots()
    {
        var desired = _byConnection.Count == 0
            ? 0
            : Math.Min(Settings.Bots, GameConfig.MaxPlayers - _byConnection.Count);

        var bots = _players.Values.Where(p => p.IsBot).OrderByDescending(p => p.Id).ToList();
        while (bots.Count > desired)
        {
            _players.Remove(bots[0].Id);
            bots.RemoveAt(0);
            _rosterDirty = true;
        }

        for (var i = bots.Count; i < desired; i++)
        {
            var id = _nextPlayerId++;
            var team = Settings.Mode == GameMode.Teams ? SmallerTeam() : 0;
            var bot = new Player
            {
                Id = id,
                Name = BotNames[id % BotNames.Length],
                Hue = HueFor(id, team),
                Team = team,
                Brain = new BotBrain(_rng.Next()),
            };
            _players[id] = bot;
            Respawn(bot);
            _rosterDirty = true;
        }
    }

    /// <summary>Nobody is left: drop bots and scores and prepare a fresh maze for the next crowd.</summary>
    private void ResetEmpty()
    {
        _players.Clear();
        _shots.Clear();
        _events.Clear();
        Array.Clear(_teamKills);
        Map = NewMap();
        _phase = Phase.Play;
        _phaseLeft = Settings.RoundSeconds;
        _mapDirty = false;
        _rosterDirty = false;
    }

    public PlayerInfo[] GetRoster()
    {
        lock (_gate) return RosterUnsafe();
    }

    private PlayerInfo[] RosterUnsafe() =>
        _players.Values.OrderBy(p => p.Id)
            .Select(p => new PlayerInfo(p.Id, p.Name, p.Hue, p.Team, p.IsBot)).ToArray();

    private static string CleanName(string? name, int id)
    {
        var chars = (name ?? "").Where(c => !char.IsControl(c)).ToArray();
        var clean = new string(chars).Trim();
        if (clean.Length > GameConfig.MaxNameLength)
            clean = clean[..GameConfig.MaxNameLength];
        return clean.Length == 0 ? $"Smiley{id}" : clean;
    }

    // ---- input -------------------------------------------------------------------------------

    public void EnqueueInput(string connectionId, InputCommand cmd)
    {
        lock (_gate)
        {
            if (!_byConnection.TryGetValue(connectionId, out var id))
                return;

            var queue = _players[id].Inputs;
            queue.Enqueue(cmd);
            while (queue.Count > GameConfig.InputQueueMax)
                queue.Dequeue();
        }
    }

    // ---- simulation --------------------------------------------------------------------------

    public void Tick()
    {
        lock (_gate)
        {
            _tick++;

            _phaseLeft -= GameConfig.Dt;
            if (_phaseLeft <= 0)
            {
                if (_phase == Phase.Play) EndRound();
                else StartRound();
            }

            foreach (var p in _players.Values)
                UpdatePlayer(p);
            UpdateShots();
        }
    }

    private void EndRound()
    {
        _phase = Phase.Over;
        _phaseLeft = GameConfig.RoundOverSeconds;
        _shots.Clear();
    }

    private void StartRound()
    {
        Map = NewMap();
        _phase = Phase.Play;
        _phaseLeft = Settings.RoundSeconds;
        _shots.Clear();
        Array.Clear(_teamKills);
        _mapDirty = true;

        foreach (var p in _players.Values)
        {
            p.Kills = p.Hits = p.Deaths = 0;
            Respawn(p);
        }
    }

    private void UpdatePlayer(Player p)
    {
        p.Cooldown = Math.Max(0, p.Cooldown - GameConfig.Dt);
        p.InputBudget = Math.Min(GameConfig.InputBudgetMax, p.InputBudget + GameConfig.InputBudgetPerTick);

        if (_phase == Phase.Over)
        {
            DrainIgnored(p); // results screen: nobody moves
            return;
        }

        if (!p.Alive)
        {
            DrainIgnored(p);
            p.RespawnIn -= GameConfig.Dt;
            if (p.RespawnIn <= 0)
                Respawn(p);
            return;
        }

        if (p.Brain is { } brain)
        {
            RunBot(p, brain);
            return;
        }

        while (p.InputBudget >= 1 && p.Inputs.TryDequeue(out var cmd))
        {
            p.InputBudget -= 1;
            p.Ack = cmd.Seq;
            Apply(p,
                Math.Clamp(cmd.Forward, -1, 1), Math.Clamp(cmd.Strafe, -1, 1), Math.Clamp(cmd.Turn, -1, 1),
                cmd.Fire);
        }
    }

    private static void DrainIgnored(Player p)
    {
        while (p.Inputs.TryDequeue(out var ignored))
            p.Ack = ignored.Seq;
    }

    private void RunBot(Player bot, BotBrain brain)
    {
        var enemies = new List<BotBrain.Target>();
        foreach (var o in _players.Values)
        {
            if (o.Id == bot.Id || !o.Alive)
                continue;
            if (Settings.Mode == GameMode.Teams && o.Team == bot.Team)
                continue;
            enemies.Add(new BotBrain.Target(o.Id, o.X, o.Y));
        }

        var action = brain.Think(Map, bot.X, bot.Y, bot.A, enemies);
        Apply(bot, action.Forward, action.Strafe, action.Turn, action.Fire);
    }

    private void Apply(Player p, int forward, int strafe, int turn, bool fire)
    {
        Movement.Step(Map, ref p.X, ref p.Y, ref p.A, forward, strafe, turn);

        if (fire && p.Cooldown <= 0)
        {
            p.Cooldown = GameConfig.ShotCooldown;
            Fire(p);
        }
    }

    private void Fire(Player p)
    {
        var x = p.X + Math.Cos(p.A) * GameConfig.ShotSpawnOffset;
        var y = p.Y + Math.Sin(p.A) * GameConfig.ShotSpawnOffset;
        if (Map.IsWall((int)Math.Floor(x), (int)Math.Floor(y)))
            return; // muzzle is inside a wall

        _shots.Add(new Shot { Id = _nextShotId++, OwnerId = p.Id, OwnerTeam = p.Team, X = x, Y = y, A = p.A });
    }

    private void UpdateShots()
    {
        // Three sub-steps per tick so a fast shot cannot tunnel through a player.
        const int subSteps = 3;
        var step = GameConfig.ShotSpeed * GameConfig.Dt / subSteps;
        var teams = Settings.Mode == GameMode.Teams;

        for (var i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            s.Life -= GameConfig.Dt;
            var gone = s.Life <= 0;

            for (var k = 0; k < subSteps && !gone; k++)
            {
                s.X += Math.Cos(s.A) * step;
                s.Y += Math.Sin(s.A) * step;

                if (Map.IsWall((int)Math.Floor(s.X), (int)Math.Floor(s.Y)))
                {
                    gone = true;
                    break;
                }

                foreach (var victim in _players.Values)
                {
                    if (victim.Id == s.OwnerId || !victim.Alive)
                        continue;
                    if (teams && victim.Team == s.OwnerTeam)
                        continue; // no friendly fire: shots pass through teammates

                    var dx = victim.X - s.X;
                    var dy = victim.Y - s.Y;
                    if (dx * dx + dy * dy > GameConfig.ShotHitRadius * GameConfig.ShotHitRadius)
                        continue;

                    RegisterHit(s.OwnerId, victim);
                    gone = true;
                    break;
                }
            }

            if (gone)
                _shots.RemoveAt(i);
        }
    }

    private void RegisterHit(int shooterId, Player victim)
    {
        victim.Hp = Math.Max(0, victim.Hp - GameConfig.ShotDamage);
        var killed = !victim.Alive;
        if (killed)
        {
            victim.Deaths++;
            victim.RespawnIn = GameConfig.RespawnDelay;
        }

        if (_players.TryGetValue(shooterId, out var shooter))
        {
            shooter.Hits++;
            if (killed)
            {
                shooter.Kills++;
                if (Settings.Mode == GameMode.Teams)
                    _teamKills[shooter.Team]++;
            }
        }

        _events.Add(new HitEvent(shooterId, victim.Id, killed));
    }

    private void Respawn(Player p)
    {
        // Of a handful of random spots, take the one farthest away from every living player.
        var candidates = Map.SpawnCandidates().ToList();
        var best = candidates[_rng.Next(candidates.Count)];
        var bestScore = -1.0;

        for (var i = 0; i < 8; i++)
        {
            var c = candidates[_rng.Next(candidates.Count)];
            var nearest = double.MaxValue;
            foreach (var other in _players.Values)
            {
                if (other.Id == p.Id || !other.Alive)
                    continue;
                var d = (other.X - c.X) * (other.X - c.X) + (other.Y - c.Y) * (other.Y - c.Y);
                nearest = Math.Min(nearest, d);
            }

            if (nearest > bestScore)
            {
                bestScore = nearest;
                best = c;
            }
        }

        p.X = best.X;
        p.Y = best.Y;
        p.A = OpenestDirection(best.X, best.Y);
        p.Hp = GameConfig.MaxHealth;
        p.RespawnIn = 0;
        p.Cooldown = 0;
        p.Inputs.Clear();
    }

    /// <summary>Of the four axis directions, the one with the longest free run (ties are random), so nobody spawns staring at a wall.</summary>
    private double OpenestDirection(double x, double y)
    {
        var best = 0.0;
        var bestFree = -1.0;
        var offset = _rng.Next(4);

        for (var i = 0; i < 4; i++)
        {
            var angle = ((i + offset) % 4 - 2) * Math.PI / 2; // -pi, -pi/2, 0, pi/2
            double dx = Math.Round(Math.Cos(angle)), dy = Math.Round(Math.Sin(angle));

            var free = 0.0;
            while (free < 30 && !Map.IsWall((int)Math.Floor(x + dx * (free + 0.5)), (int)Math.Floor(y + dy * (free + 0.5))))
                free += 1;

            if (free > bestFree)
            {
                bestFree = free;
                best = angle;
            }
        }
        return best;
    }

    // ---- snapshots ---------------------------------------------------------------------------

    /// <summary>Builds the current snapshot and clears the pending events. Null when nobody is here.</summary>
    public Snapshot? TakeSnapshot()
    {
        lock (_gate)
        {
            if (_byConnection.Count == 0)
            {
                _events.Clear();
                return null;
            }

            var players = _players.Values.OrderBy(p => p.Id).Select(p => new PlayerState(
                p.Id, Round(p.X), Round(p.Y), Round(p.A),
                p.Hp, Math.Round(Math.Max(0, p.RespawnIn), 1),
                p.Kills, p.Hits, p.Deaths, p.Score,
                p.Ack)).ToArray();

            var shots = _shots.Select(s => new ShotState(s.Id, Round(s.X), Round(s.Y), Round(s.A))).ToArray();
            var events = _events.ToArray();
            _events.Clear();

            var map = _mapDirty ? Map.ToRows() : null;
            var roster = _rosterDirty || _mapDirty ? RosterUnsafe() : null;
            _mapDirty = false;
            _rosterDirty = false;

            return new Snapshot(
                _tick,
                _phase == Phase.Play ? "play" : "over",
                Math.Round(Math.Max(0, _phaseLeft), 1),
                [_teamKills[1], _teamKills[2]],
                players, shots, events, map, roster);
        }
    }

    private static double Round(double v) => Math.Round(v, 3);

    // ---- test hooks --------------------------------------------------------------------------

    /// <summary>Put a player at an exact spot.</summary>
    internal void Place(string connectionId, double x, double y, double angle)
    {
        lock (_gate)
        {
            var p = _players[_byConnection[connectionId]];
            p.X = x;
            p.Y = y;
            p.A = angle;
        }
    }

    /// <summary>Jump to the end of the current phase on the next tick.</summary>
    internal void ExpirePhase()
    {
        lock (_gate) _phaseLeft = 0;
    }
}
