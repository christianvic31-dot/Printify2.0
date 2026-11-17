# Printify Backend (C# ASP.NET Core + PostgreSQL)

## Kailangan
1. **Visual Studio 2022** (version 17.8 pataas) na may **"ASP.NET and web development"** workload.
   (Kung luma ang VS mo: Help > Check for Updates.)
2. **PostgreSQL + pgAdmin 4** -> https://www.postgresql.org/download/
3. Internet sa unang takbo (dina-download ang NuGet packages).

## Paano patakbuhin sa Visual Studio 2022
1. I-double-click ang **`Printify.sln`** (nasa labas ng Backend at Frontend folder).
2. Itakda ang database connection string gamit ang User Secrets:
   ```
   dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=printify;Username=postgres;Password=PASSWORD_MO" --project Backend\PrintifyBackend.csproj
   ```
3. Magtakda rin ng random na `AdminSetup:InitialAdminToken` na hindi bababa sa 32 characters gamit ang User Secrets para sa lokal na first-admin setup:
   ```
   dotnet user-secrets set "AdminSetup:InitialAdminToken" "ILAGAY_ANG_RANDOM_SECRET_NA_32_CHARACTERS_O_HIGIT_PA" --project Backend\PrintifyBackend.csproj
   ```
4. Pindutin ang **F5** (o Ctrl+F5). Gamitin ang HTTPS address na naka-configure para sa Development.

Kung hindi ginagamit ang VS: sa terminal, sa loob ng `Backend`, `dotnet run`.
Para gumawa ng unang Admin nang lokal, buksan mismo ang `https://localhost:3002/register.html`; wala nang “Open setup” link sa login page.

Ang database na `printify` at lahat ng table ay **kusang gagawin** pag unang takbo. Hindi mo kailangang gumawa sa pgAdmin.
The default development address is `https://localhost:3002`. To use another address, set `ASPNETCORE_URLS` (for example, `https://localhost:3003`) before starting the backend.
Huwag buksan nang direkta ang HTML files (file://). Dapat buksan ang site sa HTTPS address na naka-configure para sa Development.

## Makita ang data sa pgAdmin 4
Servers > PostgreSQL > Databases > **printify** > Schemas > public > Tables
(users, orders, inventory_items, staff_members, attendance_records, notifications, ...)
Right-click ang table > View/Edit Data > All Rows.

(Optional: nandito rin ang `database.sql` kung gusto mong gumawa ng mga table nang mano-mano sa Query Tool.)

## Accounts (Admin at Staff)
- Ang first-admin setup ay para lang sa Development sa `localhost` at kailangan ang tamang setup token. Naka-disable ito sa Production; gamitin muna ang lokal na app.
- Ang **Admin lang** ang gumagawa ng **Staff accounts**: *Staff Management > Add New Staff* (username + password ng staff).
- **Admin** (dashboard.html) - nakikita ang lahat: orders, inventory, staff, tracking, reports, settings.
  Puwedeng pumili ng staff sa *Orders > Assign to Staff*; kung walang pinili, salit-salitang
  ia-assign ang order sa mga aktibong staff. Nakikita niya sa
  Dashboard at Staff Management kung **ilang orders ang natanggap ng bawat Staff** (pending / in progress / completed).
- **Staff** (staff-dashboard.html) - nakikita ang sariling naka-assign na orders at order notifications;
  puwedeng mag-update ng status, mag-clock in/out, mag-edit ng sariling contact details, at magpalit ng password.
  Hindi sila makakapasok sa mga Admin page.
- Ang proteksyon ay nasa server din (hindi lang sa page): 403/404 ang sagot kapag Staff ang kumuha ng Admin data.
- Forgot password: ang reset link ay ipinapakita sa terminal ng server (wala pang email service).

## Public deployment (Render)
May `Dockerfile` at `render.yaml` sa project root para sa Render web service at managed PostgreSQL. Gumawa ng Blueprint sa Render mula sa repository; ang service name ay `printify`, kaya inaasahang `https://printify.onrender.com` ang generated URL (kumpirmahin ito sa Render dashboard). Sa Production, sarado ang first-admin registration; kailangang mag-provision ng Admin account nang direkta sa hosted database bago makapag-login. Walang lokal na staff o business data na awtomatikong ina-upload o inililipat; maaaring may singil ang hosting at database plans.
Sa Production, nire-redirect ng server ang HTTP requests sa HTTPS. Para sa custom domain, idagdag ito sa Render at tiyaking aktibo ang TLS certificate nito; gamitin ang `https://` address.

## Kung ibang .NET version ang meron ka
Sa `PrintifyBackend.csproj`, palitan ang `net8.0` (hal. `net9.0`) at ang version ng
`Npgsql.EntityFrameworkCore.PostgreSQL` sa katugmang major version (9.x para sa net9.0).

## API
| Method | URL | Gamit |
|---|---|---|
| POST | /api/auth/register | Gumawa ng account |
| POST | /api/auth/login | Mag-login (cookie session) |
| POST | /api/auth/logout | Mag-logout |
| GET | /api/auth/me | Kasalukuyang user |
| POST | /api/auth/forgot-password | Humingi ng reset link |
| POST | /api/auth/reset-password | Mag-set ng bagong password |
| POST | /api/auth/change-password | Palitan ang password |
| GET/POST/PUT/DELETE | /api/staff | Admin lang: Staff accounts + bilang ng orders kada staff |
| POST | /api/staff/attendance/scan | Admin: i-verify ang Staff ID mula sa QR at mag-clock in/out |
| GET/POST/PUT/DELETE | /api/orders | Admin lang: lahat ng orders (may assigned staff) |
| GET | /api/orders/mine | Staff: ang sariling orders lang |
| PATCH | /api/orders/{id}/status | Staff (sariling order) o Admin |
| GET/PUT | /api/staff/me/profile | Staff: sariling profile |
| GET/POST | /api/staff/me/attendance | Staff: sariling attendance; POST sa `/clock-in` at `/clock-out` |
| GET/PATCH | /api/staff/me/notifications | Staff: sariling order notifications |
| GET/PUT | /api/data/inventory, orders, staff | Luma, Admin lang (hindi na ginagamit ng orders/staff pages) |
| GET/PUT | /api/data/attendance, notifications | Admin; Staff GET ay sariling attendance at sariling/global notifications lang |

## Istruktura
- `Program.cs` - setup ng server, database, login, at pag-serve ng Frontend
- `Controllers/AuthController.cs` - register, login, logout, forgot/reset password
- `Controllers/StaffController.cs` - Staff accounts (Admin)
- `Controllers/StaffSelfController.cs` - sariling profile, attendance, at notifications (Staff)
- `Controllers/OrdersController.cs` - orders at assignment sa Staff
- `Controllers/DataController.cs` - inventory, attendance, notifications (luma)
- `Models/Entities.cs` - mga table
- `Data/AppDbContext.cs` - koneksyon sa PostgreSQL
- `Services/` - password hashing, login lock

## Kung may luma ka nang database
Kusang dinadagdagan ng server ng bagong columns sa `users`, `attendance_records`, at `notifications`, pati bagong table na `print_orders`, pag nag-start.
Hindi na ililipat ang mga lumang orders na nasa browser (localStorage) lang; gawin ulit ang mga ito sa Orders page.
Ang mga lumang Staff account (dating nag-register) ay mananatili at bibigyan ng Staff ID (STF-001, ...).
