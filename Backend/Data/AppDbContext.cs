using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Printify.Backend.Models;

namespace Printify.Backend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<PendingAdminRegistration> PendingAdminRegistrations => Set<PendingAdminRegistration>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<CollectionState> CollectionStates => Set<CollectionState>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<NotificationItem> Notifications => Set<NotificationItem>();
    public DbSet<PrintOrder> PrintOrders => Set<PrintOrder>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Mga pangalan ng table sa PostgreSQL (makikita sa pgAdmin 4)
        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<PendingAdminRegistration>().ToTable("pending_admin_registrations");
        modelBuilder.Entity<PendingAdminRegistration>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<PasswordResetToken>().ToTable("password_reset_tokens");
        modelBuilder.Entity<CollectionState>().ToTable("collection_states");
        modelBuilder.Entity<InventoryItem>().ToTable("inventory_items");
        modelBuilder.Entity<Order>().ToTable("orders");
        modelBuilder.Entity<StaffMember>().ToTable("staff_members");
        modelBuilder.Entity<AttendanceRecord>().ToTable("attendance_records");
        modelBuilder.Entity<AttendanceRecord>().HasIndex(a => a.StaffUserId);
        modelBuilder.Entity<NotificationItem>().ToTable("notifications");
        modelBuilder.Entity<NotificationItem>().HasIndex(n => n.RecipientUserId);
        modelBuilder.Entity<PrintOrder>().ToTable("print_orders");
        modelBuilder.Entity<PrintOrder>().HasIndex(o => o.AssignedStaffId);
        modelBuilder.Entity<StockTransaction>().ToTable("stock_transactions");
        modelBuilder.Entity<StockTransaction>().HasIndex(t => t.ItemId);
        modelBuilder.Entity<StockTransaction>().HasIndex(t => t.CreatedAt);
        modelBuilder.Entity<StockTransaction>().HasOne<InventoryItem>().WithMany().HasForeignKey(t => t.ItemId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ActivityLog>().ToTable("activity_logs");
        modelBuilder.Entity<ActivityLog>().HasIndex(a => a.CreatedAt);
        modelBuilder.Entity<PrintOrder>().HasOne<User>().WithMany().HasForeignKey(o => o.AssignedStaffId).OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<User>().HasIndex(u => u.Role)
            .IsUnique()
            .HasDatabaseName("ix_users_single_admin")
            .HasFilter("\"role\" = 'Admin'");
        modelBuilder.Entity<PasswordResetToken>().HasIndex(t => t.TokenHash);

        // Gawing snake_case ang mga column (full_name, unit_price, ...)
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        return Regex.Replace(name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
    }
}
