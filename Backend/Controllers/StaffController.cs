using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Printify.Backend.Data;
using Printify.Backend.Models;
using Printify.Backend.Services;

namespace Printify.Backend.Controllers;

public class StaffRequest
{
    public string Name { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string Phone { get; set; }
    public string Position { get; set; }
    public string Address { get; set; }
    public string Status { get; set; }
}

/// <summary>Staff ID read from a staff attendance QR code.</summary>
public sealed record ScanAttendanceRequest
{
    public string StaffCode { get; init; } = "";
}

/// <summary>Verified staff attendance recorded from a QR scan.</summary>
public sealed record ScanAttendanceResponse(
    long Id,
    string StaffId,
    string StaffName,
    string Date,
    string TimeIn,
    string TimeOut,
    string Status,
    string Action);

/* ==========================================
   STAFF ACCOUNTS - Admin lang
   Ang Admin ang gumagawa ng login ng bawat Staff
   at nakikita niya kung ilang orders ang natanggap ng bawat isa.
========================================== */

[ApiController]
[Route("api/staff")]
[Authorize(Roles = "Admin")]
public class StaffController : ControllerBase
{
    private static readonly string[] AllowedStatuses = { "Active", "On Leave", "Inactive" };

    private readonly AppDbContext _db;

    public StaffController(AppDbContext db)
    {
        _db = db;
    }

    /* ---------- LIST (kasama ang bilang ng orders) ---------- */

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var staff = await _db.Users.AsNoTracking()
            .Where(u => u.Role == "Staff")
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();

        var counts = await _db.PrintOrders.AsNoTracking()
            .Where(o => o.AssignedStaffId != null)
            .GroupBy(o => new { o.AssignedStaffId, o.OrderStatus })
            .Select(g => new { g.Key.AssignedStaffId, g.Key.OrderStatus, Count = g.Count() })
            .ToListAsync();

        var items = staff.Select(user =>
        {
            var mine = counts.Where(c => c.AssignedStaffId == user.Id).ToList();

            int Of(string status) => mine.Where(c => c.OrderStatus == status).Sum(c => c.Count);

            return ToDto(user, new
            {
                total = mine.Sum(c => c.Count),
                pending = Of("Pending"),
                inProgress = Of("In Progress"),
                completed = Of("Completed"),
                cancelled = Of("Cancelled")
            });
        });

        return Ok(new { items });
    }

    /* ---------- VERIFY QR AND RECORD ATTENDANCE ---------- */

