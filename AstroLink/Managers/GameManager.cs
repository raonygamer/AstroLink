using System.Globalization;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Timers;
using Astro.Core;
using Astro.Models.Database;
using Astro.Models.Game;
using Astro.Utils;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Linq;
using PlayerIOClient;

namespace Astro.Managers;

#pragma warning disable CS0612 // Type or member is obsolete

public class GameManager(string gameId, string email, string password) : IDisposable
{
    public AstroLink Main => AstroLink.Instance;
    
    public const int ClientVersion = 1389;
    public const string HyperionSolarSystemKey = "HrAjOBivt0SHPYtxKyiB_Q";
    public const int ServerConnectionDelay = 10 * 1000;
    public const int ServerConnectionCooldown = 30 * 1000;
    public const int MaxServerAuthentications = 3;
    public const int MaxServiceReconnections = 3;
    public const int MaxGameReconnections = 3;
    
    #region Authentication
    public readonly string Session = Guid.NewGuid().ToString();
    public readonly string GameId = gameId;
    public readonly string Email = email;
    public readonly string Password = password;
    public Client? Client { get; private set; }
    public bool IsAuthenticated => Client is not null;
    public AuthenticationException? CheckAuthenticated(string message = "Not authenticated with server.")
    {
        if (!IsAuthenticated || Client is not { } client)
        {
            return new AuthenticationException(message);
        }
        return null;
    }
    public async Task<Client> AuthenticateAsync()
    {
        if (IsAuthenticated)
            return Client!;
        var clientConnectionTask = new TaskCompletionSource<Client>();
        PlayerIO.QuickConnect.SimpleConnect(GameId, Email, Password, null, client =>
        {
            clientConnectionTask.TrySetResult(client);
        }, err =>
        {
            clientConnectionTask.TrySetException(err);
        });
        return Client = await clientConnectionTask.Task;
    }

    public async Task<Client?> TryAuthenticateAsync(int maxTries = -1)
    {
        var tries = 0;
        var triesForInterval = 0;
        Client? client = null;
        do
        {
            tries++;
            triesForInterval++;
            try
            {
                Log.TraceLine("Trying to authenticate to the server...");
                client = await AuthenticateAsync();
                break;
            }
            catch (Exception e)
            {
                Log.ErrorLine($"Failed to authenticate to the server: {e.Message}");
                if (maxTries == -1 || tries < maxTries)
                    Log.TraceLine($"Retrying in {Math.Floor((double)(triesForInterval >= MaxServerAuthentications ? ServerConnectionCooldown : ServerConnectionDelay) / 1000)}s...");
                // Ignored
            }

            if (triesForInterval >= MaxServerAuthentications)
            {
                await Task.Delay(ServerConnectionCooldown);
                triesForInterval = 0;
            }
            else
            {
                await Task.Delay(ServerConnectionDelay);
            }
        } while (maxTries == -1 || tries < maxTries);

        if (client is not null)
        {
            Log.SuccessLine($"Authenticated successfully to the server.");
        }
        return client;
    }
    #endregion
    #region Service Room
    public async Task<IEnumerable<RoomInfo>> GetServiceRoomsAsync()
    {
        if (CheckAuthenticated("Could not get service rooms because client was not authenticated.") is { } ex)
            throw ex;
        var client = Client!;
        
        var taskCompletionSource = new TaskCompletionSource<IEnumerable<RoomInfo>>();
        client.Multiplayer.ListRooms(
            "service",
            [],
            1000,
            0,
            rooms =>
            {
                taskCompletionSource.TrySetResult(rooms);
            },
            error =>
            {
                taskCompletionSource.TrySetException(error);
            }
        );
        return await taskCompletionSource.Task;
    }

    public string GetServiceRoomId(int index) => $"Service-{ClientVersion}_{index}";
    
