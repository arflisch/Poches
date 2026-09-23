using Poches.Core.Models;

namespace Poches.Core.Data;

public sealed partial class BudgetStore
{
    /// <summary>Fills an empty database with a few realistic pockets so the app can be explored right away.</summary>
    public async Task SeedSampleDataAsync(DateTime now)
    {
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

            var safety = Add("Épargne de précaution", "🛟", "#10B981", 6000m, 6);
            Move(safety, 2000m, 6, 2, "Montant de départ");
            for (var m = 5; m >= 0; m--)
                Move(safety, 250m, m, 3, "Virement mensuel");

            var invest = Add("Investissement", "📈", "#6366F1", null, 6);
            Move(invest, 1500m, 6, 5, "PEA");
            Move(invest, 400m, 4, 5, "PEA");
            Move(invest, 400m, 3, 5, "PEA");
            Move(invest, 600m, 2, 5, "Assurance vie");
            Move(invest, 400m, 1, 5, "PEA");
            Move(invest, 400m, 0, 5, "PEA");

            var holidays = Add("Vacances", "🏖️", "#F97316", 2500m, 5);
            Move(holidays, 300m, 5, 10);
            Move(holidays, 300m, 4, 10);
            Move(holidays, 450m, 3, 10, "Prime");
            Move(holidays, -620m, 2, 18, "Location Biarritz");
            Move(holidays, 300m, 1, 10);
            Move(holidays, 300m, 0, 10);

            var car = Add("Nouvelle voiture", "🚗", "#0891B2", 8000m, 4);
            Move(car, 500m, 4, 12);
            Move(car, 200m, 3, 12);
            Move(car, 200m, 2, 12);
            Move(car, 200m, 1, 12);
            Move(car, 200m, 0, 12);

            var gifts = Add("Cadeaux", "🎁", "#EC4899", null, 3);
            Move(gifts, 150m, 3, 20);
            Move(gifts, -45m, 2, 22, "Anniversaire Léa");
            Move(gifts, 100m, 0, 20);
        });

        OnChanged();
    }
}
