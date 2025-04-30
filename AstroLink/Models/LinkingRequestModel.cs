namespace Astro.Models;

public class LinkingRequestModel(ulong userId, string username, TimeSpan expirationTime)
{
    public const int DefaultExpirationMinutes = 30;
    
    public ulong DiscordUserId { get; set; } = userId;
    public string DiscordUsername { get; set; } = username;
    public string RequestToken { get; set; } = Guid.NewGuid().ToString().Replace("-", "");
    public DateTime ExpirationDate { get; set; } = DateTime.UtcNow.Add(expirationTime);
    
    public bool IsExpired => ExpirationDate < DateTime.UtcNow;
    public bool IsValidUserId => DiscordUserId != 0 && DiscordUsername != string.Empty;
    public bool IsValid => !IsExpired && IsValidUserId && RequestToken != string.Empty;
}