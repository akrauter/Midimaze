using MidiMaze.Server.Game;

namespace MidiMaze.Tests;

public class RoundAndTeamTests
{
    private static readonly string[] CorridorRows =
    [
        "###########",
        "#.........#",
        "###########",
    ];

    private static GameRoom TeamCorridor() => new(
        MazeMap.FromRows(CorridorRows), seed: 1,
        new RoomSettings("Teams", GameMode.Teams, 120, 0));

    private static void Fire(GameRoom room, string conn, int seq) =>
        room.EnqueueInput(conn, new InputCommand(seq, 0, 0, 0, true));

    [Fact]
    public void Team_mode_splits_players_evenly_and_colors_by_team()
    {
        var room = TeamCorridor();
        for (var i = 0; i < 6; i++)
            room.Join($"c{i}", $"p{i}");

        var roster = room.GetRoster();
        Assert.Equal(3, roster.Count(p => p.Team == 1));
        Assert.Equal(3, roster.Count(p => p.Team == 2));
        Assert.All(roster.Where(p => p.Team == 1), p => Assert.Equal(GameConfig.RedHue, p.Hue));
        Assert.All(roster.Where(p => p.Team == 2), p => Assert.Equal(GameConfig.BlueHue, p.Hue));
    }

    [Fact]
    public void Free_for_all_has_no_teams()
    {
        var room = new GameRoom(MazeMap.FromRows(CorridorRows), seed: 1);
        room.Join("a", "A");

        Assert.All(room.GetRoster(), p => Assert.Equal(0, p.Team));
    }

    [Fact]
    public void Shots_pass_through_teammates_but_hit_enemies()
    {
        var room = TeamCorridor();
        var a = room.Join("a", "A")!; // red
        var b = room.Join("b", "B")!; // blue
        var c = room.Join("c", "C")!; // red
        room.Place("a", 1.5, 1.5, 0);
        room.Place("c", 4.5, 1.5, 0);   // teammate in the line of fire
        room.Place("b", 8.5, 1.5, Math.PI);

        Fire(room, "a", 1);
        for (var i = 0; i < 40; i++) room.Tick();

        var snap = room.TakeSnapshot()!;
        Assert.Equal(GameConfig.MaxHealth, snap.Players.Single(p => p.Id == c.Id).Hp);
        Assert.Equal(GameConfig.MaxHealth - GameConfig.ShotDamage, snap.Players.Single(p => p.Id == b.Id).Hp);
        Assert.Equal(1, snap.Players.Single(p => p.Id == a.Id).Hits);
    }

    [Fact]
    public void Kills_count_for_the_team_score()
    {
        var room = TeamCorridor();
        room.Join("a", "A"); // red
        room.Join("b", "B"); // blue
        room.Place("a", 1.5, 1.5, 0);
        room.Place("b", 6.5, 1.5, Math.PI);

        var seq = 0;
        for (var shot = 0; shot < 3; shot++)
        {
            Fire(room, "a", ++seq);
            for (var i = 0; i < 30; i++) room.Tick();
        }

        var snap = room.TakeSnapshot()!;
        Assert.Equal([1, 0], snap.TeamScores);
    }

    [Fact]
    public void Round_ends_shows_results_then_starts_fresh_with_a_new_map()
    {
        var room = new GameRoom(MazeMap.FromRows(CorridorRows), seed: 5,
            new RoomSettings("R", GameMode.FreeForAll, 60, 0));
        room.Join("a", "A");
        room.Join("b", "B");
        room.Place("a", 1.5, 1.5, 0);
        room.Place("b", 6.5, 1.5, Math.PI);
        Fire(room, "a", 1);
        for (var i = 0; i < 30; i++) room.Tick();
        Assert.Equal(1, room.TakeSnapshot()!.Players.Max(p => p.Hits));

        room.ExpirePhase();
        room.Tick();
        var over = room.TakeSnapshot()!;
        Assert.Equal("over", over.Phase);
        Assert.InRange(over.Time, GameConfig.RoundOverSeconds - 0.2, GameConfig.RoundOverSeconds);
        Assert.Null(over.Map);

        // input is frozen during the results screen
        var before = over.Players[0].X;
        room.EnqueueInput("a", new InputCommand(50, 1, 0, 0, false));
        room.Tick();
        Assert.Equal(before, room.TakeSnapshot()!.Players[0].X);

        room.ExpirePhase();
        room.Tick();
        var fresh = room.TakeSnapshot()!;
        Assert.Equal("play", fresh.Phase);
        Assert.NotNull(fresh.Map);
        Assert.NotNull(fresh.Roster);
        Assert.All(fresh.Players, p =>
        {
            Assert.Equal(0, p.Hits);
            Assert.Equal(0, p.Score);
            Assert.Equal(GameConfig.MaxHealth, p.Hp);
        });

        // map/roster are only sent once
        room.Tick();
        var next = room.TakeSnapshot()!;
        Assert.Null(next.Map);
        Assert.Null(next.Roster);
    }

    [Fact]
    public void Bots_fill_the_room_and_make_way_for_humans()
    {
        var room = new GameRoom(MazeMap.Generate(8, 6, 3), seed: 1,
            new RoomSettings("Bots", GameMode.FreeForAll, 120, 4));

        room.Join("a", "A");
        Assert.Equal(4, room.BotCount);
        Assert.Equal(4, room.GetRoster().Count(p => p.Bot));

        for (var i = 0; i < GameConfig.MaxPlayers - 1; i++)
            room.Join($"h{i}", $"H{i}");

        Assert.Equal(GameConfig.MaxPlayers, room.HumanCount);
        Assert.Equal(0, room.BotCount);
        Assert.Equal(GameConfig.MaxPlayers, room.GetRoster().Length);
    }

    [Fact]
    public void Leaving_the_last_human_removes_the_bots()
    {
        var room = new GameRoom(MazeMap.Generate(8, 6, 3), seed: 1,
            new RoomSettings("Bots", GameMode.FreeForAll, 120, 3));
        room.Join("a", "A");
        Assert.Equal(3, room.BotCount);

        room.Leave("a");

        Assert.Equal(0, room.BotCount);
        Assert.Empty(room.GetRoster());
    }

    [Fact]
    public void Bots_hunt_and_eventually_hit_a_standing_human()
    {
        var room = new GameRoom(MazeMap.Generate(8, 6, 11), seed: 3,
            new RoomSettings("Hunt", GameMode.FreeForAll, 600, 2));
        var human = room.Join("a", "A")!;

        var hits = 0;
        for (var i = 0; i < GameConfig.TickRate * 90 && hits == 0; i++)
        {
            room.Tick();
            hits += room.TakeSnapshot()!.Events.Count(e => e.VictimId == human.Id);
        }

        Assert.True(hits > 0, "bots never hit a stationary player within 90 s");
    }

    [Fact]
    public void Bots_do_not_shoot_their_own_team()
    {
        var room = new GameRoom(MazeMap.Generate(8, 6, 4), seed: 2,
            new RoomSettings("T", GameMode.Teams, 600, 4));
        room.Join("a", "A");
        var roster = room.GetRoster();
        var byId = roster.ToDictionary(p => p.Id);

        for (var i = 0; i < GameConfig.TickRate * 45; i++)
        {
            room.Tick();
            foreach (var e in room.TakeSnapshot()!.Events)
                Assert.NotEqual(byId[e.ShooterId].Team, byId[e.VictimId].Team);
        }
    }
}
