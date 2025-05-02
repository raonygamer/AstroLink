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
    public const int NormalReconnectionCooldown = 10 * 1000;
    public const int BiggerReconnectionCooldown = 30 * 1000;
    public const int MaxServiceReconnections = 5;
    public const int MaxGameReconnections = 5;
    
    public AstroLink Main => AstroLink.Instance;
    
    public Client? Client { get; private set; }
    public Connection? ServiceRoom { get; private set; }
    public string? ServiceRoomId { get; private set; }
    public Connection? GameRoom { get; private set; }
    public string? GameRoomId { get; private set; }
    public PlayerDataModel? PlayerData { get; private set; }

    public readonly string GameId = gameId;
    public readonly string Email = email;
    public readonly string Password = password;
    
    public bool IsConnected => Client is not null;
    public bool IsOnServiceRoom => IsConnected && ServiceRoom is not null && ServiceRoomId is not null && PlayerData is not null;
    public bool IsOnGameRoom => IsOnServiceRoom && GameRoom is not null;
    
    public bool IsConnectingToServiceRoom { get; private set; } = false;
    public bool IsConnectingToGameRoom { get; private set; } = false;
    
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

    public async Task<bool> TryConnectAsync(int maxTries = -1, Action? eachAttempt = null)
    {
        var tries = 0;
        while (true)
        {
            if (maxTries != -1 && tries++ >= maxTries)
                break;
            try
            {
                if (!IsConnected)
                    await ConnectAsync();
                break;
                eachAttempt?.Invoke();
            }
            catch (Exception ex)
            {
                var parts = ex.Message.Split('.');
                Log.ErrorLine($"Failed to authenticate to the server: {parts.FirstOrDefault("You broke it.")}");
            }
            await Task.Delay(NormalReconnectionCooldown);
        }
        return IsConnected;
    }

    public async Task<bool> TryConnectToServiceRoomAsync(int maxTries = -1, Action? eachAttempt = null)
    {
        var tries = 0;
        var internalTries = 0;
        while (true)
        {
            if (maxTries != -1 && internalTries++ >= maxTries)
                break;
            tries++;
            try
            {
                if (!IsOnServiceRoom)
                    await ConnectToServiceRoomAsync();
                break;
                eachAttempt?.Invoke();
            }
            catch (Exception ex)
            {
                var parts = ex.Message.Split('.');
                Log.ErrorLine($"Failed to connect to the service room: {parts.FirstOrDefault("You broke it.")}");
            }

            if (tries >= MaxServiceReconnections)
            {
                tries = 0;
                await Task.Delay(BiggerReconnectionCooldown);
            }
            else
            {
                await Task.Delay(NormalReconnectionCooldown);
            }
        }

        return IsOnServiceRoom;
    }
    
    public async Task<bool> TryConnectToGameRoomAsync(int maxTries = -1, Action? eachAttempt = null)
    {
        var tries = 0;
        var internalTries = 0;
        while (true)
        {
            if (maxTries != -1 && internalTries++ >= maxTries)
                break;
            tries++;
            try
            {
                if (!IsOnGameRoom)
                    await ConnectToGameRoomAsync();
                break;
                eachAttempt?.Invoke();
            }
            catch (Exception ex)
            {
                var parts = ex.Message.Split('.');
                Log.ErrorLine($"Failed to connect to the game room: {parts.FirstOrDefault("You broke it.")}");
            }

            if (tries >= MaxGameReconnections)
            {
                tries = 0;
                await Task.Delay(BiggerReconnectionCooldown);
            }
            else
            {
                await Task.Delay(NormalReconnectionCooldown);
            }
        }

        return IsOnGameRoom;
    }
    
    public async Task ConnectToServiceRoomAsync()
    {
        if (Client is null)
            throw new NullReferenceException("Couldn't connect to service room because Client was not connected.");
        
        if (IsConnectingToServiceRoom)
            throw new Exception("Already connecting to a service room.");
        
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
        
        if (IsConnectingToServiceRoom)
            throw new Exception("Already connecting to a service room.");
        IsConnectingToServiceRoom = true;

        if (ServiceRoom is not null && ServiceRoom.Connected)
        {
            DisposeServiceRoom();
        }
        
        var taskCompletionSource = new TaskCompletionSource<Connection>();
        OnPlayerJoinedServiceTask = new TaskCompletionSource<PlayerDataModel?>();
        Client.Multiplayer.CreateJoinRoom(id, "service", true, [], new() { { "client_version", ClientVersion.ToString() } }, connection =>
            {
                Log.TraceLine(connection is { Connected: true }
                    ? $"Connected to service room '{id}'."
                    : $"Failed to connect to service room '{id}'.");

                connection.OnDisconnect += OnServiceDisconnect;
                connection.OnMessage += OnServiceMessage;
                taskCompletionSource.TrySetResult(connection);
                ServiceRoomId = id;
            }, err =>
            {
                taskCompletionSource.TrySetException(err);
                OnPlayerJoinedServiceTask.TrySetResult(null);
            });

        try
        {
            ServiceRoom = await taskCompletionSource.Task;
            PlayerData = await OnPlayerJoinedServiceTask.Task;
        }
        finally
        {
            IsConnectingToServiceRoom = false;
        }
    }

    public async Task ConnectToGameRoomAsync()
    {
        if (Client is null)
            throw new NullReferenceException("Couldn't connect to game room because Client was not connected.");
        
        if (!IsOnServiceRoom)
            throw new Exception("Couldn't connect to game room because the client is not on a service room.");
        
        if (IsConnectingToGameRoom)
            throw new Exception("Already connecting to a game room.");
        
        await JoinGameRoomAsync();
    }

    private async Task JoinGameRoomAsync()
    {
        if (Client is null)
            throw new NullReferenceException("Couldn't connect to game room because Client was not connected.");
        
        if (!IsOnServiceRoom)
            throw new Exception("Couldn't connect to game room because the client is not on a service room.");

        if (IsConnectingToGameRoom)
            throw new Exception("Already connecting to a game room.");
        IsConnectingToGameRoom = true;

        if (GameRoom is not null && GameRoom.Connected)
        {
            DisposeGameRoom();
        }
        
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
            
                connection.OnDisconnect += OnGameDisconnect;
                connection.OnMessage += OnGameMessage;
                taskCompletionSource.TrySetResult(connection);
                GameRoomId = id;
            }, err =>
            {
                taskCompletionSource.TrySetException(err);
            });
        
        try
        {
            GameRoom = await taskCompletionSource.Task;
        }
        finally
        {
            IsConnectingToGameRoom = false;
        }
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
                break;
            default:
                break;
        }
    }

    private async Task OnPlayerJoinedServiceRoomAsync(PlayerDataModel player)
    {
        OnPlayerJoinedServiceTask.TrySetResult(player);
    }

    private void OnServiceDisconnect(object sender, string reason)
    {
        try
        {
            _ = OnServiceDisconnectAsync(sender, reason);
        }
        catch (Exception ex)
        {
            var parts = ex.Message.Split('.');
            Log.ErrorLine($"Exception on service disconnect: {parts.FirstOrDefault("You broke it.")}");
        }
    }
    
    private void OnServiceMessage(object sender, Message message)
    {
        try
        {
            _ = OnServiceMessageAsync(sender, message);
        }
        catch (Exception ex)
        {
            var parts = ex.Message.Split('.');
            Log.ErrorLine($"Exception on service message: {parts.FirstOrDefault("You broke it.")}");
        }
    }
    
    private void OnGameDisconnect(object sender, string reason)
    {
        try
        {
            _ = OnGameDisconnectAsync(sender, reason);
        }
        catch (Exception ex)
        {
            var parts = ex.Message.Split('.');
            Log.ErrorLine($"Exception on game disconnect: {parts.FirstOrDefault("You broke it.")}");
        }
    }
    
    private void OnGameMessage(object sender, Message message)
    {
        try
        {
            _ = OnGameMessageAsync(sender, message);
        }
        catch (Exception ex)
        {
            var parts = ex.Message.Split('.');
            Log.ErrorLine($"Exception on game message: {parts.FirstOrDefault("You broke it.")}");
        }
    }

    private void DisposeServiceRoom()
    {
        if (ServiceRoom is not null)
        {
            ServiceRoom.OnMessage -= OnServiceMessage;
            ServiceRoom.OnDisconnect -= OnServiceDisconnect;
        }
        ServiceRoom = null;
        ServiceRoomId = null;
        PlayerData = null;
    }
    
    private void DisposeGameRoom()
    {
        if (GameRoom is not null)
        {
            GameRoom.OnMessage -= OnGameMessage;
            GameRoom.OnDisconnect -= OnGameDisconnect;
        }
        GameRoom = null;
        GameRoomId = null;
    }
    
    private async Task OnServiceDisconnectAsync(object sender, string reason)
    {
        Log.ErrorLine($"Disconnected from the service room!");
        var tries = 0;
        DisposeGameRoom();
        DisposeServiceRoom();
        while (!IsOnServiceRoom)
        {
            try
            {
                await ConnectToServiceRoomAsync();
                break;
            }
            catch (Exception ex)
            {
                var parts = ex.Message.Split('.');
                Log.ErrorLine($"Failed to reconnect to the service room: {parts.FirstOrDefault("You broke it.")}");
            }
            
            tries++;
            if (tries >= MaxServiceReconnections)
            {
                tries = 0;
                await Task.Delay(BiggerReconnectionCooldown);
            }
            else
            {
                await Task.Delay(NormalReconnectionCooldown);
            }
        }
    }
    
    private async Task OnGameDisconnectAsync(object sender, string reason)
    {
        Log.ErrorLine($"Disconnected from the game room!");
        var tries = 0;
        DisposeGameRoom();
        while (!IsOnGameRoom)
        {
            try
            {
                await ConnectToGameRoomAsync();
                break;
            }
            catch (Exception ex)
            {
                var parts = ex.Message.Split('.');
                Log.ErrorLine($"Failed to reconnect to the game room: {parts.FirstOrDefault("You broke it.")}");
            }
            
            tries++;
            if (tries >= MaxGameReconnections)
            {
                tries = 0;
                await Task.Delay(BiggerReconnectionCooldown);
            }
            else
            {
                await Task.Delay(NormalReconnectionCooldown);
            }
        }
    }

    public async Task<double> GetServerTimeAsync()
    {
        if (!IsOnServiceRoom)
            throw new Exception("Couldn't get server time because the client was not on a service room.");

        if (!IsOnGameRoom)
            throw new Exception("Couldn't get server time because the client was not on a game room.");

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
            throw new TimeoutException("Failed to get server time because it resulted in a timeout.");
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        ServiceRoom?.Disconnect();
        Client?.Logout();
    }
}