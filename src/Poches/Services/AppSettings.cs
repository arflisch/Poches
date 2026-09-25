using Poches.Core.Formatting;
using Poches.Localization;

namespace Poches.Services;

/// <summary>User preferences persisted with <see cref="IPreferences"/>.</summary>
public sealed class AppSettings
{
    public static readonly IReadOnlyList<string> Currencies = ["€", "CHF", "$", "£"];

    private const string CurrencyKey = "currency";
    private const string HideAmountsKey = "hide_amounts";
    private const string LastBackupKey = "last_backup";
    private const string LanguageKey = "language";

    private readonly IPreferences _preferences;

    public AppSettings(IPreferences preferences)
    {
        _preferences = preferences;
        Localizer.Instance.SetLanguage(Language);
        Formatter = CreateFormatter();
    }

    /// <summary>Raised when any setting changes.</summary>
    public event EventHandler? Changed;

    public MoneyFormatter Formatter { get; private set; }

    /// <summary>The chosen language; defaults to the phone's language when supported.</summary>
    public AppLanguage Language
    {
        get => Localizer.Find(_preferences.Get<string?>(LanguageKey, null)) ?? Localizer.DeviceLanguage;
        set => Update(() =>
        {
            _preferences.Set(LanguageKey, value.Code);
            Localizer.Instance.SetLanguage(value);
        });
    }

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

    /// <summary>When the user last exported a backup file, or null if never.</summary>
    public DateTime? LastBackupAt
    {
        get => _preferences.ContainsKey(LastBackupKey) ? _preferences.Get(LastBackupKey, DateTime.MinValue) : null;
        set => Update(() =>
        {
            if (value is { } date)
                _preferences.Set(LastBackupKey, date);
            else
                _preferences.Remove(LastBackupKey);
        });
    }

    private void Update(Action write)
    {
        write();
        Formatter = CreateFormatter();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private MoneyFormatter CreateFormatter() => new(Currency, HideAmounts, Localizer.Instance.Culture);
}
