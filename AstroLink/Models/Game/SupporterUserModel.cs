using System.Globalization;
using Astro.Managers;
using Astro.Models.Database;

namespace Astro.Models.Game;

public class SupporterUserModel
{
    public readonly DiscordManager Discord;
    public UserLinkModel UserLink;
    public DateTime ExpirationDate { get; set; }
    public bool IsMod { get; set; }

    public SupporterUserModel(DiscordManager discord, UserLinkModel userLink, DateTime expirationDate, bool isMod)
    {
        Discord = discord;
        UserLink = userLink;
        ExpirationDate = expirationDate;
        IsMod = isMod;
    }

    public bool IsExpired => ExpirationDate < DateTime.Now && !IsMod;
    
    public static DateTime? TryParseExpirationDate(string date)
    {
        try
        {
            const string format = "dd/MM/yyyy HH:mm:ss";
            var parsedDate = DateTime.ParseExact(date, format, CultureInfo.InvariantCulture);
            return parsedDate;
        }
        catch
        {
            return null;
        }
    }
}