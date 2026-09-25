using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;

namespace Poches.Localization;

/// <summary>Short-hands to read translated texts from code.</summary>
public static class Loc
{
    private static Localizer L => Localizer.Instance;

    public static string Get(string key) => L[key];

    public static string Format(string key, params object?[] args) => string.Format(L.Culture, L[key], args);

    /// <summary>Translated message for a refused operation (keys "Error_" + <see cref="BudgetError"/> name).</summary>
    public static string Error(BudgetException exception) => L[$"Error_{exception.Error}"];

    /// <summary>"1 pocket", "3 pockets"… from the "{noun}_One" / "{noun}_Many" keys.</summary>
    public static string Count(int count, string noun) =>
        $"{count} {L[$"{noun}_{(IsSingular(count) ? "One" : "Many")}"]}";

    public static string Noun(int count, string noun) => L[$"{noun}_{(IsSingular(count) ? "One" : "Many")}"];

    public static string RelativeDate(DateTime then) =>
        Core.Formatting.RelativeDate.Describe(
            then,
            DateTime.Now,
            new RelativeDateTexts(L["Date_Today"], L["Date_Yesterday"], L["Date_DaysAgo"], L["Date_On"]),
            L.Culture);

    /// <summary>Formats a date with the current language, capitalising the first letter ("Thursday 24 September").</summary>
    public static string Date(DateTime date, string format)
    {
        var text = date.ToString(format, L.Culture);
        return text.Length == 0 ? text : char.ToUpper(text[0], L.Culture) + text[1..];
    }

    public static SampleTexts SampleTexts => new(
        L["Sample_Savings"], L["Edit_InitialNote"], L["Sample_MonthlyTransfer"], L["Sample_Investments"],
        L["Sample_Stocks"], L["Sample_LifeInsurance"], L["Sample_Holidays"], L["Sample_Bonus"],
        L["Sample_HolidayRental"], L["Sample_NewCar"], L["Sample_Gifts"], L["Sample_Birthday"]);

    // French treats 0 as singular ("0 poche"), English and Dutch do not.
    private static bool IsSingular(int count) => count == 1 || (count == 0 && L.Language.Code == "fr");
}
