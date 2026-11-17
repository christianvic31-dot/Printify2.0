# Printify – Printing Shop Management System

ASP.NET Core 8 Web API + PostgreSQL (Npgsql / EF Core) serving the HTML/CSS/JS frontend.

## 1. Requirements
- Visual Studio 2022 (17.8+) with the **ASP.NET and web development** workload (includes .NET 8 SDK)
- PostgreSQL 14+ and pgAdmin 4
- Internet on first build (NuGet restore)

## 2. Database
The API creates the tables on first start. You only need an empty database:
1. Open **pgAdmin 4** → connect to your server → right-click **Databases → Create → Database…** → name it `printify`.
2. (Optional) Open **Query Tool** on that database and run `Backend/database.sql` to create the tables manually.

## 3. Configuration (keep secrets out of source control)
For local development, store the database connection string in User Secrets:
```powershell
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=printify;Username=postgres;Password=YOUR_PASSWORD" --project Backend\PrintifyBackend.csproj
```
The first Admin registration also requires `AdminSetup:InitialAdminToken`, set with User Secrets locally. Use a random value of at least 32 characters. Never put database credentials, the setup token, or certificate passwords in source control.

## 4. Run
1. Open `Printify.sln` and press **F5**, or run `dotnet run --project Backend\PrintifyBackend.csproj`.
2. For local Development only, create the first Admin at `https://localhost:3002/register.html` using the configured setup token. First-admin setup is disabled outside Development and cannot be opened remotely.
3. Log in as Admin → **Staff Management → Add New Staff** to create Staff accounts.
4. Forgot password: there is no email service yet. The reset link is written to the server console (Output window).

## 5. Public deployment (Render)
The root `Dockerfile` and `render.yaml` configure a Render web service and PostgreSQL database. Deploy the Blueprint from Render; the service is named `printify`, so its generated URL should be `https://printify.onrender.com` (confirm the actual URL in the Render dashboard). Production first-admin registration remains disabled. The hosted database starts empty, so manually provision an Admin account in that database before login. Render supplies HTTPS; existing local data is not uploaded.

## 6. Roles
| | Admin | Staff |
|---|---|---|
| Dashboard, reports, audit log | yes | no |
| Inventory | add/edit/delete/stock in-out/history | read only |
| Orders | create, edit, assign, cancel, delete | only orders assigned to them; status only |
| Staff accounts | create, edit, deactivate, delete | no |
Permissions are enforced by the API (`[Authorize(Roles=...)]`). On every request the cookie's role is re-checked
against the database, so a deactivated or deleted user is signed out immediately.

## 7. API summary
| Route | Who |
|---|---|
| POST /api/auth/login, logout, forgot-password, reset-password, change-password; GET /api/auth/me | public / logged in |
| GET /api/inventory | Admin, Staff |
| POST/PUT/DELETE /api/inventory, POST /api/inventory/{id}/stock, GET /api/inventory/history | Admin |
| GET/POST/PUT/DELETE /api/orders; GET /api/orders/mine; PATCH /api/orders/{id}/status | Admin / Staff (own orders) |
| /api/staff (CRUD) | Admin |
| GET /api/dashboard/summary | Admin |  GET /api/dashboard/mine | Staff |
| GET /api/reports/chart, sales?period=daily\|weekly\|monthly\|yearly, orders, inventory, staff-performance (add `&format=csv`) | Admin |
| GET /api/audit | Admin |

"Total Sales" on the dashboard = money actually collected (sum of amount paid on non-cancelled orders).
Reports also show the billed total.

## 8. Known limitations
See `TESTING.md` for the checklist and what is not finished.
