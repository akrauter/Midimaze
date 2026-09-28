namespace MidiMaze.Server.Game;

/// <summary>Tile grid: '#' is wall, '.' is floor. The outer border is always wall.</summary>
public sealed class MazeMap
{
    private readonly bool[] _walls;

    public int Width { get; }
    public int Height { get; }

    private MazeMap(int width, int height)
    {
        Width = width;
        Height = height;
        _walls = new bool[width * height];
        Array.Fill(_walls, true);
    }

    public bool IsWall(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height || _walls[y * Width + x];

    public string[] ToRows()
    {
        var rows = new string[Height];
        for (var y = 0; y < Height; y++)
        {
            var chars = new char[Width];
            for (var x = 0; x < Width; x++)
                chars[x] = IsWall(x, y) ? '#' : '.';
            rows[y] = new string(chars);
        }
        return rows;
    }

    /// <summary>Centers of all cells that are guaranteed to be floor (odd/odd grid positions).</summary>
    public IEnumerable<(double X, double Y)> SpawnCandidates()
    {
        for (var y = 1; y < Height; y += 2)
            for (var x = 1; x < Width; x += 2)
                if (!IsWall(x, y))
                    yield return (x + 0.5, y + 0.5);
    }

    /// <summary>
    /// Perfect maze via iterative depth-first backtracking, then some walls are knocked out to create
    /// loops and a few open rooms, so that fights do not end in dead-end corridors only.
    /// </summary>
    public static MazeMap Generate(int cellsWide, int cellsHigh, int seed, double loopChance = 0.12, int rooms = 3)
    {
        var rng = new Random(seed);
        var map = new MazeMap(cellsWide * 2 + 1, cellsHigh * 2 + 1);

        var visited = new bool[cellsWide, cellsHigh];
        var stack = new Stack<(int X, int Y)>();
        visited[0, 0] = true;
        map.Carve(1, 1);
        stack.Push((0, 0));

        var dirs = new (int Dx, int Dy)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Peek();

            // Fisher-Yates shuffle so the maze looks different per seed.
            for (var i = dirs.Length - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }

            var moved = false;
            foreach (var (dx, dy) in dirs)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= cellsWide || ny >= cellsHigh || visited[nx, ny])
                    continue;

                visited[nx, ny] = true;
                map.Carve(cx * 2 + 1 + dx, cy * 2 + 1 + dy); // the wall piece between the two cells
                map.Carve(nx * 2 + 1, ny * 2 + 1);
                stack.Push((nx, ny));
                moved = true;
                break;
            }

            if (!moved)
                stack.Pop();
        }

        // Loops: remove interior wall pieces that sit between two cells.
        for (var y = 1; y < map.Height - 1; y++)
            for (var x = 1; x < map.Width - 1; x++)
                if (x % 2 != y % 2 && map.IsWall(x, y) && rng.NextDouble() < loopChance)
                    map.Carve(x, y);

        // Rooms: clear a 3x3 block of tiles around a 2x2 group of cells.
        for (var r = 0; r < rooms && cellsWide > 2 && cellsHigh > 2; r++)
        {
            var cx = rng.Next(cellsWide - 1);
            var cy = rng.Next(cellsHigh - 1);
            for (var y = cy * 2 + 1; y <= cy * 2 + 3; y++)
                for (var x = cx * 2 + 1; x <= cx * 2 + 3; x++)
                    map.Carve(x, y);
        }

        return map;
    }

    /// <summary>Build a map from text rows ('#' = wall, anything else = floor). Used by tests.</summary>
    public static MazeMap FromRows(params string[] rows)
    {
        var map = new MazeMap(rows[0].Length, rows.Length);
        for (var y = 0; y < rows.Length; y++)
        {
            if (rows[y].Length != map.Width)
                throw new ArgumentException($"Row {y} has length {rows[y].Length}, expected {map.Width}.");
            for (var x = 0; x < map.Width; x++)
                if (rows[y][x] != '#')
                    map.Carve(x, y);
        }
        return map;
    }

    private void Carve(int x, int y) => _walls[y * Width + x] = false;
}
