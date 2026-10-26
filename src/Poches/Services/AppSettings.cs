using Poches.Core.Formatting;
using Poches.Core.Models;
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
    private const string ReminderKey = "reminder_days_before";
    private const string LockKey = "app_lock";
    private const string OnlineQuotesKey = "online_quotes";
    private const string BenchmarkKey = "investments_benchmark";
    private const string BelgianTaxKey = "belgian_tax";
    private const string UsTaxKey = "us_tax";
    private const string IgnoredDividendsKey = "ignored_dividends";
    private const string CsvMappingsKey = "csv_mappings";

    /// <summary>Reminder choices, in days before a payment (null = no reminder).</summary>
    public static readonly IReadOnlyList<int?> ReminderChoices = [null, 0, 1, 3, 7];

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

    /// <summary>Whether Face ID / Touch ID (or the passcode) is required to open the app.</summary>
    public bool LockEnabled
    {
        get => _preferences.Get(LockKey, false);
        set => Update(() => _preferences.Set(LockKey, value));
    }

    /// <summary>
    /// Whether investment quotes are fetched from Yahoo Finance. Off by default: Poches stays offline until the
    /// user turns this on.
    /// </summary>
    public bool OnlineQuotes
    {
        get => _preferences.Get(OnlineQuotesKey, false);
        set => Update(() => _preferences.Set(OnlineQuotesKey, value));
    }

    /// <summary>Symbol the investments are compared with on the evolution chart; empty for none.</summary>
    public string Benchmark
    {
        get => _preferences.Get(BenchmarkKey, "CW8.PA");
        set => Update(() => _preferences.Set(BenchmarkKey, value));
    }

    /// <summary>
    /// The tax rules offered for the phone's region: Belgian ones in Belgium, US ones in the United States, none
    /// elsewhere.
    /// </summary>
    public static TaxCountry RegionTaxCountry => DeviceRegion switch
    {
        "BE" => TaxCountry.Belgium,
        "US" => TaxCountry.UnitedStates,
        _ => TaxCountry.None,
    };

    /// <summary>
    /// The region set in the phone's settings. Asked to the system: .NET's current region follows the culture the app
    /// imposes for its language (fr-FR for French), not the phone's region.
    /// </summary>
    private static string? DeviceRegion
    {
        get
        {
#if IOS || MACCATALYST
            // A region that differs from the language's one is kept apart: "en_US@rg=bezzzz" is English in Belgium.
            var locale = Foundation.NSLocale.CurrentLocale;
            var marker = locale.Identifier.IndexOf("rg=", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0 && locale.Identifier.Length >= marker + 5)
                return locale.Identifier.Substring(marker + 3, 2).ToUpperInvariant();
            return locale.CountryCode?.ToUpperInvariant();
#elif ANDROID
            return Java.Util.Locale.Default.Country?.ToUpperInvariant();
#else
            try
            {
                return System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
            }
            catch (ArgumentException)
            {
                return null;
            }
#endif
        }
    }

    /// <summary>
    /// Whether the investments follow the tax rules of the phone's region (on by default there): in Belgium the
    /// stock-exchange tax, the capital gains tax and tax envelopes; in the United States short- and long-term capital
    /// gains, loss carryforward, wash sales and retirement accounts.
    /// </summary>
    public bool TaxRules
    {
        get => RegionTaxCountry switch
        {
            TaxCountry.Belgium => _preferences.Get(BelgianTaxKey, true),
            TaxCountry.UnitedStates => _preferences.Get(UsTaxKey, true),
            _ => false,
        };
        set => Update(() => _preferences.Set(RegionTaxCountry == TaxCountry.UnitedStates ? UsTaxKey : BelgianTaxKey, value));
    }

    /// <summary>The tax rules in force: those of the phone's region when the option is on.</summary>
    public TaxCountry TaxCountry => TaxRules ? RegionTaxCountry : TaxCountry.None;

    public bool BelgianTax => TaxCountry == TaxCountry.Belgium;

    public bool UsTax => TaxCountry == TaxCountry.UnitedStates;

    /// <summary>Detected dividends the user chose to ignore (holding id and ex-date), on this device.</summary>
    public IReadOnlySet<string> IgnoredDividends =>
        _preferences.Get(IgnoredDividendsKey, string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    public void IgnoreDividend(string key)
    {
        var keys = _preferences.Get(IgnoredDividendsKey, string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (keys.Contains(key))
            return;
        keys.Add(key);
        // Silent: nothing on screen depends on the setting itself, the screens reload from their own data.
        _preferences.Set(IgnoredDividendsKey, string.Join('|', keys.TakeLast(500)));
    }

    /// <summary>The column mapping last used for a statement format, by its header row.</summary>
    public Dictionary<string, int>? CsvMapping(string headers) =>
        ReadCsvMappings().GetValueOrDefault(headers);

    public void SaveCsvMapping(string headers, Dictionary<string, int> mapping)
    {
        var all = ReadCsvMappings();
        all[headers] = mapping;
        _preferences.Set(CsvMappingsKey, System.Text.Json.JsonSerializer.Serialize(all.TakeLast(20).ToDictionary(), CsvMappingContext.Default.DictionaryStringDictionaryStringInt32));
    }

    private Dictionary<string, Dictionary<string, int>> ReadCsvMappings()
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize(_preferences.Get(CsvMappingsKey, "{}"), CsvMappingContext.Default.DictionaryStringDictionaryStringInt32) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>ISO code of the display currency, used to convert quotes ("€" → EUR).</summary>
    public string CurrencyCode => Currency switch
    {
        "CHF" => "CHF",
        "$" => "USD",
        "£" => "GBP",
        _ => "EUR",
    };

    /// <summary>How many days before a subscription payment to send a reminder; null disables reminders.</summary>
    public int? ReminderDaysBefore
    {
        get => _preferences.Get(ReminderKey, 1) is var days and >= 0 ? days : null;
        set => Update(() => _preferences.Set(ReminderKey, value ?? -1));
    }

    private void Update(Action write)
    {
        write();
        Formatter = CreateFormatter();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private MoneyFormatter CreateFormatter() => new(Currency, HideAmounts, Localizer.Instance.Culture);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, Dictionary<string, int>>))]
internal sealed partial class CsvMappingContext : System.Text.Json.Serialization.JsonSerializerContext;
