using Astro.Utils;
namespace Astro.Models;

public class VariableModel : ArgumentModel
{
    public string GameId { get; set; } = string.Empty;
    public string GameEmail { get; set; } = string.Empty;
    public string GamePassword { get; set; } = string.Empty;
    public string DatabaseString { get; set; } = string.Empty;
    public string DiscordToken { get; set; } = string.Empty;
    public ulong BotOwnerId { get; set; } = 0;

    public bool IsValid()
    {
        return GameId != string.Empty && 
               GameEmail != string.Empty && 
               GamePassword != string.Empty && 
               DatabaseString != string.Empty && 
               DiscordToken != string.Empty &&
               BotOwnerId != 0;
    }
}