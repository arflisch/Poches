using System.Globalization;

namespace Poches.Core.Formatting;

public static class RelativeDate
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>"aujourd'hui", "hier", "il y a 5 jours", then "le 3 août 2026" beyond a month.</summary>
    public static string Describe(DateTime then, DateTime now)
    {
        var days = (now.Date - then.Date).Days;
        return days switch
        {
            <= 0 => "aujourd'hui",
            1 => "hier",
            < 31 => $"il y a {days} jours",
            _ => $"le {then.ToString("d MMMM yyyy", French)}",
        };
    }
}
