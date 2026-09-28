using MidiMaze.Server.Game;

namespace MidiMaze.Tests;

public class BotDifficultyTests
{
    private static readonly MazeMap Corridor = MazeMap.FromRows(
        "#####################",
        "#...................#",
        "#####################");

    [Fact]
    public void Profiles_get_stricter_from_easy_to_hard()
    {
        var easy = BotBrain.Profile.For(BotDifficulty.Easy);
        var normal = BotBrain.Profile.For(BotDifficulty.Normal);
        var hard = BotBrain.Profile.For(BotDifficulty.Hard);

        Assert.True(easy.ReactionSeconds > normal.ReactionSeconds && normal.ReactionSeconds > hard.ReactionSeconds);
        Assert.True(easy.AimJitter > normal.AimJitter && normal.AimJitter > hard.AimJitter);
        Assert.True(easy.SightRange < normal.SightRange && normal.SightRange < hard.SightRange);
        Assert.True(easy.DodgeChance < hard.DodgeChance);
    }

    private static int TicksUntilFirstShot(BotDifficulty difficulty, int seed, double distance)
    {
        var brain = new BotBrain(seed, difficulty);
        var enemy = new[] { new BotBrain.Target(2, 1.5 + distance, 1.5) };
        for (var i = 1; i <= GameConfig.TickRate * 10; i++)
            if (brain.Think(Corridor, 1.5, 1.5, 0, enemy).Fire)
                return i;
        return int.MaxValue;
    }

    [Fact]
    public void Harder_bots_shoot_sooner()
    {
        double Average(BotDifficulty d) =>
            Enumerable.Range(0, 20).Average(seed => (double)TicksUntilFirstShot(d, seed, distance: 6));

        var easy = Average(BotDifficulty.Easy);
        var normal = Average(BotDifficulty.Normal);
        var hard = Average(BotDifficulty.Hard);

        Assert.True(easy > normal, $"easy {easy} vs normal {normal}");
        Assert.True(normal > hard, $"normal {normal} vs hard {hard}");
    }

    [Fact]
    public void Easy_bots_do_not_notice_far_away_enemies_but_hard_ones_do()
    {
        Assert.Equal(int.MaxValue, TicksUntilFirstShot(BotDifficulty.Easy, 1, distance: 12));
        Assert.NotEqual(int.MaxValue, TicksUntilFirstShot(BotDifficulty.Hard, 1, distance: 12));
    }

    [Fact]
    public void Bots_in_a_room_use_the_room_difficulty()
    {
        foreach (var difficulty in Enum.GetValues<BotDifficulty>())
        {
            var mgr = new RoomManager(1);
            var info = mgr.Create(new CreateRoomRequest("R", "ffa", 120, 2, difficulty.ToString().ToLowerInvariant()))!;
            Assert.Equal(difficulty.ToString().ToLowerInvariant(), info.Difficulty);

            mgr.Join("a", info.Id, "A", out _);
            Assert.Equal(difficulty.ToString().ToLowerInvariant(), mgr.List().Single(r => r.Id == info.Id).Difficulty);
        }
    }

    [Theory]
    [InlineData("easy", "easy")]
    [InlineData("HARD", "hard")]
    [InlineData(" Normal ", "normal")]
    [InlineData("nonsense", "normal")]
    [InlineData(null, "normal")]
    public void Difficulty_text_is_parsed_leniently(string? input, string expected)
    {
        var info = new RoomManager(1).Create(new CreateRoomRequest("R", "ffa", 120, 1, input))!;
        Assert.Equal(expected, info.Difficulty);
    }

    [Fact]
    public void Hard_bots_are_more_accurate_and_score_more_hits_than_easy_bots()
    {
        // Bots also shoot each other, so count every hit in the room, not just those on the human.
        static (int Shots, int Hits) Fight(BotDifficulty difficulty)
        {
            int shots = 0, hits = 0;
            for (var seed = 1; seed <= 8; seed++)
            {
                var room = new GameRoom(MazeMap.Generate(8, 6, seed), seed,
                    new RoomSettings("H", GameMode.FreeForAll, 600, 4, difficulty));
                room.Join("a", "A");
                var seen = new HashSet<int>();
                for (var i = 0; i < GameConfig.TickRate * 40; i++)
                {
                    room.Tick();
                    var snap = room.TakeSnapshot()!;
                    hits += snap.Events.Length;
                    foreach (var s in snap.Shots)
                        if (seen.Add(s.Id)) shots++;
                }
            }
            return (shots, hits);
        }

        var easy = Fight(BotDifficulty.Easy);
        var hard = Fight(BotDifficulty.Hard);

        Assert.True(easy.Shots > 0 && hard.Shots > 0);
        Assert.True((double)hard.Hits / hard.Shots > (double)easy.Hits / easy.Shots,
            $"accuracy hard {hard.Hits}/{hard.Shots} vs easy {easy.Hits}/{easy.Shots}");
        Assert.True(hard.Hits > easy.Hits, $"hits hard {hard.Hits} vs easy {easy.Hits}");
    }
}
