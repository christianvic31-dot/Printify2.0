using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Printify.Backend.Data;
using Printify.Backend.Services;

namespace Printify.Backend.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;

    public DashboardController(AppDbContext db)
    {
        _db = db;
    }

    /* ---------- ADMIN: business-wide numbers ---------- */

    [Authorize(Roles = "Admin")]
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var today = DateTime.Today;

        var orders = await _db.PrintOrders.AsNoTracking().ToListAsync();
        var live = orders.Where(o => o.OrderStatus != "Cancelled").ToList();
        var inventory = await _db.InventoryItems.AsNoTracking().ToListAsync();

        var staff = await _db.Users.AsNoTracking().Where(u => u.Role == "Staff").ToListAsync();
        var activeStaffIds = staff.Where(s => s.Status == "Active").Select(s => s.Id).ToHashSet();

        var low = inventory
            .Where(i => i.Quantity <= i.Threshold)
            .OrderBy(i => i.Quantity - i.Threshold)
            .ToList();

        object Series(string kind, int count)
        {
            var s = SalesAggregator.Series(orders, kind, count, today);
            return new { labels = s.Labels, values = s.Collected, billed = s.Billed, orders = s.Orders };
        }

        var recent = orders.OrderByDescending(o => o.CreatedAt).Take(5).Select(o => new
        {
            id = o.Id,
            customer = o.CustomerName,
            service = o.PrintingService,
            total = o.Total,
            status = o.OrderStatus,
            paymentStatus = o.PaymentStatus,
            date = o.OrderDate
        });

        var activity = await _db.ActivityLogs.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt).Take(8)
            .Select(a => new { a.Username, a.Role, a.Action, a.Details, a.CreatedAt })
            .ToListAsync();

        return Ok(new
        {
            totalOrders = orders.Count,
            pendingOrders = orders.Count(o => o.OrderStatus == "Pending"),
            inProgressOrders = orders.Count(o => o.OrderStatus == "In Progress"),
            completedOrders = orders.Count(o => o.OrderStatus == "Completed"),
            cancelledOrders = orders.Count(o => o.OrderStatus == "Cancelled"),
            totalSales = live.Sum(o => o.AmountPaid),     // money actually collected
            totalBilled = live.Sum(o => o.Total),
            totalCustomers = orders.Select(o => (o.CustomerName ?? "").Trim().ToLowerInvariant()).Where(n => n.Length > 0).Distinct().Count(),
            activeStaff = activeStaffIds.Count,
            totalStaff = staff.Count,
            currentlyWorking = orders.Where(o => o.OrderStatus == "In Progress" && o.AssignedStaffId != null && activeStaffIds.Contains(o.AssignedStaffId.Value))
                                     .Select(o => o.AssignedStaffId).Distinct().Count(),
            completedStaffWork = orders.Count(o => o.OrderStatus == "Completed" && o.AssignedStaffId != null),
            lowStockCount = low.Count,
            outOfStockCount = inventory.Count(i => i.Quantity <= 0),
            lowStock = low.Take(8).Select(i => new
            {
                i.Id, i.Name, i.Category, i.Quantity, i.Unit, i.Threshold,
                level = i.Quantity <= 0 ? "out" : (i.Quantity <= i.Threshold / 2 ? "critical" : "low")
            }),
            recentOrders = recent,
            recentActivity = activity,
            sales = new
            {
                daily = Series("hour", 24),
                weekly = Series("day", 7),
                monthly = Series("month", 12),
                yearly = Series("year", 5)
            }
        });
    }

    /* ---------- STAFF: only their own numbers ---------- */

    [Authorize(Roles = "Staff")]
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var me);

        var mine = await _db.PrintOrders.AsNoTracking().Where(o => o.AssignedStaffId == me).ToListAsync();

        return Ok(new
        {
            assigned = mine.Count,
            pending = mine.Count(o => o.OrderStatus == "Pending"),
            inProgress = mine.Count(o => o.OrderStatus == "In Progress"),
            completed = mine.Count(o => o.OrderStatus == "Completed"),
            recentCompleted = mine.Where(o => o.OrderStatus == "Completed")
                .OrderByDescending(o => o.UpdatedAt).Take(10)
                .Select(o => new { id = o.Id, customer = o.CustomerName, service = o.PrintingService, completedAt = o.UpdatedAt })
        });
    }
}
