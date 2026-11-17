using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Printify.Backend.Data;
using Printify.Backend.Services;

namespace Printify.Backend.Controllers;

/* ==========================================
   REPORTS - Admin only. All numbers come from PostgreSQL.
   Add ?format=csv to download a CSV (opens in Excel).
========================================== */

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db)
    {
        _db = db;
    }

    private bool WantsCsv(string format) => string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

    private FileContentResult CsvFile(string name, string[] headers, IEnumerable<string[]> rows) =>
        File(Csv.Build(headers, rows), "text/csv; charset=utf-8", $"{name}-{DateTime.Today:yyyyMMdd}.csv");

    private static string UiStatus(string s) => s == "In Progress" ? "Processing" : s;

    /* All four chart periods at once (used by the Reports page). */
    [HttpGet("chart")]
    public async Task<IActionResult> Chart()
    {
        var orders = await _db.PrintOrders.AsNoTracking().ToListAsync();
        var today = DateTime.Today;

        object One(string kind, int count)
        {
            var s = SalesAggregator.Series(orders, kind, count, today);
            return new { labels = s.Labels, values = s.Collected, billed = s.Billed, orders = s.Orders };
        }

        return Ok(new { daily = One("day", 14), weekly = One("week", 12), monthly = One("month", 12), yearly = One("year", 5) });
    }

    /* Daily / weekly / monthly / yearly sales table. */
    [HttpGet("sales")]
    public async Task<IActionResult> Sales([FromQuery] string period = "daily", [FromQuery] string format = "json")
    {
        var (kind, count) = (period ?? "").ToLowerInvariant() switch
        {
            "weekly" => ("week", 12),
            "monthly" => ("month", 12),
            "yearly" => ("year", 5),
            "daily" => ("day", 30),
            _ => ((string)null, 0)
        };

        if (kind == null) return BadRequest(new { message = "period must be daily, weekly, monthly or yearly." });

        var orders = await _db.PrintOrders.AsNoTracking().ToListAsync();
        var s = SalesAggregator.Series(orders, kind, count, DateTime.Today);

        var rows = s.Labels.Select((label, i) => new { period = label, orders = s.Orders[i], billed = s.Billed[i], collected = s.Collected[i] }).ToList();

        if (WantsCsv(format))
            return CsvFile("sales-" + period, new[] { "Period", "Orders", "Billed", "Collected" },
                rows.Select(r => new[] { Csv.Text(r.period), r.orders.ToString(), Csv.Num(r.billed), Csv.Num(r.collected) }));

        return Ok(new { period, items = rows });
    }

    /* Every order (what the Reports table shows). */
    [HttpGet("orders")]
    public async Task<IActionResult> Orders([FromQuery] string format = "json")
    {
        var orders = await _db.PrintOrders.AsNoTracking().OrderByDescending(o => o.CreatedAt).ToListAsync();

        if (WantsCsv(format))
            return CsvFile("orders", new[] { "Order ID", "Customer", "Service", "Quantity", "Total", "Paid", "Order Date", "Status", "Payment" },
                orders.Select(o => new[] { Csv.Text(o.Id), Csv.Text(o.CustomerName), Csv.Text(o.PrintingService), o.Quantity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    Csv.Num(o.Total), Csv.Num(o.AmountPaid), Csv.Text(SalesAggregator.OrderDay(o).ToString("yyyy-MM-dd")), Csv.Text(o.OrderStatus), Csv.Text(o.PaymentStatus) }));

        return Ok(new
        {
            items = orders.Select(o => new
            {
                orderId = o.Id,
                customer = o.CustomerName,
                date = SalesAggregator.OrderDay(o).ToString("yyyy-MM-dd"),
                amount = o.Total,
                amountPaid = o.AmountPaid,
                orderStatus = UiStatus(o.OrderStatus),
                paymentStatus = o.PaymentStatus
            })
        });
    }

    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory([FromQuery] string format = "json")
    {
        var items = await _db.InventoryItems.AsNoTracking().OrderBy(i => i.Name).ToListAsync();

        if (WantsCsv(format))
            return CsvFile("inventory", new[] { "ID", "Name", "Category", "Quantity", "Unit", "Unit Price", "Stock Value", "Low-stock Level", "Status" },
                items.Select(i => new[] { Csv.Text(i.Id), Csv.Text(i.Name), Csv.Text(i.Category), Csv.Num(i.Quantity), Csv.Text(i.Unit), Csv.Num(i.Price),
                    Csv.Num(i.Quantity * i.Price), Csv.Num(i.Threshold), Csv.Text(i.Quantity <= 0 ? "Out of stock" : i.Quantity <= i.Threshold ? "Low" : "OK") }));

        return Ok(new { items, totalValue = items.Sum(i => i.Quantity * i.Price) });
    }

    [HttpGet("staff-performance")]
    public async Task<IActionResult> StaffPerformance([FromQuery] string format = "json")
    {
        var staff = await _db.Users.AsNoTracking().Where(u => u.Role == "Staff").OrderBy(u => u.FullName).ToListAsync();
        var orders = await _db.PrintOrders.AsNoTracking().Where(o => o.AssignedStaffId != null).ToListAsync();

        var rows = staff.Select(s =>
        {
            var mine = orders.Where(o => o.AssignedStaffId == s.Id).ToList();
            var done = mine.Count(o => o.OrderStatus == "Completed");
            var counted = mine.Count(o => o.OrderStatus != "Cancelled");
            return new
            {
                staffCode = s.StaffCode,
                name = s.FullName,
                status = s.Status,
                assigned = mine.Count,
                pending = mine.Count(o => o.OrderStatus == "Pending"),
                inProgress = mine.Count(o => o.OrderStatus == "In Progress"),
                completed = done,
                cancelled = mine.Count(o => o.OrderStatus == "Cancelled"),
                completionRate = counted == 0 ? 0m : Math.Round(100m * done / counted, 1),
                revenueCompleted = mine.Where(o => o.OrderStatus == "Completed").Sum(o => o.AmountPaid)
            };
        }).ToList();

        if (WantsCsv(format))
            return CsvFile("staff-performance", new[] { "Staff ID", "Name", "Status", "Assigned", "Pending", "In Progress", "Completed", "Cancelled", "Completion %", "Revenue (completed)" },
                rows.Select(r => new[] { Csv.Text(r.staffCode), Csv.Text(r.name), Csv.Text(r.status), r.assigned.ToString(), r.pending.ToString(), r.inProgress.ToString(),
                    r.completed.ToString(), r.cancelled.ToString(), Csv.Num(r.completionRate), Csv.Num(r.revenueCompleted) }));

        return Ok(new { items = rows });
    }
}
