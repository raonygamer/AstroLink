using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Timers;
using Astro.Core;
using Astro.Models.Database;
using Astro.Models.Game;
using Astro.Utils;
using MongoDB.Driver.Linq;
using PlayerIOClient;

namespace Astro.Managers;

#pragma warning disable CS0612 // Type or member is obsolete

public class GameManager(string gameId, string email, string password) : IDisposable
{
    public const int ClientVersion = 1389;
    public const string HyperionSolarSystemKey = "HrAjOBivt0SHPYtxKyiB_Q";
    
    public AstroLink Main => AstroLink.Instance;
    
    public Client? Client { get; private set; }
    public Connection? ServiceRoom { get; private set; }
    public string? ServiceRoomId { get; private set; }
    public Connection? GameRoom { get; private set; }
    public PlayerDataModel? PlayerData { get; private set; }

    public string GameId = gameId;
    public string Email = email;
    public string Password = password;
    
    public bool IsConnected => Client is not null;
    public bool IsOnServiceRoom => IsConnected && ServiceRoom is not null && ServiceRoomId is not null && PlayerData is not null;
    public bool IsOnGameRoom => IsOnServiceRoom && GameRoom is not null;
    
    public readonly string Session = Guid.NewGuid().ToString();

    public TaskCompletionSource<PlayerDataModel?> OnPlayerJoinedServiceTask { get; private set; } = null!;

    public async Task<Client> ConnectAsync()
    {
        Log.TraceLine("Connecting to game...");
        var clientConnectionTask = new TaskCompletionSource<Client>();

        PlayerIO.QuickConnect.SimpleConnect(GameId, Email, Password, null, client =>
        {
            clientConnectionTask.TrySetResult(client);
        }, err =>
        {
            clientConnectionTask.TrySetException(err);
        });
        
        Log.SuccessLine($"Connected to game '{GameId}'.");
        return Client = await clientConnectionTask.Task;
    }

    public async Task ConnectToServiceRoomAsync()
    {
        if (Client is null)
            throw new NullReferenceException("Couldn't connect to service room because Client was not connected.");
        
        Log.TraceLine("Listing service rooms...");
        var listedRooms = await ListServiceRoomsAsync();
        
        Log.TraceLine("Joining service room...");
        if (listedRooms.Length == 0)
        {
            await ConnectToServiceRoomAsync(GetServiceRoomId());
            return;
        }

        var room = listedRooms.FirstOrDefault(r => r.OnlineUsers < 40);
        var lastId = listedRooms.Max(r => int.Parse(r.Id.Split('_').Last()));
        if (room is null)
        {
            await ConnectToServiceRoomAsync(GetServiceRoomId(lastId + 1));
            return;
        }
        await ConnectToServiceRoomAsync(room.Id);
    }

