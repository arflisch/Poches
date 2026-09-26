using Poches.Core.Models;
using Poches.Localization;

namespace Poches.Services;

/// <summary>Display name, emoji and chart colour of each <see cref="ChargeCategory"/>.</summary>
public static class ChargeCategories
{
    public static readonly IReadOnlyList<ChargeCategory> All =
    [
        ChargeCategory.Subscription,
        ChargeCategory.Insurance,
        ChargeCategory.Housing,
        ChargeCategory.Loan,
        ChargeCategory.Other,
    ];

    public static string Name(ChargeCategory category) => category switch
    {
        ChargeCategory.Subscription => Loc.Get("Category_Subscription"),
        ChargeCategory.Insurance => Loc.Get("Category_Insurance"),
        ChargeCategory.Housing => Loc.Get("Category_Housing"),
        ChargeCategory.Loan => Loc.Get("Category_Loan"),
        _ => Loc.Get("Category_Other"),
    };

    /// <summary>Also the icon suggested for a new charge of this category.</summary>
    public static string Icon(ChargeCategory category) => category switch
    {
        ChargeCategory.Subscription => "📺",
        ChargeCategory.Insurance => "🛡️",
        ChargeCategory.Housing => "🏠",
        ChargeCategory.Loan => "🏦",
        _ => "📦",
    };

    public static Color Color(ChargeCategory category) => Microsoft.Maui.Graphics.Color.FromArgb(category switch
    {
        ChargeCategory.Subscription => "#8B5CF6",
        ChargeCategory.Insurance => "#0891B2",
        ChargeCategory.Housing => "#F97316",
        ChargeCategory.Loan => "#F43F5E",
        _ => "#475569",
    });
}
