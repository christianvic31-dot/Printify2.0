using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Printify.Backend.Data;

namespace Printify.Backend.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Roles = "Admin")]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 200)
    {
        take = Math.Clamp(take, 1, 1000);

        var items = await _db.ActivityLogs.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt).Take(take)
            .Select(a => new { a.Id, a.Username, a.Role, a.Action, a.Details, a.CreatedAt })
            .ToListAsync();

        return Ok(new { items });
    }
}
