using Microsoft.EntityFrameworkCore;
using Printify.Backend.Data;

namespace Printify.Backend.Services;

// Dinadagdagan ang database ng mga bagong column/table kahit meron nang lumang database
// (hindi nagagawa ng EnsureCreated kapag may existing na tables na).
public static class DbBootstrap
{
    public static void Run(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS pending_admin_registrations (
                id uuid PRIMARY KEY,
                full_name text NOT NULL,
                username text NOT NULL,
                email text NOT NULL UNIQUE,
                password_hash text NOT NULL,
                code_hash text NOT NULL,
                expires_at timestamptz NOT NULL,
                last_sent_at timestamptz NOT NULL,
                failed_attempts integer NOT NULL DEFAULT 0
            );
            ALTER TABLE inventory_items ADD COLUMN IF NOT EXISTS archived_at timestamptz;
            ALTER TABLE print_orders ADD COLUMN IF NOT EXISTS archived_at timestamptz;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS staff_code text;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS phone text;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS ""position"" text;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS address text;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'Active';
            ALTER TABLE attendance_records ADD COLUMN IF NOT EXISTS time_out text NOT NULL DEFAULT '';
            ALTER TABLE attendance_records ADD COLUMN IF NOT EXISTS staff_user_id uuid;
            ALTER TABLE notifications ADD COLUMN IF NOT EXISTS recipient_user_id uuid;

            CREATE TABLE IF NOT EXISTS print_orders (
                id                text PRIMARY KEY,
                customer_name     text,
                customer_contact  text,
                printing_service  text,
                quantity          numeric NOT NULL,
                unit_price        numeric NOT NULL,
                total             numeric NOT NULL,
                amount_paid       numeric NOT NULL,
                order_date        text,
                due_date          text,
                order_status      text,
                payment_status    text,
                notes             text,
                assigned_staff_id uuid,
                created_at        timestamptz NOT NULL,
                updated_at        timestamptz NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_print_orders_assigned_staff_id ON print_orders (assigned_staff_id);

            CREATE TABLE IF NOT EXISTS stock_transactions (
                id            uuid PRIMARY KEY,
                item_id       text NOT NULL REFERENCES inventory_items(id) ON DELETE CASCADE,
                item_name     text NOT NULL,
                type          text NOT NULL,
                quantity      numeric NOT NULL,
                balance_after numeric NOT NULL,
                note          text NOT NULL,
                user_id       uuid,
                username      text NOT NULL,
                created_at    timestamptz NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_stock_transactions_item_id ON stock_transactions (item_id);
            CREATE INDEX IF NOT EXISTS ix_stock_transactions_created_at ON stock_transactions (created_at);
            CREATE INDEX IF NOT EXISTS ix_attendance_records_staff_user_id ON attendance_records (staff_user_id);
            CREATE INDEX IF NOT EXISTS ix_notifications_recipient_user_id ON notifications (recipient_user_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_users_single_admin ON users (role)
                WHERE role = 'Admin';

            CREATE TABLE IF NOT EXISTS activity_logs (
                id         uuid PRIMARY KEY,
                user_id    uuid,
                username   text NOT NULL,
                role       text NOT NULL,
                action     text NOT NULL,
                details    text NOT NULL,
                created_at timestamptz NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_activity_logs_created_at ON activity_logs (created_at);
        ");

        // Bigyan ng staff code ang mga lumang Staff account na wala pa.
        var missing = db.Users
            .Where(u => u.Role == "Staff" && (u.StaffCode == null || u.StaffCode == ""))
            .OrderBy(u => u.CreatedAt)
            .ToList();

        if (missing.Count > 0)
        {
            foreach (var user in missing)
                user.StaffCode = NextStaffCode(db);

            db.SaveChanges();
        }

    }

    public static string NextStaffCode(AppDbContext db)
    {
        var max = 0;

        foreach (var code in db.Users.Where(u => u.StaffCode != null).Select(u => u.StaffCode).ToList())
        {
            if (code.StartsWith("STF-") && int.TryParse(code.Substring(4), out var n) && n > max)
                max = n;
        }

        // Kasama ang mga hindi pa nase-save na user sa tracker ng context.
        foreach (var entry in db.ChangeTracker.Entries<Printify.Backend.Models.User>())
        {
            var code = entry.Entity.StaffCode;
            if (code != null && code.StartsWith("STF-") && int.TryParse(code.Substring(4), out var n) && n > max)
                max = n;
        }

        return "STF-" + (max + 1).ToString("D3");
    }
}
