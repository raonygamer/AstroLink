using Astro.Core;
using Astro.Models.Database;
using Astro.Utils;
using MongoDB.Driver;

namespace Astro.Managers;

public class DatabaseManager : IDisposable
{
    public AstroLink Main => AstroLink.Instance;
    
    public const string DatabaseName = "AstroLinkDb";
    public const string SettingsCollectionName = "Settings";
    public const string LinksCollectionName = "Links";
    
    public readonly MongoClient Client;
    public readonly IMongoDatabase Database;
    public readonly IMongoCollection<SettingsModel> SettingsCollection;
    
    public DatabaseManager(string connectionString)
    {
        Log.TraceLine($"Connecting to database with '{connectionString}'...");
        Client = new MongoClient(connectionString);
        Database = Client.GetDatabase(DatabaseName);
        SettingsCollection = Database.GetCollection<SettingsModel>(SettingsCollectionName);
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

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Client.Dispose();
    }
}