using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using MidiMaze.Server.Hubs;

namespace MidiMaze.Server.Game;

/// <summary>
/// Runs all rooms at a fixed 30 Hz using real elapsed time (the OS timer is too coarse to rely on
/// one wake-up per tick), broadcasts one snapshot per room after each batch of ticks and pushes the
/// room list to everyone in the lobby whenever it changed.
/// </summary>
public sealed class GameLoopService(RoomManager rooms, IHubContext<GameHub> hub, ILogger<GameLoopService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed.TotalSeconds;
        var accumulator = 0.0;
        var nextLobbyPush = 0.0;
        string? lastLobby = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(5));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = clock.Elapsed.TotalSeconds;
                accumulator = Math.Min(accumulator + (now - previous), 0.25);
                previous = now;

                var ran = false;
                while (accumulator >= GameConfig.Dt)
                {
                    accumulator -= GameConfig.Dt;
                    try
                    {
                        rooms.Tick();
                        ran = true;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Game tick failed");
                    }
                }

                if (ran)
                {
                    foreach (var (roomId, snapshot) in rooms.TakeSnapshots())
                        await hub.Clients.Group(GameHub.RoomGroup(roomId)).SendAsync("Snapshot", snapshot, stoppingToken);
                }

                if (now >= nextLobbyPush)
                {
                    nextLobbyPush = now + 1;
                    var list = rooms.List();
                    var key = JsonSerializer.Serialize(list);
                    if (key != lastLobby)
                    {
                        lastLobby = key;
                        await hub.Clients.Group(GameHub.LobbyGroup).SendAsync("Rooms", list, stoppingToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
