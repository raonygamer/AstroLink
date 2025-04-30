using MongoDB.Bson;

namespace Astro.Models.Database;

public class UserLinkModel
{
    public ObjectId Id { get; set; }
    public string DiscordUsername { get; set; } = string.Empty;
    public ulong DiscordUserId { get; set; } = 0;
    public string GamePlayerId { get; set; } = string.Empty;
}