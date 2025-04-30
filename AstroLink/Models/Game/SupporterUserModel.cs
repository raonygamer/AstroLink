using System.Globalization;

namespace Astro.Models.Game;

public class SupporterUserModel
{
    public string PlayerId { get; set; } = string.Empty;
    public DateTime ExpirationDate { get; set; }

    public static DateTime ParseExpirationDate(string date)
    {
        const string format = "dd/MM/yyyy HH:mm:ss";
        DateTime parsedDate = DateTime.ParseExact(date, format, CultureInfo.InvariantCulture);
        return parsedDate;
    }
}