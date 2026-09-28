namespace MidiMaze.Server.Game;

public enum GameMode { FreeForAll, Teams }

public enum BotDifficulty { Easy, Normal, Hard }

/// <summary>Per-room rules, fixed at creation.</summary>
public sealed record RoomSettings(
    string Name, GameMode Mode, int RoundSeconds, int Bots, BotDifficulty Difficulty = BotDifficulty.Normal);

/// <summary>One client input step (a fixed 1/30 s tick worth of held keys).</summary>
public sealed record InputCommand(int Seq, int Forward, int Strafe, int Turn, bool Fire);

/// <summary>Team is 0 (free-for-all), 1 (red) or 2 (blue).</summary>
public sealed record PlayerInfo(int Id, string Name, int Hue, int Team, bool Bot);

public sealed record ConfigDto(
    int TickRate,
    double MoveSpeed,
    double TurnSpeed,
    double PlayerRadius,
    int MaxHealth,
    double RespawnDelay);

public sealed record Welcome(
    int Id, string RoomId, string RoomName, string Mode,
    string[] Map, ConfigDto Config, PlayerInfo[] Roster);

public sealed record JoinResult(Welcome? Welcome, string? Error);

public sealed record PlayerState(
    int Id, double X, double Y, double A,
    int Hp, double Respawn,
    int Kills, int Hits, int Deaths, int Score,
    int Ack);

public sealed record ShotState(int Id, double X, double Y, double A);

/// <summary>Someone shot someone. Killed is true when the hit was fatal.</summary>
public sealed record HitEvent(int ShooterId, int VictimId, bool Killed);

/// <summary>
/// Phase is "play" or "over" (round finished, results shown); Time is the seconds left in that phase.
/// Map and Roster are only present in the snapshot right after they changed.
/// </summary>
public sealed record Snapshot(
    long Tick, string Phase, double Time, int[] TeamScores,
    PlayerState[] Players, ShotState[] Shots, HitEvent[] Events,
    string[]? Map, PlayerInfo[]? Roster);

/// <summary>Difficulty is "easy", "normal" or "hard" (anything else means normal).</summary>
public sealed record CreateRoomRequest(string? Name, string? Mode, int RoundSeconds, int Bots, string? Difficulty = null);

public sealed record RoomInfo(
    string Id, string Name, string Mode, int Players, int Bots, int MaxPlayers, string Phase, int RoundSeconds,
    string Difficulty);
