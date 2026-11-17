using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Printify.Backend.Data;
using Printify.Backend.Models;
using Printify.Backend.Services;

namespace Printify.Backend.Controllers;

public class OrderRequest
{
    public string Id { get; set; }
    public string CustomerName { get; set; }
    public string CustomerContact { get; set; }
    public string PrintingService { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal AmountPaid { get; set; }
    public string OrderDate { get; set; }
    public string DueDate { get; set; }
    public string OrderStatus { get; set; }
    public string PaymentStatus { get; set; }
    public string Notes { get; set; }
    public Guid? AssignedStaffId { get; set; }
}

public class StatusRequest
{
    public string Status { get; set; }
}

/* ==========================================
   ORDERS
   - Admin: nakikita at ina-assign ang lahat ng orders
   - Staff: ang sariling naka-assign na orders lang,
            at status lang ang puwede nilang baguhin
========================================== */

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private static long _notificationId = DateTime.UtcNow.Ticks;
    private static readonly string[] AllStatuses = { "Pending", "In Progress", "Completed", "Cancelled" };
    private static readonly string[] StaffStatuses = { "Pending", "In Progress", "Completed" };
    private static readonly string[] PaymentStatuses = { "Paid", "Unpaid", "Partially Paid" };

    private readonly AppDbContext _db;

    public OrdersController(AppDbContext db)
    {
        _db = db;
    }

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    /* ---------- ADMIN: LAHAT NG ORDERS ---------- */

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var orders = await _db.PrintOrders.AsNoTracking()
            .Where(o => o.ArchivedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var names = await StaffNames();

        return Ok(new { items = orders.Select(o => ToDto(o, names)) });
    }

    /* ---------- STAFF: ANG SARILING ORDERS LANG ---------- */

    [Authorize(Roles = "Staff")]
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        var me = CurrentUserId;

        var orders = await _db.PrintOrders.AsNoTracking()
            .Where(o => o.AssignedStaffId == me)
            .Where(o => o.ArchivedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var names = await StaffNames();

        return Ok(new { items = orders.Select(o => ToDto(o, names)) });
    }

