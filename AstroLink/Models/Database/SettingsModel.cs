using Astro.Utils;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Astro.Models.Database;

public class SettingsModel
{
    public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
    
    [BsonSerializer(typeof(UlongKeyDictionarySerializer))]
    public Dictionary<ulong, ulong> SupporterRolesForGuilds { get; set; } = [];
}