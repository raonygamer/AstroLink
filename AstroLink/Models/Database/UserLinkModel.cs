using MongoDB.Bson;

namespace Astro.Models.Database;

public class UserLinkModel
{
    public ObjectId Id { get; set; }
    public string DiscordUsername { get; set; } = string.Empty;
    public ulong DiscordUserId { get; set; } = 0;
    public string GamePlayerId { get; set; } = string.Empty;

    public override bool Equals(object? obj)
    {
        return obj is UserLinkModel userLink && Equals(userLink);
    }

    protected bool Equals(UserLinkModel other)
    {
        return DiscordUserId == other.DiscordUserId && GamePlayerId == other.GamePlayerId;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(DiscordUserId, GamePlayerId);
    }
    
    public static bool operator ==(UserLinkModel left, UserLinkModel right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(UserLinkModel left, UserLinkModel right)
    {
        return !(left == right);
    }
}