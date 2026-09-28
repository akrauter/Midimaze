using MidiMaze.Server.Game;

namespace MidiMaze.Tests;

public class RoomManagerTests
{
    [Fact]
    public void Default_room_exists_and_comes_first()
    {
        var list = new RoomManager(1).List();

        Assert.Equal(GameConfig.DefaultRoomId, list[0].Id);
        Assert.Equal("Arena", list[0].Name);
    }

    [Fact]
    public void Created_rooms_are_sanitized_and_clamped()
    {
        var mgr = new RoomManager(1);
        var room = mgr.Create(new CreateRoomRequest("  Big Fight  ", "TEAMS", 99999, 50))!;

        Assert.Equal("Big Fight", room.Name);
        Assert.Equal("teams", room.Mode);
        Assert.Equal(GameConfig.MaxRoundSeconds, room.RoundSeconds);

        var unnamed = mgr.Create(new CreateRoomRequest("", "ffa", 1, -3))!;
        Assert.StartsWith("Raum ", unnamed.Name);
        Assert.Equal("ffa", unnamed.Mode);
        Assert.Equal(GameConfig.MinRoundSeconds, unnamed.RoundSeconds);
    }

    [Fact]
    public void Room_count_is_limited()
    {
        var mgr = new RoomManager(1);
        for (var i = 1; i < GameConfig.MaxRooms; i++)
            Assert.NotNull(mgr.Create(new CreateRoomRequest($"r{i}", "ffa", 120, 0)));

        Assert.Null(mgr.Create(new CreateRoomRequest("one too many", "ffa", 120, 0)));
    }

    [Fact]
    public void Join_moves_connection_between_rooms_and_empty_custom_rooms_vanish()
    {
        var mgr = new RoomManager(1);
        var custom = mgr.Create(new CreateRoomRequest("Custom", "ffa", 120, 0))!;

        var first = mgr.Join("c1", custom.Id, "Ann", out var left1);
        Assert.NotNull(first.Welcome);
        Assert.Null(left1);
        Assert.Equal(custom.Id, first.Welcome!.RoomId);
        Assert.Equal(1, mgr.List().Single(r => r.Id == custom.Id).Players);

        var second = mgr.Join("c1", GameConfig.DefaultRoomId, "Ann", out var left2);
        Assert.NotNull(second.Welcome);
        Assert.Equal(custom.Id, left2);
        Assert.DoesNotContain(mgr.List(), r => r.Id == custom.Id);
        Assert.Equal(1, mgr.List().Single(r => r.Id == GameConfig.DefaultRoomId).Players);
    }

    [Fact]
    public void Default_room_survives_when_empty()
    {
        var mgr = new RoomManager(1);
        mgr.Join("c1", GameConfig.DefaultRoomId, "Ann", out _);
        mgr.Leave("c1");

        Assert.Contains(mgr.List(), r => r.Id == GameConfig.DefaultRoomId && r.Players == 0);
    }

    [Fact]
    public void Unknown_and_full_rooms_are_rejected_with_a_reason()
    {
        var mgr = new RoomManager(1);
        var custom = mgr.Create(new CreateRoomRequest("Full", "ffa", 120, 0))!;

        var missing = mgr.Join("c0", "nope", "Ann", out _);
        Assert.Null(missing.Welcome);
        Assert.False(string.IsNullOrEmpty(missing.Error));

        for (var i = 0; i < GameConfig.MaxPlayers; i++)
            Assert.NotNull(mgr.Join($"c{i}", custom.Id, $"p{i}", out _).Welcome);

        var full = mgr.Join("late", custom.Id, "Late", out _);
        Assert.Null(full.Welcome);
        Assert.False(string.IsNullOrEmpty(full.Error));
    }

    [Fact]
    public void Input_and_snapshots_are_routed_per_room()
    {
        var mgr = new RoomManager(1);
        var custom = mgr.Create(new CreateRoomRequest("Two", "ffa", 120, 0))!;
        var a = mgr.Join("a", GameConfig.DefaultRoomId, "A", out _).Welcome!;
        var b = mgr.Join("b", custom.Id, "B", out _).Welcome!;

        mgr.Input("a", new InputCommand(7, 1, 0, 0, false));
        mgr.Input("nobody", new InputCommand(1, 0, 0, 0, false)); // ignored
        mgr.Tick();

        var snaps = mgr.TakeSnapshots().ToDictionary(s => s.RoomId, s => s.Snapshot);
        Assert.Equal(2, snaps.Count);
        Assert.Equal(7, snaps[GameConfig.DefaultRoomId].Players.Single(p => p.Id == a.Id).Ack);
        Assert.Equal(0, snaps[custom.Id].Players.Single(p => p.Id == b.Id).Ack);
    }

    [Fact]
    public void Rooms_without_humans_are_not_simulated_or_snapshotted()
    {
        var mgr = new RoomManager(1);
        mgr.Tick();
        Assert.Empty(mgr.TakeSnapshots());
    }
}