    public async Task<Connection> JoinSuitableServiceRoomAsync()
    {
        if (CheckAuthenticated("Could not join suitable service room because client was not authenticated.") is { } ex)
            throw ex;
        var client = Client!;
        
        var rooms = (await GetServiceRoomsAsync())
            .Where(room =>
            {
                if (room.RoomType != "service")
                    return false;
                var version = int.Parse(room.Id.Substring(8, 4));
                return version >= ClientVersion;
            })
            .Select(room => (Index: int.Parse(room.Id.Substring(13, 1)), Room: room))
            .ToDictionary(o => o.Index, o => o.Room);

        Connection? connection = null;
        Exception? roomJoinException = null;
        if (rooms.Count == 0)
        {
            try
            {
                connection = await JoinServiceRoomAsync(0);
            }
            catch (Exception e)
            {
                Log.ErrorLine($"Failed to join service room: {GetServiceRoomId(0)}\n    {e.Message}");
                roomJoinException = e;
            }
        }
        else
        {
            var joined = false;
            var lastRoomIndex = rooms.Keys.Max(v => v);
            foreach (var (index, room) in rooms)
            {
                if (room.OnlineUsers >= 40)
                    continue;
                try
                {
                    connection = await JoinServiceRoomAsync(index);
                    joined = true;
                    break;
                }
                catch (Exception e)
                {
                    Log.ErrorLine($"Failed to join service room: {room.Id}\n    {e.Message}");
                    roomJoinException = e;
                }
            }

            if (!joined)
            {
                try
                {
                    connection = await JoinServiceRoomAsync(lastRoomIndex + 1);
                }
                catch (Exception e)
                {
                    Log.ErrorLine($"Failed to join service room: {GetServiceRoomId(lastRoomIndex + 1)}\n    {e.Message}");
                    roomJoinException = e;
                }
            }
        }

        if (connection is not null) 
            return connection;
        
        if (roomJoinException is not null)
            throw roomJoinException;
        
        throw new Exception("Failed to join suitable service room.");
    }

    public async Task<Connection> JoinServiceRoomAsync(int index)
    {
        if (CheckAuthenticated("Could not join service room because client was not authenticated.") is { } ex)
            throw ex;
        var client = Client!;
        var roomId = GetServiceRoomId(index);
        var connectionTask = new TaskCompletionSource<Connection>();
        client.Multiplayer.CreateJoinRoom(
            roomId,
            "service",
            true,
            [],
            new()
            {
                { "client_version", $"{ClientVersion}" },
            },
            connection =>
            {
                void OnRoomMessage(object sender, Message message)
                {
                    switch (message.Type)
                    {
                        case "error":
                            connection.OnMessage -= OnRoomMessage;
                            connection.OnDisconnect -= OnServiceDisconnect;
                            connection.OnMessage -= OnServiceMessage;
                            connection.Disconnect();
                            connectionTask.TrySetException(new Exception(message.GetString(0)));
                            return;
                        case "groupdisallowedjoin":
                            connection.OnMessage -= OnRoomMessage;
                            connection.OnDisconnect -= OnServiceDisconnect;
                            connection.OnMessage -= OnServiceMessage;
                            connection.Disconnect();
                            connectionTask.TrySetException(new Exception("Disallowed join!"));
                            return;
                        case "joined":
                            connection.OnDisconnect += OnServiceDisconnect;
                            connection.OnMessage += OnServiceMessage;
                            OnServiceJoin(connection, message);
                            connectionTask.TrySetResult(connection);
                            return;
                    }
                }
                connection.OnMessage += OnRoomMessage;
            },
            error =>
            {
                connectionTask.TrySetException(error);
            }
        );
        return await connectionTask.Task;
    }

    public void OnServiceJoin(Connection connection, Message message)
    {
        Log.TraceLine(message);
    }
    
    public void OnServiceMessage(object sender, Message message)
    {
        
    }
    
    public void OnServiceDisconnect(object sender, string reason)
    {
        
    }
    #endregion
    
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Client?.Logout();
    }
}