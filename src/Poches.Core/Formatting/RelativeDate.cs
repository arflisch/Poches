using System.Globalization;

namespace Poches.Core.Formatting;

/// <summary>Translated building blocks for <see cref="RelativeDate"/>; formats use {0} for the value.</summary>
public sealed record RelativeDateTexts(string Today, string Yesterday, string DaysAgoFormat, string OnDateFormat)
{
    public static RelativeDateTexts French { get; } = new("aujourd'hui", "hier", "il y a {0} jours", "le {0}");
}

public static class RelativeDate
{
    /// <summary>"today", "yesterday", "5 days ago", then "on 3 August 2026" beyond a month.</summary>
    public static string Describe(DateTime then, DateTime now, RelativeDateTexts texts, CultureInfo culture)
    {
        var days = (now.Date - then.Date).Days;
        return days switch
        {
            <= 0 => texts.Today,
            1 => texts.Yesterday,
            < 31 => string.Format(culture, texts.DaysAgoFormat, days),
            _ => string.Format(culture, texts.OnDateFormat, then.ToString("d MMMM yyyy", culture)),
        };
    }
}
