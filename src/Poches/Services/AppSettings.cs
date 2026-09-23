using Poches.Core.Formatting;

namespace Poches.Services;

/// <summary>User preferences persisted with <see cref="IPreferences"/>.</summary>
public sealed class AppSettings
{
    public static readonly IReadOnlyList<string> Currencies = ["€", "CHF", "$", "£"];

    private const string CurrencyKey = "currency";
    private const string HideAmountsKey = "hide_amounts";

    private readonly IPreferences _preferences;

    public AppSettings(IPreferences preferences)
    {
        _preferences = preferences;
        Formatter = CreateFormatter();
    }

    /// <summary>Raised when the currency or the privacy mode changes.</summary>
    public event EventHandler? Changed;

    public MoneyFormatter Formatter { get; private set; }

    public string Currency
    {
        get => _preferences.Get(CurrencyKey, "€");
        set => Update(() => _preferences.Set(CurrencyKey, value));
    }

    public bool HideAmounts
    {
        get => _preferences.Get(HideAmountsKey, false);
        set => Update(() => _preferences.Set(HideAmountsKey, value));
    }

    private void Update(Action write)
    {
        write();
        Formatter = CreateFormatter();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private MoneyFormatter CreateFormatter() => new(Currency, HideAmounts);
}
