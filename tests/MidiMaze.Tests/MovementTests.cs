using MidiMaze.Server.Game;

namespace MidiMaze.Tests;

public class MovementTests
{
    private static readonly MazeMap Hall = MazeMap.FromRows(
        "#######",
        "#.....#",
        "#.....#",
        "#######");

    [Fact]
    public void Moving_forward_advances_along_the_heading()
    {
        double x = 2.5, y = 1.5, a = 0;
        Movement.Step(Hall, ref x, ref y, ref a, forward: 1, strafe: 0, turn: 0);

        Assert.Equal(2.5 + GameConfig.MoveSpeed * GameConfig.Dt, x, 6);
        Assert.Equal(1.5, y, 6);
    }

    [Fact]
    public void Walls_stop_the_player()
    {
        double x = 5.5, y = 1.5, a = 0;
        for (var i = 0; i < 60; i++)
            Movement.Step(Hall, ref x, ref y, ref a, 1, 0, 0);

        Assert.True(x <= 6 - GameConfig.PlayerRadius + 1e-9);
        Assert.False(Movement.Collides(Hall, x, y));
    }

    [Fact]
    public void Player_slides_along_a_wall()
    {
        // heading diagonally into the east wall: x is blocked, y keeps moving
        double x = 5.6, y = 1.3, a = Math.PI / 4;
        Movement.Step(Hall, ref x, ref y, ref a, 1, 0, 0);
        var y0 = y;
        for (var i = 0; i < 5; i++)
            Movement.Step(Hall, ref x, ref y, ref a, 1, 0, 0);

        Assert.True(y > y0);
    }

    [Fact]
    public void Diagonal_movement_is_not_faster()
    {
        double x = 3, y = 1.5, a = 0;
        Movement.Step(Hall, ref x, ref y, ref a, 1, 1, 0);
        var dist = Math.Sqrt((x - 3) * (x - 3) + (y - 1.5) * (y - 1.5));

        Assert.Equal(GameConfig.MoveSpeed * GameConfig.Dt, dist, 6);
    }

    [Fact]
    public void Turning_wraps_the_angle()
    {
        double x = 3, y = 1.5, a = Math.PI - 0.01;
        Movement.Step(Hall, ref x, ref y, ref a, 0, 0, turn: 1);

        Assert.InRange(a, -Math.PI, Math.PI);
        Assert.True(a < 0);
    }
}
