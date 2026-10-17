using Poches.Core.Models;

namespace Poches.Core.Data;

/// <summary>Names and notes of the sample data, in the user's language.</summary>
public sealed record SampleTexts(
    string Savings,
    string StartingAmount,
    string MonthlyTransfer,
    string Investments,
    string Stocks,
    string LifeInsurance,
    string Holidays,
    string Bonus,
    string HolidayRental,
    string NewCar,
    string Gifts,
    string Birthday)
{
    public static SampleTexts French { get; } = new(
        "Épargne de précaution", "Montant de départ", "Virement mensuel", "Investissement", "PEA", "Assurance vie",
        "Vacances", "Prime", "Location Biarritz", "Nouvelle voiture", "Cadeaux", "Anniversaire Léa");
}

public sealed partial class BudgetStore
{
    /// <summary>Fills an empty database with a few realistic pockets so the app can be explored right away.</summary>
    public async Task SeedSampleDataAsync(DateTime now, SampleTexts? texts = null)
    {
        var t = texts ?? SampleTexts.French;
        var db = await GetConnectionAsync();
        if (await db.Table<Pocket>().CountAsync() > 0)
            return;

        await db.RunInTransactionAsync(conn =>
        {
            Pocket Add(string name, string icon, string color, decimal? goal, int monthsAgo)
            {
                var pocket = new Pocket { Name = name, Icon = icon, ColorHex = color, Goal = goal, CreatedAt = now.AddMonths(-monthsAgo) };
                conn.Insert(pocket);
                return pocket;
            }

            void Move(Pocket pocket, decimal amount, int monthsAgo, int day, string? note = null)
            {
                var month = now.AddMonths(-monthsAgo);
                var date = new DateTime(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)), 9, 0, 0);
                if (date > now)
                    date = now;
                conn.Insert(new Movement
                {
                    PocketId = pocket.Id,
                    AmountCents = Money.ToCents(amount),
                    Kind = amount >= 0 ? MovementKind.Deposit : MovementKind.Withdrawal,
                    Note = note,
                    Date = date,
                });
            }

            var safety = Add(t.Savings, "🛟", "#10B981", 6000m, 6);
            Move(safety, 2000m, 6, 2, t.StartingAmount);
            for (var m = 5; m >= 0; m--)
                Move(safety, 250m, m, 3, t.MonthlyTransfer);

            var invest = Add(t.Investments, "📈", "#6366F1", null, 6);
            Move(invest, 1500m, 6, 5, t.Stocks);
            Move(invest, 400m, 4, 5, t.Stocks);
            Move(invest, 400m, 3, 5, t.Stocks);
            Move(invest, 600m, 2, 5, t.LifeInsurance);
            Move(invest, 400m, 1, 5, t.Stocks);
            Move(invest, 400m, 0, 5, t.Stocks);

            var holidays = Add(t.Holidays, "🏖️", "#F97316", 2500m, 5);
            Move(holidays, 300m, 5, 10);
            Move(holidays, 300m, 4, 10);
            Move(holidays, 450m, 3, 10, t.Bonus);
            Move(holidays, -620m, 2, 18, t.HolidayRental);
            Move(holidays, 300m, 1, 10);
            Move(holidays, 300m, 0, 10);

            var car = Add(t.NewCar, "🚗", "#0891B2", 8000m, 4);
            Move(car, 500m, 4, 12);
            Move(car, 200m, 3, 12);
            Move(car, 200m, 2, 12);
            Move(car, 200m, 1, 12);
            Move(car, 200m, 0, 12);

            var gifts = Add(t.Gifts, "🎁", "#EC4899", null, 3);
            Move(gifts, 150m, 3, 20);
            Move(gifts, -45m, 2, 22, t.Birthday);
            Move(gifts, 100m, 0, 20);
        });

        OnChanged();
    }
}

/// <summary>Names of the sample investments, in the user's language.</summary>
public sealed record SampleInvestmentTexts(string World, string Sp500, string EuroFund, string StockAccount, string LifeInsurance)
{
    public static SampleInvestmentTexts French { get; } = new("ETF MSCI World", "ETF S&P 500", "Fonds euros", "PEA", "Assurance vie");
}

public sealed partial class BudgetStore
{
    /// <summary>
    /// Fills empty investments with monthly purchases over the last ten months. The ETFs carry their real Yahoo
    /// symbols, so switching online quotes on brings them to life; their manual prices are used until then.
    /// </summary>
    public async Task SeedSampleInvestmentsAsync(DateTime today, SampleInvestmentTexts? texts = null)
    {
        var t = texts ?? SampleInvestmentTexts.French;
        var db = await GetConnectionAsync();
        if (await db.Table<Holding>().CountAsync() > 0)
            return;

        await db.RunInTransactionAsync(conn =>
        {
            Holding Add(string name, string? symbol, string? code, HoldingType type, string account, string color, decimal price)
            {
                var holding = new Holding
                {
                    Name = name, Symbol = symbol, Code = code, Type = type, Account = account, ColorHex = color,
                    ManualPrice = price, ManualPriceDate = today.Date, CreatedAt = today.AddMonths(-10),
                };
                conn.Insert(holding);
                return holding;
            }

            void Buy(Holding holding, int monthsAgo, decimal amount, decimal price, decimal fees)
            {
                var month = today.AddMonths(-monthsAgo);
                var date = new DateTime(month.Year, month.Month, Math.Min(5, DateTime.DaysInMonth(month.Year, month.Month)));
                if (date > today.Date)
                    date = today.Date;
                conn.Insert(new Purchase
                {
                    HoldingId = holding.Id,
                    Date = date,
                    Quantity = decimal.Round(amount / price, 4),
                    UnitPrice = price,
                    Fees = fees,
                });
            }

            // A gentle climb with a dip four months ago, so later purchases do better than some earlier ones.
            decimal[] path = [0.86m, 0.88m, 0.91m, 0.90m, 0.94m, 0.97m, 0.92m, 0.95m, 0.98m, 1.00m];
            var world = Add(t.World, "CW8.PA", "LU1681043599", HoldingType.Etf, t.StockAccount, "#6366F1", 709.55m);
            var sp500 = Add(t.Sp500, "PSP5.PA", "FR0011871128", HoldingType.Etf, t.StockAccount, "#0891B2", 60.35m);
            for (var i = 0; i < path.Length; i++)
            {
                var monthsAgo = path.Length - 1 - i;
                Buy(world, monthsAgo, 300m, decimal.Round(709.55m * path[i], 2), 1.99m);
                if (i % 2 == 0)
                    Buy(sp500, monthsAgo, 200m, decimal.Round(60.35m * path[Math.Min(i + 1, path.Length - 1)], 2), 0.99m);
            }

            var euroFund = Add(t.EuroFund, null, null, HoldingType.Fund, t.LifeInsurance, "#10B981", 102.4m);
            Buy(euroFund, 9, 2000m, 100m, 0m);
        });

        OnChanged();
    }
}
