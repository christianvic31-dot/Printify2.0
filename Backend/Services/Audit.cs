using System.Security.Claims;
using Printify.Backend.Data;
using Printify.Backend.Models;

namespace Printify.Backend.Services;

// Adds an audit row to the current DbContext. The caller's SaveChanges persists it
// in the same transaction as the change being logged.
public static class Audit
{
    public static void Add(AppDbContext db, ClaimsPrincipal user, string action, string details = "")
    {
        Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id);
        Add(db, id == Guid.Empty ? null : id, user?.Identity?.Name ?? "", user?.FindFirstValue(ClaimTypes.Role) ?? "", action, details);
    }

    public static void Add(AppDbContext db, Guid? userId, string username, string role, string action, string details = "")
    {
        db.ActivityLogs.Add(new ActivityLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Username = username ?? "",
            Role = role ?? "",
            Action = action,
            Details = details ?? "",
            CreatedAt = DateTime.UtcNow
        });
    }
}
