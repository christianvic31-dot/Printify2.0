using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Printify.Backend.Data;
using Printify.Backend.Models;
using Printify.Backend.Services;

namespace Printify.Backend.Controllers;

public class InventoryRequest
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; }
    public decimal Price { get; set; }
    public decimal Threshold { get; set; }
}

public class StockRequest
{
    public string Type { get; set; }      // In / Out
    public decimal Quantity { get; set; }
    public string Note { get; set; }
}

/* ==========================================
   INVENTORY
   - Admin + Staff: view supplies (read-only for Staff)
   - Admin only: add, edit, delete, stock in/out, history
========================================== */

[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly AppDbContext _db;

    public InventoryController(AppDbContext db)
    {
        _db = db;
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var items = await _db.InventoryItems.AsNoTracking()
            .Where(i => i.ArchivedAt == null)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Name)
            .ToListAsync();

        return Ok(new { items });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] InventoryRequest r)
    {
        var error = Validate(r);
        if (error != null) return BadRequest(new { message = error });

        var id = (r.Id ?? "").Trim();

        if (id.Length == 0 || id.Length > 40)
            id = await NextId();
        else if (await _db.InventoryItems.AnyAsync(i => i.Id == id))
            id = await NextId();

        var maxOrder = await _db.InventoryItems.Select(i => (int?)i.SortOrder).MaxAsync() ?? -1;

        var item = new InventoryItem { Id = id, SortOrder = maxOrder + 1 };
        Apply(item, r);
        _db.InventoryItems.Add(item);

        if (item.Quantity > 0)
            AddTransaction(item, "Initial", item.Quantity, "Item created");

        Audit.Add(_db, User, "Inventory item created", $"{item.Id} {item.Name}");
        await _db.SaveChangesAsync();

        return StatusCode(201, item);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] InventoryRequest r)
    {
        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id);
        if (item == null || item.ArchivedAt != null) return NotFound(new { message = "Item not found." });

        var error = Validate(r);
        if (error != null) return BadRequest(new { message = error });

        var before = item.Quantity;
        Apply(item, r);

        if (item.Quantity != before)
            AddTransaction(item, "Adjustment", item.Quantity - before, "Edited from inventory page");

        Audit.Add(_db, User, "Inventory item updated", $"{item.Id} {item.Name}");
        await _db.SaveChangesAsync();

        return Ok(item);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id);
        if (item == null || item.ArchivedAt != null) return NotFound(new { message = "Item not found." });

        item.ArchivedAt = DateTime.UtcNow;
        Audit.Add(_db, User, "Inventory item archived", $"{item.Id} {item.Name}");
        await _db.SaveChangesAsync();

        return Ok(new { message = "Item archived." });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("archived")]
    public async Task<IActionResult> GetArchived()
    {
        var items = await _db.InventoryItems.AsNoTracking()
            .Where(i => i.ArchivedAt != null)
            .OrderByDescending(i => i.ArchivedAt)
            .ToListAsync();
        return Ok(new { items });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id && i.ArchivedAt != null);
        if (item == null) return NotFound(new { message = "Archived product not found." });
        item.ArchivedAt = null;
        Audit.Add(_db, User, "Inventory item restored", id);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Product restored.", item });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}/permanent")]
    public async Task<IActionResult> PermanentDelete(string id)
    {
        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id && i.ArchivedAt != null);
        if (item == null) return NotFound(new { message = "Archived product not found." });
        _db.InventoryItems.Remove(item);
        Audit.Add(_db, User, "Inventory item permanently deleted", id);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Product permanently deleted." });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/stock")]
    public async Task<IActionResult> Stock(string id, [FromBody] StockRequest r)
    {
        var type = (r?.Type ?? "").Trim();
        if (type != "In" && type != "Out")
            return BadRequest(new { message = "Type must be 'In' or 'Out'." });

        if (r.Quantity <= 0)
            return BadRequest(new { message = "Quantity must be greater than zero." });

        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id);
        if (item == null || item.ArchivedAt != null) return NotFound(new { message = "Item not found." });

        var delta = type == "In" ? r.Quantity : -r.Quantity;

        if (item.Quantity + delta < 0)
            return BadRequest(new { message = "Not enough stock for this stock-out." });

        item.Quantity += delta;
        AddTransaction(item, type, delta, (r.Note ?? "").Trim());
        Audit.Add(_db, User, "Stock " + type.ToLowerInvariant(), $"{item.Id} {item.Name} {delta:+0.##;-0.##}");
        await _db.SaveChangesAsync();

        return Ok(item);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] string itemId = null, [FromQuery] int take = 200)
    {
        take = Math.Clamp(take, 1, 1000);

        var query = _db.StockTransactions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(itemId)) query = query.Where(t => t.ItemId == itemId);

        var items = await query.OrderByDescending(t => t.CreatedAt).Take(take).ToListAsync();
        return Ok(new { items });
    }

    /* ---------- helpers ---------- */

    private void AddTransaction(InventoryItem item, string type, decimal delta, string note)
    {
        _db.StockTransactions.Add(new StockTransaction
        {
            Id = Guid.NewGuid(),
            ItemId = item.Id,
            ItemName = item.Name,
            Type = type,
            Quantity = delta,
            BalanceAfter = item.Quantity,
            Note = note ?? "",
            UserId = CurrentUserId,
            Username = User.Identity?.Name ?? "",
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task<string> NextId()
    {
        var ids = await _db.InventoryItems.AsNoTracking().Select(i => i.Id).ToListAsync();
        var max = 0;
        foreach (var id in ids)
            if (id.StartsWith("PRD-") && int.TryParse(id.Substring(4), out var n) && n > max) max = n;
        return "PRD-" + (max + 1).ToString("D3");
    }

    private static string Validate(InventoryRequest r)
    {
        if (r == null) return "Invalid item.";
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 100) return "Item name is required (max 100 characters).";
        if (r.Quantity < 0) return "Quantity cannot be negative.";
        if (r.Price < 0) return "Price cannot be negative.";
        if (r.Threshold < 0) return "Low-stock level cannot be negative.";
        return null;
    }

    private static void Apply(InventoryItem item, InventoryRequest r)
    {
        item.Name = r.Name.Trim();
        item.Category = (r.Category ?? "").Trim();
        item.Quantity = r.Quantity;
        item.Unit = (r.Unit ?? "").Trim();
        item.Price = r.Price;
        item.Threshold = r.Threshold;
    }
}
