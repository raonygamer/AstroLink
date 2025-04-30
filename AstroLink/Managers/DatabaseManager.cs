using Astro.Core;
using Astro.Models.Database;
using Astro.Utils;
using MongoDB.Driver;

namespace Astro.Managers;

public class DatabaseManager : IDisposable
{
    public const string DatabaseName = "AstroLinkDb";
    public const string SettingsCollectionName = "Settings";
    public const string LinksCollectionName = "Links";
    
    public readonly AstroLink Main;
    public readonly MongoClient Client;
    public readonly IMongoDatabase Database;
    public readonly IMongoCollection<SettingsModel> SettingsCollection;
    public readonly IMongoCollection<UserLinkModel> UserLinkCollection;
    
    private DatabaseManager(AstroLink main, MongoClient client)
    {
        Main = main;
        Client = client;
        Database = client.GetDatabase(DatabaseName);
        SettingsCollection = Database.GetCollection<SettingsModel>(SettingsCollectionName);
        UserLinkCollection = Database.GetCollection<UserLinkModel>(LinksCollectionName);
    }

    public static async Task<DatabaseManager> CreateAsync(AstroLink main, string connectionString)
    {
        Log.TraceLine($"Creating database manager with connection '{connectionString}'...");
        var client = new MongoClient(connectionString);
        var manager = new DatabaseManager(main, client);
        Log.SuccessLine("Database manager created successfully.");
        return manager;
    }

    public async Task<SettingsModel?> LoadSettingsAsync()
    {
        var foundCollection = await (await SettingsCollection.FindAsync(Builders<SettingsModel>.Filter.Empty)).ToListAsync();
        return foundCollection.Count == 0 ? null : foundCollection.SingleOrDefault();
    }

    public async Task SaveSettingsAsync(SettingsModel settings)
    {
        if (await LoadSettingsAsync() is null)
        {
            await SettingsCollection.InsertOneAsync(settings);
            return;
        }
        await SettingsCollection.ReplaceOneAsync(Builders<SettingsModel>.Filter.Empty, settings);
    }

    public async Task<List<UserLinkModel>> GetUserLinksAsync()
    {
        return await (await UserLinkCollection.FindAsync(Builders<UserLinkModel>.Filter.Empty)).ToListAsync();
    }
    
    public List<UserLinkModel> GetUserLinks()
    {
        return UserLinkCollection.Find(Builders<UserLinkModel>.Filter.Empty).ToList();
    }

    public async Task<UserLinkModel?> GetUserLinkByDiscordUserIdAsync(ulong userId)
    {
        var filter = Builders<UserLinkModel>.Filter.Eq(x => x.DiscordUserId, userId);
        var collection = await (await UserLinkCollection.FindAsync(filter)).ToListAsync();
        return collection.Count == 0 ? null : collection.SingleOrDefault();
    }
    
    public async Task<UserLinkModel?> GetUserLinkByGamePlayerIdAsync(string playerId)
    {
        var filter = Builders<UserLinkModel>.Filter.Eq(x => x.GamePlayerId, playerId);
        var collection = await (await UserLinkCollection.FindAsync(filter)).ToListAsync();
        return collection.Count == 0 ? null : collection.SingleOrDefault();
    }

    public async Task AddLinkAsync(UserLinkModel link)
    {
        var filter = Builders<UserLinkModel>.Filter.Eq(x => x.DiscordUserId, link.DiscordUserId) & Builders<UserLinkModel>.Filter.Eq(x => x.GamePlayerId, link.GamePlayerId);
        if (await UserLinkCollection.Find(filter).AnyAsync())
            return;
        await UserLinkCollection.InsertOneAsync(link);
    }

    public async Task RemoveLinkAsync(UserLinkModel link)
    {
        var filter = Builders<UserLinkModel>.Filter.Eq(x => x.DiscordUserId, link.DiscordUserId) & Builders<UserLinkModel>.Filter.Eq(x => x.GamePlayerId, link.GamePlayerId);
        if (await UserLinkCollection.Find(filter).AnyAsync())
        {
            await UserLinkCollection.DeleteOneAsync(filter);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Client.Dispose();
    }
}