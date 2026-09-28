namespace MidiMaze.Server.Game;

/// <summary>
/// Decision making for one computer-controlled smiley. It only sees what a human could see
/// (line of sight) plus the enemies' positions for path-finding, and it aims imperfectly on
/// purpose so that humans stay competitive.
/// </summary>
internal sealed class BotBrain
{
    public readonly record struct Target(int Id, double X, double Y);
    public readonly record struct Action(int Forward, int Strafe, int Turn, bool Fire);

    private const double SightRange = 12;
    private const double ReactionSeconds = 0.3;
    private const double TurnDeadzone = 0.05;
    private const double AimTolerance = 0.10;

    private readonly Random _rng;

    private (int X, int Y)[] _path = [];
    private int _pathIndex;
    private double _repathIn;
    private (int X, int Y)? _wanderGoal;

    private double _seenFor;
    private double _aimJitter;
    private double _jitterIn;
    private int _strafeDir = 1;
    private double _strafeIn;

    private double _stuckTimer;
    private double _lastX = double.NaN, _lastY;
    private double _unstickIn;
    private int _unstickTurn = 1;

    public BotBrain(int seed) => _rng = new Random(seed);

    public Action Think(MazeMap map, double x, double y, double a, IReadOnlyList<Target> enemies)
    {
        const double dt = GameConfig.Dt;

        _jitterIn -= dt;
        if (_jitterIn <= 0)
        {
            _aimJitter = (_rng.NextDouble() - 0.5) * 0.10;
            _jitterIn = 0.4 + _rng.NextDouble() * 0.4;
        }

        _strafeIn -= dt;
        if (_strafeIn <= 0)
        {
            _strafeDir = _rng.Next(2) * 2 - 1;
            _strafeIn = 0.6 + _rng.NextDouble() * 0.8;
        }

        // Nearest enemy we can actually see.
        Target? visible = null;
        var best = double.MaxValue;
        foreach (var e in enemies)
        {
            var d2 = (e.X - x) * (e.X - x) + (e.Y - y) * (e.Y - y);
            if (d2 < best && d2 < SightRange * SightRange && HasLineOfSight(map, x, y, e.X, e.Y))
            {
                best = d2;
                visible = e;
            }
        }

        _seenFor = visible is null ? 0 : _seenFor + dt;

        if (visible is { } t)
        {
            var diff = Wrap(Math.Atan2(t.Y - y, t.X - x) + _aimJitter - a);
            var dist = Math.Sqrt(best);
            var forward = dist > 3.5 && Math.Abs(diff) < 0.8 ? 1 : dist < 2 ? -1 : 0;
            var strafe = dist < 8 ? _strafeDir : 0;
            var fire = _seenFor >= ReactionSeconds && Math.Abs(diff) < AimTolerance;
            return new Action(forward, strafe, TurnToward(diff), fire);
        }

        return Roam(map, x, y, a, enemies);
    }

    private Action Roam(MazeMap map, double x, double y, double a, IReadOnlyList<Target> enemies)
    {
        const double dt = GameConfig.Dt;

        // Back off for a moment when we have been wedged against something.
        _stuckTimer += dt;
        if (double.IsNaN(_lastX)) { _lastX = x; _lastY = y; }
        if (_stuckTimer >= 1)
        {
            var moved = Math.Sqrt((x - _lastX) * (x - _lastX) + (y - _lastY) * (y - _lastY));
            if (moved < 0.2)
            {
                _unstickIn = 0.5;
                _unstickTurn = _rng.Next(2) * 2 - 1;
                _repathIn = 0;
            }
            _stuckTimer = 0;
            _lastX = x;
            _lastY = y;
        }
        if (_unstickIn > 0)
        {
            _unstickIn -= dt;
            return new Action(1, 0, _unstickTurn, false);
        }

        var tile = ((int)Math.Floor(x), (int)Math.Floor(y));

        _repathIn -= dt;
        if (_repathIn <= 0 || _pathIndex >= _path.Length)
        {
            _repathIn = 0.6;
            var goal = ChooseGoal(map, tile, enemies);
            _path = FindPath(map, tile, goal);
            _pathIndex = Math.Max(0, Math.Min(1, _path.Length - 1));
        }

        if (_path.Length == 0)
            return default;

        var wp = _path[Math.Min(_pathIndex, _path.Length - 1)];
        var wx = wp.X + 0.5;
        var wy = wp.Y + 0.5;
        if ((wx - x) * (wx - x) + (wy - y) * (wy - y) < 0.06 && _pathIndex < _path.Length - 1)
            _pathIndex++;

        var diff = Wrap(Math.Atan2(wy - y, wx - x) - a);
        return new Action(Math.Abs(diff) < 0.5 ? 1 : 0, 0, TurnToward(diff), false);
    }

    private (int X, int Y) ChooseGoal(MazeMap map, (int X, int Y) from, IReadOnlyList<Target> enemies)
    {
        if (enemies.Count > 0)
        {
            var best = double.MaxValue;
            var goal = from;
            foreach (var e in enemies)
            {
                var d2 = (e.X - from.X) * (e.X - from.X) + (e.Y - from.Y) * (e.Y - from.Y);
                if (d2 < best)
                {
                    best = d2;
                    goal = ((int)Math.Floor(e.X), (int)Math.Floor(e.Y));
                }
            }
            return goal;
        }

        if (_wanderGoal is null || _wanderGoal == from)
        {
            var cells = map.SpawnCandidates().ToList();
            var c = cells[_rng.Next(cells.Count)];
            _wanderGoal = ((int)Math.Floor(c.X), (int)Math.Floor(c.Y));
        }
        return _wanderGoal.Value;
    }

    private static int TurnToward(double diff) =>
        Math.Abs(diff) > TurnDeadzone ? Math.Sign(diff) : 0;

    private static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    public static bool HasLineOfSight(MazeMap map, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        var steps = (int)Math.Ceiling(dist / 0.15);
        for (var i = 1; i < steps; i++)
        {
            var f = (double)i / steps;
            if (map.IsWall((int)Math.Floor(ax + dx * f), (int)Math.Floor(ay + dy * f)))
                return false;
        }
        return true;
    }

    /// <summary>Breadth-first path over floor tiles; returns [] when there is none.</summary>
    public static (int X, int Y)[] FindPath(MazeMap map, (int X, int Y) from, (int X, int Y) to)
    {
        if (map.IsWall(from.X, from.Y) || map.IsWall(to.X, to.Y))
            return [];

        var prev = new Dictionary<(int, int), (int X, int Y)?> { [from] = null };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(from);

        while (queue.TryDequeue(out var cur))
        {
            if (cur == to)
                break;

            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (X: cur.X + dx, Y: cur.Y + dy);
                if (map.IsWall(next.X, next.Y) || prev.ContainsKey(next))
                    continue;
                prev[next] = cur;
                queue.Enqueue(next);
            }
        }

        if (!prev.ContainsKey(to))
            return [];

        var path = new List<(int X, int Y)>();
        (int X, int Y)? step = to;
        while (step is { } s)
        {
            path.Add(s);
            step = prev[s];
        }
        path.Reverse();
        return path.ToArray();
    }
}
