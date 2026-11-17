# Test checklist and known gaps

Use this checklist for a manual regression pass. The project has no automated test suite.

## Build
1. Open `Printify.sln` → Build → Rebuild Solution. Expect 0 errors.

## Auth / roles
1. Fresh DB → `register.html` creates the Admin; a second registration returns 403.
2. Admin logs in → lands on `dashboard.html`. Add a Staff account in Staff Management.
3. Staff logs in → `staff-dashboard.html`. Open `dashboard.html`, `inventory.html`, `reports.html` directly → redirected away.
4. While logged in as Staff, in the browser console: `fetch('/api/staff').then(r=>r.status)` → 403; `/api/reports/orders` → 403; `/api/audit` → 403; `/api/dashboard/summary` → 403.
5. Set the Staff to Inactive as Admin → Staff's next request returns 401 and they are sent to login.
6. Wrong password 5 times → 429 for 15 minutes.

## Inventory
1. Add, edit, delete items → refresh the page → changes persist; check `inventory_items` in pgAdmin.
2. Edit a quantity → a row of type `Adjustment` appears in `stock_transactions`.
3. `POST /api/inventory/{id}/stock` with `{"type":"Out","quantity":999999}` → 400.

## Orders / dashboard / reports
1. Create several orders without selecting a Staff member; verify they are assigned to active Staff in rotation.
2. Log in as each Staff member; verify only their assigned orders and assignment notifications appear.
3. Use Refresh Orders and verify it reloads from the server; update status and check the Admin Orders page.
4. Dashboard numbers equal `SELECT count(*) ... FROM print_orders` groupings; low-stock list matches `quantity <= threshold`.
5. `/api/reports/sales?period=monthly&format=csv` downloads a CSV that opens in Excel.

## Staff workspace
1. Clock in and out; verify the Attendance page shows the records and a Staff account cannot view another Staff member's records.
2. Update profile contact details and verify they remain after logging out and back in.
3. Change the password and theme from Settings.

## Not finished / not done
- **Stock In/Out and Inventory History have API endpoints but no buttons yet** on `inventory.html`. Quantity edits are logged as adjustments.
- **Order status "Ready"** from the brief is not a status in the database/API (Pending, In Progress, Completed, Cancelled only).
- **Admin Staff Tracking** (`staff-tracking.html`) still keeps its attendance view in browser `localStorage`; Staff self-service attendance is stored in PostgreSQL.
- Staff theme preferences remain in `localStorage`; there is no system-settings table.
- **Audit log** is available at `/api/audit` and a summary on the dashboard; there is no full audit page.
- **Forgot password** has no email delivery (link is logged to the server console).
- **PDF export**: reports page uses the browser's Print → Save as PDF; CSV export is server-side.
- Visual redesign was **not** done; the existing styling was kept.
- Customers, Payments, ProductsAndServices tables from the brief were not created as separate tables (customer name and payment fields live on `print_orders`).
- No automated tests.