    public async Task<RoomInfo[]> ListServiceRoomsAsync()
    {
        if (Client is null)
            throw new NullReferenceException("Couldn't list service rooms because Client was not connected.");
        
        var taskCompletionSource = new TaskCompletionSource<RoomInfo[]>();
        Client.Multiplayer.ListRooms("service", null, 1000, 0, rooms => 
        {
            taskCompletionSource.SetResult(rooms.Where(r => 
                int.TryParse(r.Id.AsSpan(8, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) && version >= ClientVersion).ToArray());
        }, err =>
        {
            taskCompletionSource.SetException(err);
        });
        
        return await taskCompletionSource.Task;
    }
    
    private async Task ConnectToServiceRoomAsync(string id)
    {
        if (Client is null)
            throw new NullReferenceException("Couldn't connect to service room because Client was null!");
        
        var taskCompletionSource = new TaskCompletionSource<Connection>();
        OnPlayerJoinedServiceTask = new TaskCompletionSource<PlayerDataModel?>();
        Client.Multiplayer.CreateJoinRoom(id, "service", true, [], new() { { "client_version", ClientVersion.ToString() } }, connection =>
        {
            Log.TraceLine(connection is { Connected: true }
                ? $"Connected to service room '{id}'."
                : $"Failed to connect to service room '{id}'.");
            
            connection.AddOnDisconnect(async void (s, msg) =>
            {
                try
                {
                    await OnServiceDisconnectAsync(s, msg);
                }
                catch (Exception ex)
                {
                    Log.ErrorLine($"Exception on service disconnect: \n{ex}");
                }
            });
            
            connection.AddOnMessage(async void (s, msg) =>
            {
                try
                {
                    await OnServiceMessageAsync(s, msg);
                }
                catch (Exception ex)
                {
                    Log.ErrorLine($"Exception on service message: \n{ex}");
                }
            });
            
            taskCompletionSource.TrySetResult(connection);
            ServiceRoomId = id;
        }, err =>
        {
            taskCompletionSource.TrySetException(err);
            OnPlayerJoinedServiceTask.TrySetResult(null);
        });
        
        ServiceRoom = await taskCompletionSource.Task;
        PlayerData = await OnPlayerJoinedServiceTask.Task;
    }

    public async Task ConnectToGameRoomAsync()
    {
        if (Client is null)
            throw new NullReferenceException("Couldn't connect to game room because Client was not connected.");
        
        if (!IsOnServiceRoom)
            throw new Exception("Couldn't connect to game room because the client is not on a service room.");
        
        
        var rooms = (await ListGameRoomsAsync()).Where(r => r.Id == GetClanGameRoomId(HyperionSolarSystemKey, PlayerData!.ClanId!));
        GameRoom = await JoinGameRoomAsync();
    }

    private async Task<Connection> JoinGameRoomAsync()
    {
        if (Client is null)
            throw new NullReferenceException("Couldn't connect to game room because Client was not connected.");
        
        if (!IsOnServiceRoom)
            throw new Exception("Couldn't connect to game room because the client is not on a service room.");
        
        var taskCompletionSource = new TaskCompletionSource<Connection>();
        var id = GetClanGameRoomId(HyperionSolarSystemKey, PlayerData!.ClanId!);
        Client.Multiplayer.CreateJoinRoom(
            id,
            "game",
            false,
            new()
            {
                { "solarSystemKey", HyperionSolarSystemKey },
                { "service", ServiceRoomId },
                { "pvpAboveCap", "false" },
                { "systemType", "clan" }
            },
            new()
            {
                { "client_version", ClientVersion.ToString() },
                { "session", Session },
                { "warpJump", "false" },
                { "level", PlayerData.Level > 0 ? PlayerData.Level.ToString() : "1" }
            }, connection =>
            {
                Log.TraceLine(connection is { Connected: true }
                    ? $"Connected to game room '{id}'."
                    : $"Failed to connect to game room '{id}'.");
            
                connection.AddOnDisconnect(async void (s, msg) =>
                {
                    try
                    {
                        await OnGameDisconnectAsync(s, msg);
                    }
                    catch (Exception ex)
                    {
                        Log.ErrorLine($"Exception on game disconnect: \n{ex}");
                    }
                });
            
                connection.AddOnMessage(async void (s, msg) =>
                {
                    try
                    {
                        await OnGameMessageAsync(s, msg);
                    }
                    catch (Exception ex)
                    {
                        Log.ErrorLine($"Exception on game message: \n{ex}");
                    }
                });
                
                taskCompletionSource.TrySetResult(connection);
            }, err =>
            {
                taskCompletionSource.TrySetException(err);
            });
        return await taskCompletionSource.Task;
    }

    public async Task<RoomInfo[]> ListGameRoomsAsync()
    {
        if (Client is null)
            throw new NullReferenceException("Couldn't list game rooms because Client was not connected.");

        if (!IsOnServiceRoom)
            throw new Exception("Couldn't list game rooms because the client is not on a service room.");
        
        var taskCompletionSource = new TaskCompletionSource<RoomInfo[]>();
        Client.Multiplayer.ListRooms("game", new()
        {
            { "solarSystemKey", HyperionSolarSystemKey },
            { "service", ServiceRoomId }
        }, 1000, 0, rooms =>
        {
            const int maxUsers = 15;
            taskCompletionSource.TrySetResult(rooms.Where(room =>
            {
                if (!int.TryParse(room.RoomData["version"], out var version) || version < ClientVersion)
                    return false;
                if (room.OnlineUsers >= maxUsers)
                    return false;
                if (room.RoomData.TryGetValue("modLocked", out var modLocked) && modLocked == "true")
                    return false;
                if (room.RoomData.TryGetValue("modClosed", out var modClosed) && modClosed == "true")
                    return false;
                return true;
            }).ToArray());
        }, err =>
        {
            taskCompletionSource.TrySetException(err);
        });
        
        return await taskCompletionSource.Task;
    }

    public static string GetServiceRoomId(int index = 0)
    {
        return $"Service-{ClientVersion}_{index}";
    }

    public static string GetClanGameRoomId(string solarSystemId, string clanId)
    {
        return BitConverter.ToString(MD5.HashData(Encoding.UTF8.GetBytes(solarSystemId + clanId))).Replace("-", string.Empty).ToLowerInvariant();
    }
    
    private async Task OnServiceMessageAsync(object sender, Message message)
    {
        switch (message.Type)
        {
            case "joined":
                await OnPlayerJoinedServiceRoomAsync(new PlayerDataModel()
                {
                    Level = message.GetInt(1),
                    ClanId = message.GetString(18)
                });
                break;
            default:
                break;
        }
    }
    
    private async Task OnGameMessageAsync(object sender, Message message)
    {
        switch (message.Type)
        {
            case "error":
            case "softDisconnect":
            case "disconnect":
                await OnGameDisconnectAsync(sender, "Disconnected from the game!");
                break;
            default:
                break;
        }
    }

    public async Task OnPlayerJoinedServiceRoomAsync(PlayerDataModel player)
    {
        OnPlayerJoinedServiceTask.TrySetResult(player);
    }
    
    private async Task OnServiceDisconnectAsync(object sender, string reason)
    {
        Log.ErrorLine($"Disconnected from the service room!");
        await Task.Delay(TimeSpan.FromSeconds(5));
        GameRoom?.Disconnect();
        GameRoom = null;
        ServiceRoom?.Disconnect();
        ServiceRoom = null;
        ServiceRoomId = null;
        PlayerData = null;
        try
        {
            await ConnectToServiceRoomAsync();
        }
        catch (Exception ex)
        {
            Log.ErrorLine($"Failed to reconnect to the service room!");
        }
    }
    
    private async Task OnGameDisconnectAsync(object sender, string reason)
    {
        Log.ErrorLine($"Disconnected from the game room!");
        await Task.Delay(TimeSpan.FromSeconds(5));
        GameRoom?.Disconnect();
        GameRoom = null;
        try
        {
            await ConnectToGameRoomAsync();
        }
        catch (Exception ex)
        {
            Log.ErrorLine($"Failed to reconnect to the game room!");
        }
    }

    public async Task<double?> GetServerTimeAsync()
    {
        if (!IsOnServiceRoom)
            return null;

        var taskCompletionSource = new TaskCompletionSource<double>();
        void RpcTime(object sender, Message m)
        {
            if (m.Type != "serverTime")
                return;
            taskCompletionSource.TrySetResult(m.GetDouble(0));
            if (GameRoom is not null)
                GameRoom.OnMessage -= RpcTime;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(10000);
            taskCompletionSource.TrySetCanceled();
        });
        
        GameRoom!.OnMessage += RpcTime;
        GameRoom!.Send("timeRequest", Client!.ConnectUserId);
        try
        {
            return await taskCompletionSource.Task;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        ServiceRoom?.Disconnect();
        Client?.Logout();
    }
}