    /* ---------- ADMIN: CREATE / UPDATE / DELETE ---------- */

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] OrderRequest request)
    {
        var error = await Validate(request);

        if (error != null)
            return BadRequest(new { message = error });

        if (await _db.PrintOrders.AnyAsync(o => o.Id == request.Id))
            return Conflict(new { message = "An order with this ID already exists." });

        if (request.AssignedStaffId == null)
        {
            request.AssignedStaffId = await NextActiveStaffId();

            if (request.AssignedStaffId == null)
                return Conflict(new { message = "No active staff member is available to receive this order." });
        }

        var order = new PrintOrder { Id = request.Id.Trim(), CreatedAt = DateTime.UtcNow };

        Apply(order, request);
        AddAssignmentNotification(order);
        await MarkNotificationsSaved();
        _db.PrintOrders.Add(order);
        Audit.Add(_db, User, "Order created", order.Id);
        await _db.SaveChangesAsync();

        return StatusCode(201, ToDto(order, await StaffNames()));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] OrderRequest request)
    {
        var order = await _db.PrintOrders.FirstOrDefaultAsync(o => o.Id == id);

        if (order == null || order.ArchivedAt != null)
            return NotFound(new { message = "Order not found." });

        if (request == null)
            return BadRequest(new { message = "Invalid order." });

        request.Id = id;

        var error = await Validate(request);

        if (error != null)
            return BadRequest(new { message = error });

        var previousAssignedStaffId = order.AssignedStaffId;

        if (request.AssignedStaffId == null)
        {
            request.AssignedStaffId = await NextActiveStaffId();

            if (request.AssignedStaffId == null)
                return Conflict(new { message = "No active staff member is available to receive this order." });
        }

        Apply(order, request);
        if (order.AssignedStaffId != previousAssignedStaffId && order.AssignedStaffId != null)
        {
            AddAssignmentNotification(order);
            await MarkNotificationsSaved();
        }

        Audit.Add(_db, User, "Order updated", order.Id);
        await _db.SaveChangesAsync();

        return Ok(ToDto(order, await StaffNames()));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var order = await _db.PrintOrders.FirstOrDefaultAsync(o => o.Id == id);

        if (order == null || order.ArchivedAt != null)
            return NotFound(new { message = "Order not found." });

        order.ArchivedAt = DateTime.UtcNow;
        Audit.Add(_db, User, "Order archived", id);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Order archived." });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("archived")]
    public async Task<IActionResult> GetArchived()
    {
        var orders = await _db.PrintOrders.AsNoTracking()
            .Where(o => o.ArchivedAt != null)
            .OrderByDescending(o => o.ArchivedAt)
            .ToListAsync();
        return Ok(new { items = orders.Select(o => ToDto(o, new Dictionary<Guid, string>())) });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var order = await _db.PrintOrders.FirstOrDefaultAsync(o => o.Id == id && o.ArchivedAt != null);
        if (order == null) return NotFound(new { message = "Archived order not found." });
        order.ArchivedAt = null;
        Audit.Add(_db, User, "Order restored", id);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Order restored.", order = ToDto(order, await StaffNames()) });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}/permanent")]
    public async Task<IActionResult> PermanentDelete(string id)
    {
        var order = await _db.PrintOrders.FirstOrDefaultAsync(o => o.Id == id && o.ArchivedAt != null);
        if (order == null) return NotFound(new { message = "Archived order not found." });
        _db.PrintOrders.Remove(order);
        Audit.Add(_db, User, "Order permanently deleted", id);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Order permanently deleted." });
    }

    /* ---------- STATUS (Staff: sariling order lang) ---------- */

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] StatusRequest request)
    {
        var isAdmin = User.IsInRole("Admin");
        var allowed = isAdmin ? AllStatuses : StaffStatuses;
        var status = allowed.FirstOrDefault(s => s.Equals((request?.Status ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        if (status == null)
            return BadRequest(new { message = "Invalid status." });

        var order = await _db.PrintOrders.FirstOrDefaultAsync(o => o.Id == id);

        // Parehong 404 kapag hindi niya order, para hindi malaman kung may ganoong ID.
        if (order == null || order.ArchivedAt != null || (!isAdmin && order.AssignedStaffId != CurrentUserId))
            return NotFound(new { message = "Order not found." });

        if (!isAdmin && order.OrderStatus == "Cancelled")
            return StatusCode(403, new { message = "This order was cancelled by the admin." });

        order.OrderStatus = status;
        order.UpdatedAt = DateTime.UtcNow;
        Audit.Add(_db, User, "Order status", $"{order.Id} -> {status}");
        await _db.SaveChangesAsync();

        return Ok(ToDto(order, await StaffNames()));
    }

    /* ---------- HELPERS ---------- */

    private async Task<Guid?> NextActiveStaffId()
    {
        var activeStaffIds = await _db.Users.AsNoTracking()
            .Where(u => u.Role == "Staff" && u.Status == "Active")
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Select(u => u.Id)
            .ToListAsync();

        if (activeStaffIds.Count == 0)
            return null;

        var lastAssignedStaffId = await _db.PrintOrders.AsNoTracking()
            .Where(o => o.AssignedStaffId != null)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Select(o => o.AssignedStaffId)
            .FirstOrDefaultAsync();

        var lastIndex = activeStaffIds.FindIndex(id => id == lastAssignedStaffId);
        return activeStaffIds[(lastIndex + 1) % activeStaffIds.Count];
    }

    private void AddAssignmentNotification(PrintOrder order)
    {
        if (order.AssignedStaffId == null)
            return;

        var now = DateTime.UtcNow;
        _db.Notifications.Add(new NotificationItem
        {
            Id = Interlocked.Increment(ref _notificationId),
            RecipientUserId = order.AssignedStaffId,
            Title = "New order assigned",
            Message = $"Order {order.Id} from {order.CustomerName} was assigned to you.",
            Time = now.ToString("O"),
            Read = false
        });
    }

    private async Task MarkNotificationsSaved()
    {
        var state = await _db.CollectionStates.FindAsync("notifications");

        if (state == null)
            _db.CollectionStates.Add(new CollectionState { Name = "notifications", UpdatedAt = DateTime.UtcNow });
        else
            state.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<Dictionary<Guid, string>> StaffNames()
    {
        return await _db.Users.AsNoTracking()
            .Where(u => u.Role == "Staff")
            .ToDictionaryAsync(u => u.Id, u => u.FullName);
    }

    private async Task<string> Validate(OrderRequest r)
    {
        if (r == null) return "Invalid order.";

        if (string.IsNullOrWhiteSpace(r.Id) || r.Id.Trim().Length > 40)
            return "Order ID is required.";

        if (string.IsNullOrWhiteSpace(r.CustomerName) || r.CustomerName.Length > 100)
            return "Customer name is required.";

        if (string.IsNullOrWhiteSpace(r.PrintingService))
            return "Printing service is required.";

        if (r.Quantity <= 0 || r.UnitPrice < 0 || r.AmountPaid < 0)
            return "Quantity, price and amount paid must be valid numbers.";

        if (!AllStatuses.Contains(r.OrderStatus ?? ""))
            return "Invalid order status.";

        if (!PaymentStatuses.Contains(r.PaymentStatus ?? ""))
            return "Invalid payment status.";

        if (r.AssignedStaffId != null)
        {
            var staff = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == r.AssignedStaffId && u.Role == "Staff");

            if (staff == null)
                return "The selected staff member does not exist.";

            if (staff.Status == "Inactive")
                return "The selected staff member is inactive.";
        }

        return null;
    }

    private static void Apply(PrintOrder order, OrderRequest r)
    {
        order.CustomerName = r.CustomerName.Trim();
        order.CustomerContact = (r.CustomerContact ?? "").Trim();
        order.PrintingService = r.PrintingService.Trim();
        order.Quantity = r.Quantity;
        order.UnitPrice = r.UnitPrice;
        order.Total = Math.Round(r.Quantity * r.UnitPrice, 2);
        order.AmountPaid = r.AmountPaid;
        order.OrderDate = r.OrderDate ?? "";
        order.DueDate = r.DueDate ?? "";
        order.OrderStatus = r.OrderStatus;
        order.PaymentStatus = r.PaymentStatus;
        order.Notes = r.Notes ?? "";
        order.AssignedStaffId = r.AssignedStaffId;
        order.UpdatedAt = DateTime.UtcNow;
    }

    private static object ToDto(PrintOrder o, Dictionary<Guid, string> names) => new
    {
        id = o.Id,
        customerName = o.CustomerName,
        customerContact = o.CustomerContact,
        printingService = o.PrintingService,
        quantity = o.Quantity,
        unitPrice = o.UnitPrice,
        total = o.Total,
        amountPaid = o.AmountPaid,
        orderDate = o.OrderDate,
        dueDate = o.DueDate,
        orderStatus = o.OrderStatus,
        paymentStatus = o.PaymentStatus,
        notes = o.Notes,
        assignedStaffId = o.AssignedStaffId,
        assignedStaffName = o.AssignedStaffId != null && names.TryGetValue(o.AssignedStaffId.Value, out var n) ? n : "",
        createdAt = o.CreatedAt,
        archivedAt = o.ArchivedAt,
        updatedAt = o.UpdatedAt
    };
}
