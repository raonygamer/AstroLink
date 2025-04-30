using System.Globalization;
using Astro.Core;
using Astro.Models.Database;
using Astro.Utils;
using PlayerIOClient;

namespace Astro.Managers;

public class GameManager : IDisposable
{
    public const int ClientVersion = 1389;
    
    public readonly AstroLink Main;
    public readonly Client Client;
    public Connection? ServiceConnection { get; private set; }

    private readonly Dictionary<string, DatabaseObject> CachedPlayerObjects = [];
    
    private GameManager(AstroLink main, Client client)
    {
        Main = main;
        Client = client;
    }

    public static async Task<GameManager> CreateAsync(AstroLink main, string gameId, string email, string password)
    {
        Log.TraceLine("Creating game manager...");
        var clientConnectionTask = new TaskCompletionSource<Client>();
#pragma warning disable CS0612 // Type or member is obsolete
        PlayerIO.QuickConnect.SimpleConnect(gameId, email, password, null, client =>
        {
            clientConnectionTask.TrySetResult(client);
        }, err =>
        {
            clientConnectionTask.TrySetException(err);
        });
#pragma warning restore CS0612 // Type or member is obsolete
        var client = await clientConnectionTask.Task;
        var manager = new GameManager(main, client);
        Log.SuccessLine($"Connected to game '{gameId}' with email '{email}'.");
        Log.SuccessLine("Created game manager successfully.");
        manager.ServiceConnection = await manager.ListAndConnectToServiceRoomAsync();
        return manager;
    }

    public async Task<Connection> ListAndConnectToServiceRoomAsync()
    {
        Log.TraceLine("Listing service rooms...");
        var listedRooms = await ListServiceRoomsAsync();
        
        Log.TraceLine("Joining service room...");
        if (listedRooms.Length == 0)
        {
            return await ConnectToServiceRoomAsync(GetServiceRoomId());
        }

        var room = listedRooms.FirstOrDefault(r => r.OnlineUsers < 40);
        var lastId = listedRooms.Max(r => int.Parse(r.Id.Split('_').Last()));
        if (room == null)
        {
            return await ConnectToServiceRoomAsync(GetServiceRoomId(lastId + 1));
        }
        return await ConnectToServiceRoomAsync(room.Id);
    }

    public async Task<RoomInfo[]> ListServiceRoomsAsync()
    {
        var taskCompletionSource = new TaskCompletionSource<RoomInfo[]>();
        Client.Multiplayer.ListRooms("service", null, 1000, 0, 
            rooms => 
            {
                taskCompletionSource.SetResult(rooms.Where(r => 
                    int.TryParse(r.Id.AsSpan(8, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) && version >= ClientVersion).ToArray());
            }, 
            err =>
            {
                taskCompletionSource.SetException(err);
            });
        
        return await taskCompletionSource.Task;
    }
    
    private async Task<Connection> ConnectToServiceRoomAsync(string id)
    {
        var taskCompletionSource = new TaskCompletionSource<Connection>();
        Client.Multiplayer.CreateJoinRoom(id, "service", true, [], new() { { "client_version", ClientVersion.ToString() } },
            connection =>
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
                
                taskCompletionSource.SetResult(connection);
            }, 
            err =>
            {
                taskCompletionSource.SetException(err);
            });
        
        var connection = await taskCompletionSource.Task;
        return connection;
    }

    public static string GetServiceRoomId(int index = 0)
    {
        return $"Service-{ClientVersion}_{index}";
    }
    
    private async Task OnServiceMessageAsync(object sender, Message message)
    {
        switch (message.Type)
        {
            case "chatMsg":
                var messageType = message.GetString(0);
                var messageContent = message.GetString(1);
                var senderId = message.GetString(2);
                var senderName = message.GetString(3);
                var roles = message.GetString(4);
                var supporter = message.GetBoolean(5);
                await OnReceiveChatMessage(messageType, messageContent, senderId, senderName, roles, supporter);
                break;
            default:
                break;
        }
    }

    private async Task OnReceiveChatMessage(string messageType, string messageContent, string senderId, string senderName, string roles, bool supporter)
    {
        if (messageType != "private" || senderId == Client.ConnectUserId)
            return;
                
        var database = Main.DatabaseManager;
        var registry = Main.LinkingRegistry;
        var discord = Main.DiscordManager;
                
        if (await registry.AcceptLinkingRequest(messageContent, senderId) is not {} linkingRequest)
        {
            SendPrivateMessage(senderName, $"<font color='#ff3849'>No linking request found with token </font>'{messageContent}'");
            return;
        }
        
        var discordUsername = discord.Client.GetUser(linkingRequest.DiscordUserId).Username;
        SendPrivateMessage(senderName,
            $"<font color='#38ff5d'>Linked your game account to discord </font>'{discordUsername}'");
    }
    
    public void SendPrivateMessage(string name, string content)
    {
        ServiceConnection?.Send("chatMsg", "private", name, content);
    }

    public async Task<IEnumerable<DatabaseObject>> GetPlayerObjectsAsync(string[] playerIds)
    {
        var isCached = playerIds.All(playerId => CachedPlayerObjects.ContainsKey(playerId));
        if (isCached)
        {
            return CachedPlayerObjects.Where(kv => playerIds.Contains(kv.Key)).Select(kv => kv.Value);
        }

        var taskCompletionSource = new TaskCompletionSource<DatabaseObject[]>();
        Client.BigDB.LoadKeys("PlayerObjects", playerIds, 
            playerObjects => taskCompletionSource.SetResult(playerObjects), 
            error => taskCompletionSource.SetException(error));
        var playerObjects = await taskCompletionSource.Task;
        foreach (var pObj in playerObjects)
        {
            CachedPlayerObjects[pObj.Key] = pObj;
        }
        return playerObjects;
    }
    
    public IEnumerable<DatabaseObject> GetPlayerObjects(string[] playerIds)
    {
        var isCached = playerIds.All(playerId => CachedPlayerObjects.ContainsKey(playerId));
        if (isCached)
        {
            return CachedPlayerObjects.Where(kv => playerIds.Contains(kv.Key)).Select(kv => kv.Value);
        }

        var taskCompletionSource = new TaskCompletionSource<DatabaseObject[]>();
        Client.BigDB.LoadKeys("PlayerObjects", playerIds, 
            playerObjects => taskCompletionSource.SetResult(playerObjects), 
            error => taskCompletionSource.SetException(error));
        var playerObjects = taskCompletionSource.Task.Result;
        foreach (var pObj in playerObjects)
        {
            CachedPlayerObjects[pObj.Key] = pObj;
        }
        return playerObjects;
    }

    public async Task<DatabaseObject> GetPlayerObjectAsync(string playerId)
    {
        if (CachedPlayerObjects.TryGetValue(playerId, out var playerObject))
            return playerObject;
        
        var taskCompletionSource = new TaskCompletionSource<DatabaseObject>();
        Client.BigDB.Load("PlayerObjects", playerId, 
            pObject => taskCompletionSource.SetResult(pObject), 
            error => taskCompletionSource.SetException(error));
        playerObject = await taskCompletionSource.Task;
        return playerObject;
    }
    
    private async Task OnServiceDisconnectAsync(object sender, string reason)
    {
        Log.ErrorLine($"Disconnected from the service room: \n{reason}.");
        await Task.Delay(TimeSpan.FromSeconds(5));
        ServiceConnection?.Disconnect();
        ServiceConnection = null;
        await ListAndConnectToServiceRoomAsync();
    }

    public void Dispose()
    {
        ServiceConnection?.Disconnect();
        Client.Logout();
    }
}