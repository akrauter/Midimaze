namespace MidiMaze.Server.Game;

/// <summary>
/// The lobby: owns all rooms and knows which connection sits in which room. The default room
/// always exists; rooms created by players disappear as soon as the last human leaves.
/// </summary>
public sealed class RoomManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GameRoom> _rooms = new();
    private readonly Dictionary<string, string> _roomOf = new(); // connection -> room id
    private readonly int? _seed;
    private int _roomCounter;

    public RoomManager(int? seed = null)
    {
        _seed = seed;
        _rooms[GameConfig.DefaultRoomId] = new GameRoom(
            GameConfig.DefaultRoomId,
            new RoomSettings("Arena", GameMode.FreeForAll, GameConfig.DefaultRoundSeconds, 3),
            seed);
    }

    public RoomInfo[] List()
    {
        GameRoom[] rooms;
        lock (_gate) rooms = _rooms.Values.ToArray();

        return rooms
            .Select(r => r.GetInfo())
            .OrderBy(r => r.Id == GameConfig.DefaultRoomId ? 0 : 1)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Null when the server already runs the maximum number of rooms.</summary>
    public RoomInfo? Create(CreateRoomRequest request)
    {
        lock (_gate)
        {
            if (_rooms.Count >= GameConfig.MaxRooms)
                return null;

            _roomCounter++;
            var name = (request.Name ?? "").Trim();
            if (name.Length > GameConfig.MaxRoomNameLength)
                name = name[..GameConfig.MaxRoomNameLength];
            if (name.Length == 0 || name.Any(char.IsControl))
                name = $"Raum {_roomCounter}";

            var settings = new RoomSettings(
                name,
                string.Equals(request.Mode, "teams", StringComparison.OrdinalIgnoreCase)
                    ? GameMode.Teams
                    : GameMode.FreeForAll,
                Math.Clamp(request.RoundSeconds == 0 ? GameConfig.DefaultRoundSeconds : request.RoundSeconds,
                    GameConfig.MinRoundSeconds, GameConfig.MaxRoundSeconds),
                Math.Clamp(request.Bots, 0, GameConfig.MaxBots),
                ParseDifficulty(request.Difficulty));

            var id = Guid.NewGuid().ToString("N")[..6];
            var room = new GameRoom(id, settings, _seed);
            _rooms[id] = room;
            return room.GetInfo();
        }
    }

    private static BotDifficulty ParseDifficulty(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "easy" => BotDifficulty.Easy,
        "hard" => BotDifficulty.Hard,
        _ => BotDifficulty.Normal,
    };

    /// <summary>Puts the connection into the room, leaving its previous room first.</summary>
    public JoinResult Join(string connectionId, string roomId, string? name, out string? leftRoomId)
    {
        lock (_gate)
        {
            leftRoomId = LeaveUnsafe(connectionId);

            if (!_rooms.TryGetValue(roomId, out var room))
                return new JoinResult(null, "Diesen Raum gibt es nicht mehr.");

            var welcome = room.Join(connectionId, name);
            if (welcome is null)
                return new JoinResult(null, "Der Raum ist voll.");

            _roomOf[connectionId] = roomId;
            return new JoinResult(welcome, null);
        }
    }

    /// <summary>Returns the id of the room that was left, or null if the connection was in none.</summary>
    public string? Leave(string connectionId)
    {
        lock (_gate) return LeaveUnsafe(connectionId);
    }

    private string? LeaveUnsafe(string connectionId)
    {
        if (!_roomOf.Remove(connectionId, out var roomId))
            return null;

        if (_rooms.TryGetValue(roomId, out var room))
        {
            room.Leave(connectionId);
            if (roomId != GameConfig.DefaultRoomId && room.HumanCount == 0)
                _rooms.Remove(roomId);
        }
        return roomId;
    }

    public void Input(string connectionId, InputCommand cmd)
    {
        GameRoom? room;
        lock (_gate)
        {
            if (!_roomOf.TryGetValue(connectionId, out var roomId) || !_rooms.TryGetValue(roomId, out room))
                return;
        }
        room.EnqueueInput(connectionId, cmd);
    }

    /// <summary>Advances every room that has at least one human in it.</summary>
    public void Tick()
    {
        foreach (var room in Rooms())
            if (room.HumanCount > 0)
                room.Tick();
    }

    public IEnumerable<(string RoomId, Snapshot Snapshot)> TakeSnapshots()
    {
        foreach (var room in Rooms())
            if (room.TakeSnapshot() is { } snap)
                yield return (room.Id, snap);
    }

    private GameRoom[] Rooms()
    {
        lock (_gate) return _rooms.Values.ToArray();
    }
}