    [HttpPost("attendance/scan")]
    public async Task<IActionResult> ScanAttendance(
        [FromBody] ScanAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        var staffCode = (request?.StaffCode ?? "").Trim().ToUpperInvariant();

        if (staffCode.Length < 7 ||
            !staffCode.StartsWith("STF-", StringComparison.Ordinal) ||
            staffCode.Skip(4).Any(character => character < '0' || character > '9'))
        {
            return BadRequest(new { message = "Enter a valid Staff ID QR code." });
        }

        var user = await _db.Users.FirstOrDefaultAsync(
            item => item.Role == "Staff" && item.StaffCode == staffCode,
            cancellationToken);

        if (user == null)
            return NotFound(new { message = "The scanned Staff ID was not found." });

        if (string.Equals(user.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "This staff account is inactive." });

        var now = DateTime.Now;
        var date = now.ToString("yyyy-MM-dd");
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(
            item => item.StaffUserId == user.Id && item.Date == date,
            cancellationToken);

        string action;

        if (record == null)
        {
            record = new AttendanceRecord
            {
                Id = DateTime.UtcNow.Ticks,
                StaffUserId = user.Id,
                Name = user.FullName,
                Date = date,
                Time = now.ToString("HH:mm"),
                TimeOut = "",
                Status = now.TimeOfDay > new TimeSpan(8, 0, 0) ? "Late" : "Working",
                Notes = ""
            };

            _db.AttendanceRecords.Add(record);
            action = "Clock In";
        }
        else if (string.IsNullOrEmpty(record.TimeOut))
        {
            record.TimeOut = now.ToString("HH:mm");
            record.Status = "Completed";
            action = "Clock Out";
        }
        else
        {
            return Conflict(new { message = "Attendance is already completed for today." });
        }

        var collectionState = await _db.CollectionStates.FindAsync(
            new object[] { "attendance" },
            cancellationToken);

        if (collectionState == null)
        {
            _db.CollectionStates.Add(new CollectionState
            {
                Name = "attendance",
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            collectionState.UpdatedAt = DateTime.UtcNow;
        }

        Audit.Add(_db, User, "Staff attendance recorded", $"{staffCode} {action} {date}");
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new ScanAttendanceResponse(
            record.Id,
            staffCode,
            user.FullName,
            record.Date,
            record.Time,
            record.TimeOut ?? "",
            record.Status,
            action));
    }

    /* ---------- CREATE ---------- */

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] StaffRequest request)
    {
        var errors = Validate(request, requirePassword: true);

        var username = (request?.Username ?? "").Trim();
        var email = (request?.Email ?? "").Trim().ToLowerInvariant();

        if (errors.Count == 0)
        {
            var usernameLower = username.ToLowerInvariant();

            if (await _db.Users.AnyAsync(u => u.Username.ToLower() == usernameLower))
                errors["username"] = "That username is already taken.";

            if (await _db.Users.AnyAsync(u => u.Email == email))
                errors["email"] = "That email is already registered.";
        }

        if (errors.Count > 0)
            return BadRequest(new { message = errors.Values.First(), errors });

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.Name.Trim(),
            Username = username,
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            Role = "Staff",
            CreatedAt = DateTime.UtcNow,
            StaffCode = DbBootstrap.NextStaffCode(_db),
            Phone = (request.Phone ?? "").Trim(),
            Position = (request.Position ?? "").Trim(),
            Address = (request.Address ?? "").Trim(),
            Status = NormalizeStatus(request.Status)
        };

        _db.Users.Add(user);
        Audit.Add(_db, User, "Staff account created", $"{user.StaffCode} {user.Username}");

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "That username or email is already registered." });
        }

        return StatusCode(201, ToDto(user, EmptyCounts()));
    }

    /* ---------- UPDATE ---------- */

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] StaffRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == "Staff");

        if (user == null)
            return NotFound(new { message = "Staff account not found." });

        var changingPassword = !string.IsNullOrEmpty(request?.Password);

        // Sa pag-edit, hindi na kailangan ang username at password.
        var errors = Validate(request, requirePassword: false, requireUsername: false);

        if (changingPassword && !AuthController.IsStrongPassword(request.Password))
            errors["password"] = "The password does not meet all the requirements.";

        var email = (request?.Email ?? "").Trim().ToLowerInvariant();

        if (errors.Count == 0 && await _db.Users.AnyAsync(u => u.Email == email && u.Id != id))
            errors["email"] = "That email is already registered.";

        if (errors.Count > 0)
            return BadRequest(new { message = errors.Values.First(), errors });

        user.FullName = request.Name.Trim();
        user.Email = email;
        user.Phone = (request.Phone ?? "").Trim();
        user.Position = (request.Position ?? "").Trim();
        user.Address = (request.Address ?? "").Trim();
        user.Status = NormalizeStatus(request.Status);

        if (changingPassword)
            user.PasswordHash = PasswordHasher.Hash(request.Password);

        Audit.Add(_db, User, "Staff account updated", $"{user.StaffCode} {user.Username} ({user.Status})");
        await _db.SaveChangesAsync();

        return Ok(ToDto(user, await CountsFor(id)));
    }

    /* ---------- DELETE ---------- */

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == "Staff");

        if (user == null)
            return NotFound(new { message = "Staff account not found." });

        // Ang mga order niya ay hindi buburahin; mawawalan lang sila ng naka-assign.
        await _db.PrintOrders
            .Where(o => o.AssignedStaffId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.AssignedStaffId, (Guid?)null));

        _db.Users.Remove(user);
        Audit.Add(_db, User, "Staff account deleted", $"{user.StaffCode} {user.Username}");
        await _db.SaveChangesAsync();

        return Ok(new { message = "Staff account deleted." });
    }

    /* ---------- HELPERS ---------- */

    private static object EmptyCounts() => new { total = 0, pending = 0, inProgress = 0, completed = 0, cancelled = 0 };

    private async Task<object> CountsFor(Guid staffId)
    {
        var rows = await _db.PrintOrders.AsNoTracking()
            .Where(o => o.AssignedStaffId == staffId)
            .GroupBy(o => o.OrderStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int Of(string status) => rows.Where(r => r.Status == status).Sum(r => r.Count);

        return new
        {
            total = rows.Sum(r => r.Count),
            pending = Of("Pending"),
            inProgress = Of("In Progress"),
            completed = Of("Completed"),
            cancelled = Of("Cancelled")
        };
    }

    private static object ToDto(User user, object orders) => new
    {
        id = user.StaffCode,
        userId = user.Id,
        name = user.FullName,
        username = user.Username,
        email = user.Email,
        phone = user.Phone ?? "",
        position = user.Position ?? "",
        address = user.Address ?? "",
        dateAdded = user.CreatedAt.ToString("yyyy-MM-dd"),
        status = user.Status ?? "Active",
        orders
    };

    private static string NormalizeStatus(string status)
    {
        return AllowedStatuses.FirstOrDefault(s => s.Equals((status ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Active";
    }

    private static Dictionary<string, string> Validate(StaffRequest request, bool requirePassword, bool requireUsername = true)
    {
        var errors = new Dictionary<string, string>();

        var name = (request?.Name ?? "").Trim();
        var username = (request?.Username ?? "").Trim();
        var email = (request?.Email ?? "").Trim();

        if (name.Length == 0 || name.Length > 100 || !AuthController.NamePattern.IsMatch(name))
            errors["name"] = "Please enter a valid name.";

        if (requireUsername && !AuthController.UsernamePattern.IsMatch(username))
            errors["username"] = "Username must be 4-20 characters using letters, numbers, or underscores.";

        if (email.Length > 254 || !AuthController.EmailPattern.IsMatch(email))
            errors["email"] = "Please enter a valid email address.";

        if (requirePassword && !AuthController.IsStrongPassword(request?.Password))
            errors["password"] = "The password must be 8+ characters with uppercase, lowercase, a number and a symbol.";

        return errors;
    }
}
