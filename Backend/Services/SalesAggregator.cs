
using System.Globalization;
using Printify.Backend.Models;

namespace Printify.Backend.Services;

public static class SalesAggregator
{
    public static DateTime OrderDay(PrintOrder o)
    {
        return DateTime.TryParse(
            o.OrderDate,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var d)
                ? d.Date
                : o.CreatedAt.Date;
    }

    // Supported kinds:
    // hour  = hourly sales for today
    // day   = last 7 days
    // month = last 12 months
    // year  = last 5 years

    public static (
        List<string> Labels,
        List<decimal> Billed,
        List<decimal> Collected,
        List<int> Orders
    ) Series(
        IEnumerable<PrintOrder> orders,
        string kind,
        int count,
        DateTime today)
    {
        DateTime Start(DateTime d) => kind switch
        {
            "hour" => new DateTime(
                d.Year,
                d.Month,
                d.Day,
                d.Hour,
                0,
                0
            ),

            "week" => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)),

            "month" => new DateTime(d.Year, d.Month, 1),

            "year" => new DateTime(d.Year, 1, 1),

            _ => d.Date
        };

        DateTime Step(DateTime d, int n) => kind switch
        {
            "hour" => d.AddHours(n),

            "week" => d.AddDays(7 * n),

            "month" => d.AddMonths(n),

            "year" => d.AddYears(n),

            _ => d.AddDays(n)
        };

        string Label(DateTime d) => kind switch
        {
            "hour" => d.ToString("h tt", CultureInfo.InvariantCulture),

            "month" => d.ToString("MMM yyyy", CultureInfo.InvariantCulture),

            "year" => d.ToString("yyyy", CultureInfo.InvariantCulture),

            _ => d.ToString("MMM d", CultureInfo.InvariantCulture)
        };

        var first = Step(Start(today.Date), -(count - 1));

        var starts = Enumerable
            .Range(0, count)
            .Select(i => Step(first, i))
            .ToList();

        var live = orders
            .Where(o => o.OrderStatus != "Cancelled")
            .ToList();

        var billed = new decimal[count];
        var collected = new decimal[count];
        var number = new int[count];

        foreach (var o in live)
        {
            // Daily chart uses the actual creation time.
            // Other charts use the order date.
            var orderDate = kind == "hour"
                ? o.CreatedAt
                : OrderDay(o);

            var bucket = Start(orderDate);

            var idx = starts.IndexOf(bucket);

            if (idx < 0)
                continue;

            billed[idx] += o.Total;
            collected[idx] += o.AmountPaid;
            number[idx]++;
        }

        return (
            starts.Select(Label).ToList(),
            billed.ToList(),
            collected.ToList(),
            number.ToList()
        );
    }
}
