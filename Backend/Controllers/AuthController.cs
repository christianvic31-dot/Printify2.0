using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Printify.Backend.Data;
using Printify.Backend.Models;
using Printify.Backend.Services;

namespace Printify.Backend.Controllers;

public class RegisterRequest
{
    public string FullName { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string SetupToken { get; set; }
}

public class VerifyEmailRequest
{
    public string Email { get; set; } = "";
    public string Code { get; set; } = "";
}

public class LoginRequest
{
    public string Username { get; set; }
    public string Password { get; set; }
    public bool RememberMe { get; set; }
}

public class DeleteAdminAccountRequest
{
    public string Password { get; set; } = "";
    public string Confirmation { get; set; } = "";
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; }
    public string NewPassword { get; set; }
}

public class ForgotPasswordRequest
{
    public string Email { get; set; }
}

public class ResetPasswordRequest
{
    public string Token { get; set; }
    public string Password { get; set; }
}

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    internal static readonly Regex NamePattern = new(@"^\p{L}[\p{L}\s.'-]*$", RegexOptions.Compiled);
    internal static readonly Regex UsernamePattern = new(@"^[a-zA-Z0-9_]{4,20}$", RegexOptions.Compiled);
    internal static readonly Regex EmailPattern = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    // Ginagamit para laging may hash na ive-verify kahit walang nahanap na user (timing attack).
    private static readonly string DummyHash = PasswordHasher.Hash("dummy-password-for-timing");

    private readonly AppDbContext _db;
    private readonly ILogger<AuthController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public AuthController(
        AppDbContext db,
        ILogger<AuthController> logger,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _db = db;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
    }

    internal static bool IsStrongPassword(string value)
    {
        return !string.IsNullOrEmpty(value)
            && value.Length >= 8
            && value.Length <= 128
            && value.Any(char.IsUpper)
            && value.Any(char.IsLower)
            && value.Any(char.IsDigit)
            && value.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c))
            && !value.Any(char.IsWhiteSpace);
    }

    private static object PublicUser(User user) => new
    {
        id = user.Id,
        fullName = user.FullName,
        username = user.Username,
        email = user.Email,
        role = user.Role,
        staffCode = user.StaffCode,
        phone = user.Phone,
        position = user.Position,
        address = user.Address
    };

    [HttpGet("admin-registration-status")]
    public async Task<IActionResult> AdminRegistrationStatus()
    {
        var available = !await _db.Users.AsNoTracking().AnyAsync(u => u.Role == "Admin");
        return Ok(new { available });
    }

    /* ---------- REGISTER (UNANG ADMIN LANG) ---------- */

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!_environment.IsDevelopment() && !_environment.IsProduction())
        {
            return NotFound(new { message = "Admin setup is unavailable in this environment." });
        }

        if (!IsValidSetupToken(request?.SetupToken))
            return StatusCode(403, new { message = "Initial Admin setup is disabled or the setup key is invalid." });

        // Sarado na ang pag-register kapag may Admin na. Ang Admin na ang gumagawa ng Staff accounts.
        if (await _db.Users.AnyAsync(u => u.Role == "Admin"))
            return StatusCode(403, new { message = "An Admin account already exists. Registration is closed." });

        var fullName = (request?.FullName ?? "").Trim();
        var username = (request?.Username ?? "").Trim();
        var email = (request?.Email ?? "").Trim().ToLowerInvariant();
        var password = request?.Password;

        var errors = new Dictionary<string, string>();

        if (fullName.Length == 0 || fullName.Length > 100 || !NamePattern.IsMatch(fullName))
            errors["fullName"] = "Please enter a valid name.";

        if (!UsernamePattern.IsMatch(username))
            errors["username"] = "Username must be 4-20 characters using letters, numbers, or underscores.";

        if (email.Length > 254 || !EmailPattern.IsMatch(email))
            errors["email"] = "Please enter a valid email address.";

        if (!IsStrongPassword(password))
            errors["password"] = "Your password does not meet all the requirements.";

        if (errors.Count > 0)
            return BadRequest(new { message = "Please check the required fields.", errors });

        var usernameLower = username.ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Username.ToLower() == usernameLower))
            return Conflict(new { message = "That username is already taken.", errors = new { username = "That username is already taken." } });

        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "That email is already registered.", errors = new { email = "That email is already registered." } });

        if (string.IsNullOrWhiteSpace(_configuration["Smtp:Username"]) ||
            string.IsNullOrWhiteSpace(_configuration["Smtp:AppPassword"]) ||
            string.IsNullOrWhiteSpace(_configuration["Smtp:FromEmail"]))
            return StatusCode(503, new { message = "Email verification is not configured yet. Set SMTP environment variables in Render." });

        var now = DateTime.UtcNow;
        var pending = await _db.PendingAdminRegistrations.FirstOrDefaultAsync(p => p.Email == email);
        if (pending != null && pending.LastSentAt > now.AddMinutes(-1))
            return StatusCode(429, new { message = "Please wait one minute before requesting another code." });

        var code = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");
        if (pending == null)
        {
            pending = new PendingAdminRegistration { Id = Guid.NewGuid(), Email = email };
            _db.PendingAdminRegistrations.Add(pending);
        }
        pending.FullName = fullName;
        pending.Username = username;
        pending.PasswordHash = PasswordHasher.Hash(password);
        pending.CodeHash = HashToken(code);
        pending.ExpiresAt = now.AddMinutes(10);
        pending.LastSentAt = now;
        pending.FailedAttempts = 0;

        try
        {
            await SendVerificationEmail(email, code);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to send registration verification email.");
            return StatusCode(503, new { message = "Unable to send verification email. Check Gmail SMTP settings and try again." });
        }

        return Ok(new { message = "A 6-digit verification code was sent to your email. It expires in 10 minutes.", verificationRequired = true });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        var email = (request?.Email ?? "").Trim().ToLowerInvariant();
        var code = (request?.Code ?? "").Trim();
        if (!EmailPattern.IsMatch(email) || !Regex.IsMatch(code, @"^\d{6}$"))
            return BadRequest(new { message = "Enter your email and 6-digit verification code." });

        var pending = await _db.PendingAdminRegistrations.FirstOrDefaultAsync(p => p.Email == email);
        if (pending == null || pending.ExpiresAt <= DateTime.UtcNow || pending.FailedAttempts >= 5)
            return BadRequest(new { message = "Code expired or too many attempts. Register again to request a new code." });

        var expected = Convert.FromHexString(pending.CodeHash);
        var provided = Convert.FromHexString(HashToken(code));
        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
        {
            pending.FailedAttempts++;
            await _db.SaveChangesAsync();
            return BadRequest(new { message = "Incorrect verification code." });
        }

        if (await _db.Users.AnyAsync(u => u.Role == "Admin" || u.Email == email || u.Username.ToLower() == pending.Username.ToLower()))
            return Conflict(new { message = "Admin registration is closed or the username/email is already used." });

        var user = new User
        {
            Id = Guid.NewGuid(), FullName = pending.FullName, Username = pending.Username,
            Email = pending.Email, PasswordHash = pending.PasswordHash,
            Role = "Admin", CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        _db.PendingAdminRegistrations.Remove(pending);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { message = "An Admin account already exists or the username/email is taken." }); }
        return Ok(new { message = "Email verified. Admin account created. You can now log in." });
    }

    private async Task SendVerificationEmail(string recipient, string code)
    {
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:AppPassword"];
        var sender = _configuration["Smtp:FromEmail"];
        var host = _configuration["Smtp:Host"] ?? "smtp.gmail.com";
        var port = int.TryParse(_configuration["Smtp:Port"], out var parsed) ? parsed : 587;
        using var message = new MailMessage(sender!, recipient)
        {
            Subject = "Printify - Verify your email",
            Body = $"Your Printify verification code is: {code}\\nThis code expires in 10 minutes. Do not share it."
        };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000
        };
        await client.SendMailAsync(message);
    }

    private bool IsValidSetupToken(string providedToken)
    {
        var configuredToken = _configuration["AdminSetup:InitialAdminToken"];

        if (string.IsNullOrEmpty(configuredToken) ||
            configuredToken.Length < 32 ||
            string.IsNullOrEmpty(providedToken) ||
            providedToken.Length > 256)
        {
            return false;
        }

        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredToken));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedToken));
        return CryptographicOperations.FixedTimeEquals(configuredHash, providedHash);
    }

    /* ---------- LOGIN ---------- */

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var identifier = (request?.Username ?? "").Trim().ToLowerInvariant();
        var password = request?.Password ?? "";

        if (identifier.Length == 0 || password.Length == 0)
            return BadRequest(new { message = "Please enter your username and password." });

        var lockKey = HttpContext.Connection.RemoteIpAddress + "|" + identifier;

        if (LoginThrottle.IsLocked(lockKey))
            return StatusCode(429, new { message = "Too many failed attempts. Please try again in 15 minutes." });

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Username.ToLower() == identifier || u.Email == identifier);

        var valid = PasswordHasher.Verify(password, user?.PasswordHash ?? DummyHash) && user != null;

        if (!valid)
        {
            LoginThrottle.RecordFailure(lockKey);
            return Unauthorized(new { message = "Invalid username or password." });
        }

        if (user.Role == "Staff" && user.Status == "Inactive")
            return StatusCode(403, new { message = "This account is inactive. Please contact the administrator." });

        LoginThrottle.Reset(lockKey);
        Audit.Add(_db, user.Id, user.Username, user.Role, "Login", "");
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        var properties = new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(request.RememberMe ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8))
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);

        return Ok(new
        {
            message = "Login successful.",
            id = user.Id,
            fullName = user.FullName,
            username = user.Username,
            email = user.Email,
            role = user.Role
        });
    }

    /* ---------- LOGOUT ---------- */

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            Audit.Add(_db, User, "Logout", "");
            await _db.SaveChangesAsync();
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Logged out." });
    }

    /* ---------- CURRENT USER ---------- */

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return Unauthorized(new { message = "Not logged in." });

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Unauthorized(new { message = "Not logged in." });
        }

        return Ok(PublicUser(user));
    }

    /* ---------- CHANGE PASSWORD ---------- */

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return Unauthorized(new { message = "Not logged in." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return Unauthorized(new { message = "Not logged in." });

        if (!PasswordHasher.Verify(request?.CurrentPassword ?? "", user.PasswordHash))
            return Unauthorized(new { message = "Your current password is incorrect." });

        if (!IsStrongPassword(request.NewPassword))
            return BadRequest(new { message = "Your new password does not meet all the requirements." });

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        Audit.Add(_db, User, "Password changed", "");
        await _db.SaveChangesAsync();

        return Ok(new { message = "Password updated successfully." });
    }

    /* ---------- DELETE OWN ADMIN ACCOUNT ---------- */

    [Authorize(Roles = "Admin")]
    [HttpPost("delete-admin-account")]
    public async Task<IActionResult> DeleteAdminAccount([FromBody] DeleteAdminAccountRequest request)
    {
        if (request == null || request.Confirmation != "DELETE MY ADMIN ACCOUNT")
            return BadRequest(new { message = "Type DELETE MY ADMIN ACCOUNT to confirm." });

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return Unauthorized(new { message = "Not logged in." });

        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == "Admin");
        if (admin == null)
            return Forbid();

        if (!PasswordHasher.Verify(request.Password ?? "", admin.PasswordHash))
            return Unauthorized(new { message = "Incorrect Admin password." });

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var tokens = await _db.PasswordResetTokens.Where(t => t.UserId == id).ToListAsync();
        _db.PasswordResetTokens.RemoveRange(tokens);
        // Preserve orders and records; detach any assignments referencing this account.
        var assignedOrders = await _db.PrintOrders.Where(o => o.AssignedStaffId == id).ToListAsync();
        foreach (var order in assignedOrders) order.AssignedStaffId = null;
        var attendance = await _db.AttendanceRecords.Where(a => a.StaffUserId == id).ToListAsync();
        foreach (var record in attendance) record.StaffUserId = null;
        Audit.Add(_db, id, admin.Username, "Admin", "Admin account deleted", "Self-service account removal");
        _db.Users.Remove(admin);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Admin account deleted. You may register a new Admin." });
    }

    /* ---------- FORGOT PASSWORD ---------- */

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var email = (request?.Email ?? "").Trim().ToLowerInvariant();

        if (!EmailPattern.IsMatch(email))
            return BadRequest(new { message = "Please enter a valid email address." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user != null)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

            var oldTokens = _db.PasswordResetTokens
                .Where(t => t.UserId == user.Id || t.ExpiresAt < DateTime.UtcNow);
            _db.PasswordResetTokens.RemoveRange(oldTokens);

            _db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = HashToken(token),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

            await _db.SaveChangesAsync();

            // Wala pang email service, kaya sa console ipinapakita ang reset link.
            var link = $"{Request.Scheme}://{Request.Host}/reset-password.html?token={token}";
            _logger.LogWarning("PASSWORD RESET for {Email}: {Link}", email, link);
        }

        // Parehong sagot kahit walang account (para hindi ma-guess ang mga email).
        return Ok(new { message = "If that email is registered, reset instructions have been sent." });
    }

    /* ---------- RESET PASSWORD ---------- */

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (!IsStrongPassword(request?.Password))
            return BadRequest(new { message = "Your password does not meet all the requirements." });

        var tokenHash = HashToken(request.Token ?? "");

        var entry = await _db.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.ExpiresAt > DateTime.UtcNow);

        var user = entry == null ? null : await _db.Users.FirstOrDefaultAsync(u => u.Id == entry.UserId);

        if (entry == null || user == null)
            return BadRequest(new { message = "This reset link is invalid or has expired." });

        user.PasswordHash = PasswordHasher.Hash(request.Password);
        Audit.Add(_db, user.Id, user.Username, user.Role, "Password reset", "");
        _db.PasswordResetTokens.Remove(entry);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Password updated. You can now log in." });
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}
