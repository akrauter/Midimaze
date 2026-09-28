namespace MidiMaze.Server.Game;

/// <summary>
/// One fixed movement step. The browser client contains a line-for-line port of this
/// (wwwroot/js/movement.js) for client-side prediction - keep both in sync.
/// </summary>
public static class Movement
{
    public static void Step(MazeMap map, ref double x, ref double y, ref double a, int forward, int strafe, int turn)
    {
        a += turn * GameConfig.TurnSpeed * GameConfig.Dt;
        if (a > Math.PI) a -= 2 * Math.PI;
        else if (a <= -Math.PI) a += 2 * Math.PI;

        // Facing is (cos, sin); "right" is (-sin, cos) because the map's y axis points down.
        var mx = Math.Cos(a) * forward - Math.Sin(a) * strafe;
        var my = Math.Sin(a) * forward + Math.Cos(a) * strafe;
        var len = Math.Sqrt(mx * mx + my * my);
        if (len < 1e-9)
            return;
        if (len > 1)
        {
            mx /= len;
            my /= len;
        }

        var step = GameConfig.MoveSpeed * GameConfig.Dt;

        // Axis-separated so the player slides along walls.
        var nx = x + mx * step;
        if (!Collides(map, nx, y)) x = nx;
        var ny = y + my * step;
        if (!Collides(map, x, ny)) y = ny;
    }

    public static bool Collides(MazeMap map, double px, double py)
    {
        const double r = GameConfig.PlayerRadius;
        return map.IsWall((int)Math.Floor(px - r), (int)Math.Floor(py - r))
            || map.IsWall((int)Math.Floor(px + r), (int)Math.Floor(py - r))
            || map.IsWall((int)Math.Floor(px - r), (int)Math.Floor(py + r))
            || map.IsWall((int)Math.Floor(px + r), (int)Math.Floor(py + r));
    }
}
