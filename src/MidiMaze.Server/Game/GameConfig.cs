namespace MidiMaze.Server.Game;

/// <summary>
/// Gameplay constants. The client receives the movement values in the Welcome message so that
/// client-side prediction uses exactly the numbers the server simulates with.
/// </summary>
public static class GameConfig
{
    public const int TickRate = 30;
    public const double Dt = 1.0 / TickRate;

    public const double MoveSpeed = 3.2;      // tiles per second
    public const double TurnSpeed = 2.6;      // radians per second
    public const double PlayerRadius = 0.28;  // half-width of the collision box

    public const double ShotSpeed = 9.0;      // tiles per second
    public const double ShotHitRadius = 0.3;
    public const double ShotLife = 2.0;       // seconds
    public const double ShotCooldown = 0.4;   // seconds between two shots
    public const double ShotSpawnOffset = 0.3;

    public const int MaxHealth = 100;
    public const int ShotDamage = 34;         // three hits to go down
    public const double RespawnDelay = 3.0;   // seconds

    public const int MaxPlayers = 16;         // humans + bots per room
    public const int MaxNameLength = 16;
    public const int MaxRoomNameLength = 24;

    // Rooms and rounds
    public const string DefaultRoomId = "arena";
    public const int MaxRooms = 20;
    public const int MaxBots = 8;
    public const int MinRoundSeconds = 60;
    public const int MaxRoundSeconds = 600;
    public const int DefaultRoundSeconds = 180;
    public const double RoundOverSeconds = 10.0;

    // Teams: hues of the red and blue team
    public const int RedHue = 355;
    public const int BlueHue = 215;

    // Anti speed-hack: a client may send one command per tick on average, with a small burst allowance.
    public const double InputBudgetPerTick = 1.1;
    public const double InputBudgetMax = 3.0;
    public const int InputQueueMax = 6;

    public const int MazeCellsWide = 12;
    public const int MazeCellsHigh = 9;
}
