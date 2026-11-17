using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Printify.Backend.Data;
using Printify.Backend.Models;
using Printify.Backend.Services;

namespace Printify.Backend.Controllers;

public class StaffProfileRequest
{
    public string FullName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Address { get; set; }
}

/// <summary>Staff code encoded in the staff member's attendance QR link.</summary>
public sealed record ScanOwnAttendanceRequest
{
    public string StaffCode { get; init; } = "";
}

[ApiController]
[Route("api/staff/me")]
[Authorize(Roles = "Staff")]
public class StaffSelfController : ControllerBase
{
    private readonly AppDbContext _db;

    public StaffSelfController(AppDbContext db)
    {
        _db = db;
    }

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var user = await CurrentStaff();
        return user == null ? Unauthorized(new { message = "Not logged in." }) : Ok(ProfileDto(user));
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] StaffProfileRequest request)
    {
        var user = await CurrentStaff();

        if (user == null)
            return Unauthorized(new { message = "Not logged in." });

        var fullName = (request?.FullName ?? "").Trim();
        var email = (request?.Email ?? "").Trim().ToLowerInvariant();
        var phone = (request?.Phone ?? "").Trim();
        var address = (request?.Address ?? "").Trim();

        if (fullName.Length == 0 || fullName.Length > 100 || !AuthController.NamePattern.IsMatch(fullName))
            return BadRequest(new { message = "Please enter a valid name." });

        if (email.Length > 254 || !AuthController.EmailPattern.IsMatch(email))
            return BadRequest(new { message = "Please enter a valid email address." });

        if (phone.Length > 32 || address.Length > 250)
            return BadRequest(new { message = "Phone or address is too long." });

        if (await _db.Users.AnyAsync(u => u.Id != user.Id && u.Email == email))
            return Conflict(new { message = "That email is already registered." });

        user.FullName = fullName;
        user.Email = email;
        user.Phone = phone;
        user.Address = address;
        Audit.Add(_db, User, "Staff profile updated", user.Username);
        await _db.SaveChangesAsync();

        return Ok(ProfileDto(user));
    }

    [HttpGet("attendance")]
    public async Task<IActionResult> GetAttendance()
    {
        var records = await _db.AttendanceRecords.AsNoTracking()
            .Where(item => item.StaffUserId == CurrentUserId)
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.Id)
            .ToListAsync();

        return Ok(new { items = records.Select(AttendanceDto) });
    }

    [HttpPost("attendance/clock-in")]
    public async Task<IActionResult> ClockIn()
    {
        var user = await CurrentStaff();

        if (user == null)
            return Unauthorized(new { message = "Not logged in." });

        var now = DateTime.Now;
        var date = now.ToString("yyyy-MM-dd");
        var existing = await _db.AttendanceRecords
            .FirstOrDefaultAsync(item => item.StaffUserId == user.Id && item.Date == date);

        if (existing != null)
            return Conflict(new { message = "You already have an attendance record for today." });

        var record = new AttendanceRecord
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
        await MarkCollectionSaved("attendance");
        Audit.Add(_db, User, "Staff clocked in", date + " " + record.Time);
        await _db.SaveChangesAsync();

        return Ok(AttendanceDto(record));
    }

    [HttpPost("attendance/qr-scan")]
    public async Task<IActionResult> ClockInFromQr([FromBody] ScanOwnAttendanceRequest request)
    {
        var user = await CurrentStaff();
        var scannedStaffCode = (request?.StaffCode ?? "").Trim();

        if (user == null)
            return Unauthorized(new { message = "Sign in to your staff account on this phone first." });

        if (!string.Equals(user.StaffCode, scannedStaffCode, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        return await ClockIn();
    }

    [HttpPost("attendance/clock-out")]
    public async Task<IActionResult> ClockOut()
    {
        var user = await CurrentStaff();

        if (user == null)
            return Unauthorized(new { message = "Not logged in." });

        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var record = await _db.AttendanceRecords
            .Where(item => item.StaffUserId == user.Id && item.Date == date && item.TimeOut == "")
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync();

        if (record == null)
            return Conflict(new { message = "Clock in before clocking out." });

        record.TimeOut = DateTime.Now.ToString("HH:mm");
        record.Status = "Completed";
        await MarkCollectionSaved("attendance");
        Audit.Add(_db, User, "Staff clocked out", date + " " + record.TimeOut);
        await _db.SaveChangesAsync();

        return Ok(AttendanceDto(record));
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications()
    {
        var items = await _db.Notifications.AsNoTracking()
            .Where(item => item.RecipientUserId == CurrentUserId)
            .OrderByDescending(item => item.Id)
            .Take(100)
            .Select(item => new
            {
                id = item.Id,
                title = item.Title,
                message = item.Message,
                time = item.Time,
                read = item.Read
            })
            .ToListAsync();

        return Ok(new { items });
    }

    [HttpPatch("notifications/{id:long}/read")]
    public async Task<IActionResult> MarkNotificationRead(long id)
    {
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(item => item.Id == id && item.RecipientUserId == CurrentUserId);

        if (notification == null)
            return NotFound(new { message = "Notification not found." });

        notification.Read = true;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Notification marked as read." });
    }

    private Task<User> CurrentStaff()
    {
        return _db.Users.FirstOrDefaultAsync(user =>
            user.Id == CurrentUserId && user.Role == "Staff");
    }

    private async Task MarkCollectionSaved(string name)
    {
        var state = await _db.CollectionStates.FindAsync(name);

        if (state == null)
            _db.CollectionStates.Add(new CollectionState { Name = name, UpdatedAt = DateTime.UtcNow });
        else
            state.UpdatedAt = DateTime.UtcNow;
    }

    private static object ProfileDto(User user) => new
    {
        id = user.Id,
        fullName = user.FullName,
        email = user.Email,
        username = user.Username,
        role = user.Role,
        staffCode = user.StaffCode,
        phone = user.Phone ?? "",
        position = user.Position ?? "",
        address = user.Address ?? ""
    };

    private static object AttendanceDto(AttendanceRecord item) => new
    {
        id = item.Id,
        date = item.Date,
        timeIn = item.Time,
        timeOut = item.TimeOut ?? "",
        status = item.Status
    };
}
