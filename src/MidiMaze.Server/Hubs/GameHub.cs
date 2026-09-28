using Microsoft.AspNetCore.SignalR;
using MidiMaze.Server.Game;

namespace MidiMaze.Server.Hubs;

public sealed class GameHub(RoomManager rooms) : Hub
{
    public const string LobbyGroup = "lobby";

    public static string RoomGroup(string roomId) => "room:" + roomId;

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, LobbyGroup);
        await base.OnConnectedAsync();
    }

    public RoomInfo[] GetRooms() => rooms.List();

    /// <summary>Creates a room; the caller still has to join it.</summary>
    public RoomInfo? CreateRoom(CreateRoomRequest request) => rooms.Create(request);

    /// <summary>Enter a room (leaving the current one, if any).</summary>
    public async Task<JoinResult> Join(string roomId, string? name)
    {
        var result = rooms.Join(Context.ConnectionId, roomId, name, out var leftRoomId);

        if (leftRoomId is not null)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroup(leftRoomId));

        if (result.Welcome is null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LobbyGroup);
            return result;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, LobbyGroup);
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId));
        return result;
    }

    /// <summary>Back to the lobby.</summary>
    public async Task Leave()
    {
        var left = rooms.Leave(Context.ConnectionId);
        if (left is not null)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroup(left));
        await Groups.AddToGroupAsync(Context.ConnectionId, LobbyGroup);
    }

    public void Input(InputCommand cmd) => rooms.Input(Context.ConnectionId, cmd);

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        rooms.Leave(Context.ConnectionId); // SignalR drops the group memberships itself
        return base.OnDisconnectedAsync(exception);
    }
}
