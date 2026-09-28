using MidiMaze.Server.Game;

namespace MidiMaze.Tests;

public class MazeMapTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(2026)]
    public void Generated_maze_has_solid_border_and_is_fully_connected(int seed)
    {
        var map = MazeMap.Generate(GameConfig.MazeCellsWide, GameConfig.MazeCellsHigh, seed);

        for (var x = 0; x < map.Width; x++)
        {
            Assert.True(map.IsWall(x, 0));
            Assert.True(map.IsWall(x, map.Height - 1));
        }
        for (var y = 0; y < map.Height; y++)
        {
            Assert.True(map.IsWall(0, y));
            Assert.True(map.IsWall(map.Width - 1, y));
        }

        // flood fill from the first cell must reach every floor tile
        var seen = new HashSet<(int, int)>();
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((1, 1));
        seen.Add((1, 1));
        while (queue.TryDequeue(out var c))
        {
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = (c.X + dx, c.Y + dy);
                if (!map.IsWall(n.Item1, n.Item2) && seen.Add(n))
                    queue.Enqueue((n.Item1, n.Item2));
            }
        }

        var floor = 0;
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
                if (!map.IsWall(x, y)) floor++;

        Assert.Equal(floor, seen.Count);
    }

    [Fact]
    public void Same_seed_gives_same_maze()
    {
        var a = MazeMap.Generate(8, 6, 7).ToRows();
        var b = MazeMap.Generate(8, 6, 7).ToRows();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Spawn_candidates_are_floor()
    {
        var map = MazeMap.Generate(8, 6, 3);
        Assert.All(map.SpawnCandidates(), c => Assert.False(map.IsWall((int)c.X, (int)c.Y)));
    }
}
