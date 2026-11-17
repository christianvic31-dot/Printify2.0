using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Printify.Backend.Models;

/* ==========================================
   USERS (login / register)
========================================== */

public class User
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Staff";      // "Admin" o "Staff"
    public DateTime CreatedAt { get; set; }

    // Para sa Staff accounts (ginagawa ng Admin)
    public string StaffCode { get; set; }            // hal. "STF-001"
    public string Phone { get; set; }
    public string Position { get; set; }
    public string Address { get; set; }
    public string Status { get; set; } = "Active";   // Active / On Leave / Inactive
}

public class PendingAdminRegistration
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime LastSentAt { get; set; }
    public int FailedAttempts { get; set; }
}

public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

// Nagtatala kung nai-save na ang isang collection (para hindi bumalik ang sample data
// kapag sinadyang binura lahat ng laman).
public class CollectionState
{
    [Key]
    public string Name { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

/* ==========================================
   BASE CLASS PARA SA MGA LISTAHAN
   (ang SortOrder ay para mapanatili ang pagkakasunod-sunod
   na ginagamit ng frontend, hal. pinakabago ang nasa taas)
========================================== */

public abstract class ListItem
{
    [JsonIgnore]
    public int SortOrder { get; set; }

    public abstract object GetKey();
}

/* ==========================================
   INVENTORY
========================================== */

public class InventoryItem : ListItem
{
    public DateTime? ArchivedAt { get; set; }
    [Key]
    public string Id { get; set; }          // hal. "PRD-001"
    public string Name { get; set; }
    public string Category { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; }
    public decimal Price { get; set; }
    public decimal Threshold { get; set; }

    public override object GetKey() => Id ?? "";
}

/* ==========================================
   ORDERS
========================================== */

public class Order : ListItem
{
    [Key]
    public string Id { get; set; }          // hal. "ORD-1001"
    public string CustomerName { get; set; }
    public string CustomerContact { get; set; }
    public string Service { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string OrderDate { get; set; }   // "yyyy-MM-dd"
    public string DueDate { get; set; }     // "yyyy-MM-dd"
    public string Status { get; set; }      // Pending / In Progress / Completed / Cancelled
    public string PaymentStatus { get; set; } // Paid / Unpaid / Partially Paid
    public decimal AmountPaid { get; set; }
    public string Notes { get; set; }

    public override object GetKey() => Id ?? "";
}

/* ==========================================
   STAFF
========================================== */

public class StaffMember : ListItem
{
    [Key]
    public string Id { get; set; }          // hal. "STF-001"
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string HireDate { get; set; }    // "yyyy-MM-dd"
    public string Status { get; set; }      // Active / On Leave / Inactive

    public override object GetKey() => Id ?? "";
}

/* ==========================================
   ATTENDANCE
========================================== */

public class AttendanceRecord : ListItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }            // Date.now() mula sa frontend
    public string Name { get; set; }
    public string Status { get; set; }
    public string Date { get; set; }
    public string Time { get; set; }
    public string TimeOut { get; set; } = "";
    public Guid? StaffUserId { get; set; }
    public string Notes { get; set; }

    public override object GetKey() => Id;
}

/* ==========================================
   NOTIFICATIONS
========================================== */

public class NotificationItem : ListItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }
    public string Title { get; set; }
    public string Message { get; set; }
    public string Time { get; set; }
    public bool Read { get; set; }
    public Guid? RecipientUserId { get; set; }

    public override object GetKey() => Id;
}


/* ==========================================
   PRINT ORDERS (may naka-assign na Staff)
========================================== */

public class StockTransaction
{
    public Guid Id { get; set; }
    public string ItemId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Type { get; set; } = "";          // Initial / In / Out / Adjustment
    public decimal Quantity { get; set; }           // signed change (+ in, - out)
    public decimal BalanceAfter { get; set; }
    public string Note { get; set; } = "";
    public Guid? UserId { get; set; }
    public string Username { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class ActivityLog
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class PrintOrder
{
    public DateTime? ArchivedAt { get; set; }
    [Key]
    public string Id { get; set; }              // hal. "ORD-1001"
    public string CustomerName { get; set; }
    public string CustomerContact { get; set; }
    public string PrintingService { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public string OrderDate { get; set; }       // "yyyy-MM-dd"
    public string DueDate { get; set; }         // "yyyy-MM-dd"
    public string OrderStatus { get; set; }     // Pending / In Progress / Completed / Cancelled
    public string PaymentStatus { get; set; }   // Paid / Unpaid / Partially Paid
    public string Notes { get; set; }

    // Ang Staff (User.Id) na nakatoka sa order na ito. Null = wala pang naka-assign.
    public Guid? AssignedStaffId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
