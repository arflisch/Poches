using Poches.Core.Models;

namespace Poches.Core.Subscriptions;

/// <summary>Payment dates and normalised costs of a recurring subscription.</summary>
public static class BillingSchedule
{
    /// <summary>The first payment on or after <paramref name="from"/> (dates only).</summary>
    public static DateTime NextPayment(DateTime anchor, BillingPeriod period, DateTime from) =>
        Payments(anchor, period, from).First();

    /// <summary>
    /// Payments on or after <paramref name="from"/>, in order. Each date is computed from the anchor rather than
    /// from the previous payment, so a subscription billed on the 31st goes back to the 31st after February.
    /// </summary>
    public static IEnumerable<DateTime> Payments(DateTime anchor, BillingPeriod period, DateTime from)
    {
        anchor = anchor.Date;
        from = from.Date;

        // Jump straight to the period containing "from"; this estimate never skips a payment.
        var index = anchor >= from ? 0 : Math.Max(0, PeriodsBetween(anchor, from, period));
        while (true)
        {
            var date = Add(anchor, period, index++);
            if (date >= from)
                yield return date;
        }
    }

    public static DateTime Add(DateTime anchor, BillingPeriod period, int count) => period switch
    {
        BillingPeriod.Weekly => anchor.AddDays(7 * count),
        BillingPeriod.Monthly => anchor.AddMonths(count),
        BillingPeriod.Quarterly => anchor.AddMonths(3 * count),
        BillingPeriod.Yearly => anchor.AddYears(count),
        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };

    /// <summary>Average cost per month, e.g. a yearly 120 € is 10 € a month.</summary>
    public static decimal MonthlyCost(decimal amount, BillingPeriod period) => YearlyCost(amount, period) / 12m;

    public static decimal YearlyCost(decimal amount, BillingPeriod period) => period switch
    {
        BillingPeriod.Weekly => amount * 52m,
        BillingPeriod.Monthly => amount * 12m,
        BillingPeriod.Quarterly => amount * 4m,
        BillingPeriod.Yearly => amount,
        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };

    private static int PeriodsBetween(DateTime anchor, DateTime from, BillingPeriod period)
    {
        var months = (from.Year - anchor.Year) * 12 + from.Month - anchor.Month;
        return period switch
        {
            BillingPeriod.Weekly => (from - anchor).Days / 7,
            BillingPeriod.Monthly => months,
            BillingPeriod.Quarterly => months / 3,
            BillingPeriod.Yearly => from.Year - anchor.Year,
            _ => throw new ArgumentOutOfRangeException(nameof(period)),
        };
    }
}
