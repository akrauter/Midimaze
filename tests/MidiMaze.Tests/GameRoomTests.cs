using MidiMaze.Server.Game;

namespace MidiMaze.Tests;

public class GameRoomTests
{
    // 9x3 corridor, two players can face each other along it
    private static GameRoom Corridor() => new(MazeMap.FromRows(
        "###########",
        "#.........#",
        "###########"), seed: 1);

    private static int Cmd(GameRoom room, string conn, ref int seq, bool fire = false, int forward = 0, int turn = 0)
    {
        room.EnqueueInput(conn, new InputCommand(++seq, forward, 0, turn, fire));
        return seq;
    }

    private static PlayerState State(GameRoom room, int id) =>
        room.TakeSnapshot()!.Players.Single(p => p.Id == id);

    [Fact]
    public void Join_assigns_ids_and_cleans_names()
    {
        var room = Corridor();
        var a = room.Join("a", "  Alice\u0007  ")!;
        var b = room.Join("b", "")!;

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal("Alice", a.Roster.Single(p => p.Id == a.Id).Name);
        Assert.Equal($"Smiley{b.Id}", b.Roster.Single(p => p.Id == b.Id).Name);
        Assert.Null(room.Join("a", "again")); // same connection twice
    }

    [Fact]
    public void Arena_is_limited_to_sixteen_players()
    {
        var room = Corridor();
        for (var i = 0; i < GameConfig.MaxPlayers; i++)
            Assert.NotNull(room.Join($"c{i}", $"p{i}"));

        Assert.Null(room.Join("overflow", "late"));
    }

    [Fact]
    public void Leave_removes_player_and_reports_it()
    {
        var room = Corridor();
        room.Join("a", "A");

        Assert.True(room.Leave("a"));
        Assert.False(room.Leave("a"));
        Assert.Equal(0, room.HumanCount);
    }

    [Fact]
    public void Input_moves_player_and_is_acknowledged()
    {
        var room = Corridor();
        var a = room.Join("a", "A")!;
        room.Place("a", 2.5, 1.5, 0);

        var seq = 0;
        Cmd(room, "a", ref seq, forward: 1);
        room.Tick();

        var s = State(room, a.Id);
        Assert.Equal(1, s.Ack);
        Assert.Equal(2.5 + GameConfig.MoveSpeed * GameConfig.Dt, s.X, 3);
    }

    [Fact]
    public void Input_flood_is_limited_to_about_one_command_per_tick()
    {
        var room = Corridor();
        var a = room.Join("a", "A")!;
        room.Place("a", 1.5, 1.5, 0);

        var seq = 0;
        for (var i = 0; i < 100; i++)
            Cmd(room, "a", ref seq, forward: 1);
        for (var i = 0; i < 10; i++)
            room.Tick();

        var s = State(room, a.Id);
        var maxTravel = 10 * GameConfig.InputBudgetPerTick + GameConfig.InputBudgetMax; // commands
        Assert.True(s.X - 1.5 <= maxTravel * GameConfig.MoveSpeed * GameConfig.Dt + 1e-6);
    }

    [Fact]
    public void Shot_hits_damages_and_scores()
    {
        var room = Corridor();
        var shooter = room.Join("a", "A")!;
        var victim = room.Join("b", "B")!;
        room.Place("a", 1.5, 1.5, 0);
        room.Place("b", 6.5, 1.5, Math.PI);

        var seq = 0;
        Cmd(room, "a", ref seq, fire: true);
        for (var i = 0; i < 30; i++) room.Tick();

        var snap = room.TakeSnapshot()!;
        var v = snap.Players.Single(p => p.Id == victim.Id);
        var s = snap.Players.Single(p => p.Id == shooter.Id);

        Assert.Equal(GameConfig.MaxHealth - GameConfig.ShotDamage, v.Hp);
        Assert.Equal(1, s.Hits);
        Assert.Equal(0, s.Kills);
        Assert.Empty(snap.Shots);
    }

    [Fact]
    public void Third_hit_kills_and_victim_respawns_later()
    {
        var room = Corridor();
        var shooter = room.Join("a", "A")!;
        var victim = room.Join("b", "B")!;
        room.Place("a", 1.5, 1.5, 0);
        room.Place("b", 6.5, 1.5, Math.PI);

        var seq = 0;
        HitEvent[] events = [];
        for (var shot = 0; shot < 3; shot++)
        {
            Cmd(room, "a", ref seq, fire: true);
            for (var i = 0; i < 30; i++) // > cooldown, shot has time to land
            {
                room.Tick();
                var snap = room.TakeSnapshot()!;
                events = events.Concat(snap.Events).ToArray();
            }
        }

        Assert.Equal(3, events.Length);
        Assert.True(events[^1].Killed);
        Assert.Equal(shooter.Id, events[^1].ShooterId);
        Assert.Equal(victim.Id, events[^1].VictimId);

        var s = room.TakeSnapshot()!;
        Assert.Equal(1, s.Players.Single(p => p.Id == shooter.Id).Kills);
        Assert.Equal(1, s.Players.Single(p => p.Id == victim.Id).Deaths);

        // dead player counts down and comes back with full health
        Assert.True(s.Players.Single(p => p.Id == victim.Id).Hp <= 0);
        for (var i = 0; i < (int)(GameConfig.RespawnDelay * GameConfig.TickRate) + 2; i++)
            room.Tick();
        Assert.Equal(GameConfig.MaxHealth, State(room, victim.Id).Hp);
    }

    [Fact]
    public void Walls_stop_shots()
    {
        var room = new GameRoom(MazeMap.FromRows(
            "#########",
            "#...#...#",
            "#########"), seed: 1);
        room.Join("a", "A");
        var victim = room.Join("b", "B")!;
        room.Place("a", 1.5, 1.5, 0);
        room.Place("b", 6.5, 1.5, Math.PI);

        var seq = 0;
        Cmd(room, "a", ref seq, fire: true);
        for (var i = 0; i < 30; i++) room.Tick();

        Assert.Equal(GameConfig.MaxHealth, State(room, victim.Id).Hp);
    }

    [Fact]
    public void Shooter_cannot_hit_self_and_cooldown_limits_fire_rate()
    {
        var room = Corridor();
        var a = room.Join("a", "A")!;
        room.Place("a", 1.5, 1.5, 0);

        var seq = 0;
        for (var i = 0; i < 4; i++)
            Cmd(room, "a", ref seq, fire: true);
        room.Tick();
        room.Tick();
        room.Tick();
        room.Tick();

        var snap = room.TakeSnapshot()!;
        Assert.Single(snap.Shots); // 4 fire commands within 4 ticks, cooldown allows only one
        Assert.Equal(GameConfig.MaxHealth, snap.Players.Single(p => p.Id == a.Id).Hp);
    }

    [Fact]
    public void Players_spawn_facing_open_space_not_a_wall()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var map = MazeMap.Generate(8, 6, seed);
            var room = new GameRoom(map, seed);
            var p = room.Join("a", "A")!;
            var s = room.TakeSnapshot()!.Players.Single(x => x.Id == p.Id);

            var ahead = (X: (int)Math.Floor(s.X + Math.Round(Math.Cos(s.A))),
                         Y: (int)Math.Floor(s.Y + Math.Round(Math.Sin(s.A))));
            Assert.False(map.IsWall(ahead.X, ahead.Y), $"seed {seed}: spawned facing a wall");
        }
    }

    [Fact]
    public void Empty_room_returns_no_snapshot()
    {
        Assert.Null(Corridor().TakeSnapshot());
    }
}
