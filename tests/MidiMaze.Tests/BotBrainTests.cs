using MidiMaze.Server.Game;

namespace MidiMaze.Tests;

public class BotBrainTests
{
    [Fact]
    public void Path_leads_around_walls()
    {
        var map = MazeMap.FromRows(
            "#######",
            "#..#..#",
            "#.##.##",
            "#.....#",
            "#######");

        var path = BotBrain.FindPath(map, (1, 1), (4, 1));

        Assert.NotEmpty(path);
        Assert.Equal((1, 1), path[0]);
        Assert.Equal((4, 1), path[^1]);
        Assert.All(path, t => Assert.False(map.IsWall(t.X, t.Y)));
        // every step moves to a neighbouring tile
        for (var i = 1; i < path.Length; i++)
            Assert.Equal(1, Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Y - path[i - 1].Y));
    }

    [Fact]
    public void No_path_to_walled_off_tile()
    {
        var map = MazeMap.FromRows(
            "#####",
            "#.#.#",
            "#####");

        Assert.Empty(BotBrain.FindPath(map, (1, 1), (3, 1)));
    }

    [Fact]
    public void Line_of_sight_is_blocked_by_walls()
    {
        var map = MazeMap.FromRows(
            "#########",
            "#...#...#",
            "#########");

        Assert.True(BotBrain.HasLineOfSight(map, 1.5, 1.5, 3.5, 1.5));
        Assert.False(BotBrain.HasLineOfSight(map, 1.5, 1.5, 6.5, 1.5));
    }

    [Fact]
    public void Bot_turns_toward_and_fires_at_a_visible_enemy()
    {
        var map = MazeMap.FromRows(
            "###########",
            "#.........#",
            "###########");
        var brain = new BotBrain(1);

        // enemy dead ahead: after the reaction delay the bot must fire
        var fired = false;
        for (var i = 0; i < GameConfig.TickRate * 3 && !fired; i++)
            fired = brain.Think(map, 1.5, 1.5, 0, [new BotBrain.Target(2, 7.5, 1.5)]).Fire;
        Assert.True(fired);

        // enemy behind: turn, do not fire yet
        var turning = new BotBrain(1).Think(map, 5.5, 1.5, 0, [new BotBrain.Target(2, 1.5, 1.5)]);
        Assert.NotEqual(0, turning.Turn);
        Assert.False(turning.Fire);
    }
}
