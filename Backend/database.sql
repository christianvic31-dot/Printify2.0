-- =====================================================================
-- PRINTIFY - PostgreSQL schema (optional)
-- The API creates all of this automatically on first run.
-- Use this script only if you prefer to create the tables yourself in
-- pgAdmin 4:  create a database named "printify", open Query Tool, run this.
-- =====================================================================

CREATE TABLE IF NOT EXISTS users (
    id            uuid PRIMARY KEY,
    full_name     text NOT NULL,
    username      text NOT NULL,
    email         text NOT NULL,
    password_hash text NOT NULL,                 -- PBKDF2-SHA256, never plain text
    role          text NOT NULL,                 -- 'Admin' or 'Staff'
    created_at    timestamptz NOT NULL,
    staff_code    text,                          -- STF-001 ...
    phone         text,
    "position"    text,
    address       text,
    status        text NOT NULL DEFAULT 'Active' -- Active / On Leave / Inactive
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_users_username ON users (username);
CREATE UNIQUE INDEX IF NOT EXISTS ix_users_email    ON users (email);

CREATE TABLE IF NOT EXISTS password_reset_tokens (
    id         uuid PRIMARY KEY,
    user_id    uuid NOT NULL,
    token_hash text NOT NULL,                    -- only the SHA-256 hash is stored
    expires_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_password_reset_tokens_token_hash ON password_reset_tokens (token_hash);

CREATE TABLE IF NOT EXISTS inventory_items (
    id         text PRIMARY KEY,                 -- PRD-001 ...
    sort_order integer NOT NULL,
    name       text,
    category   text,
    quantity   numeric NOT NULL,
    unit       text,
    price      numeric NOT NULL,
    threshold  numeric NOT NULL                  -- low-stock level
);

CREATE TABLE IF NOT EXISTS stock_transactions (
    id            uuid PRIMARY KEY,
    item_id       text NOT NULL REFERENCES inventory_items(id) ON DELETE CASCADE,
    item_name     text NOT NULL,
    type          text NOT NULL,                 -- Initial / In / Out / Adjustment
    quantity      numeric NOT NULL,              -- signed change
    balance_after numeric NOT NULL,
    note          text NOT NULL,
    user_id       uuid,
    username      text NOT NULL,
    created_at    timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_stock_transactions_item_id    ON stock_transactions (item_id);
CREATE INDEX IF NOT EXISTS ix_stock_transactions_created_at ON stock_transactions (created_at);

CREATE TABLE IF NOT EXISTS print_orders (
    id                text PRIMARY KEY,          -- ORD-1001 ...
    customer_name     text,
    customer_contact  text,
    printing_service  text,
    quantity          numeric NOT NULL,
    unit_price        numeric NOT NULL,
    total             numeric NOT NULL,
    amount_paid       numeric NOT NULL,
    order_date        text,
    due_date          text,
    order_status      text,                      -- Pending / In Progress / Completed / Cancelled
    payment_status    text,                      -- Paid / Unpaid / Partially Paid
    notes             text,
    assigned_staff_id uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at        timestamptz NOT NULL,
    updated_at        timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_print_orders_assigned_staff_id ON print_orders (assigned_staff_id);

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

-- Tables used by the older attendance / notification screens
CREATE TABLE IF NOT EXISTS collection_states (
    name       text PRIMARY KEY,
    updated_at timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS attendance_records (
    id bigint PRIMARY KEY, sort_order integer NOT NULL,000000000000000000000000000000
    name text, status text, date text, time text, time_out text NOT NULL DEFAULT '',
    staff_user_id uuid, notes text
);
CREATE TABLE IF NOT EXISTS notifications (
    id bigint PRIMARY KEY, sort_order integer NOT NULL,
    title text, message text, time text, "read" boolean NOT NULL,
    recipient_user_id uuid
);
CREATE INDEX IF NOT EXISTS ix_attendance_records_staff_user_id ON attendance_records (staff_user_id);
CREATE INDEX IF NOT EXISTS ix_notifications_recipient_user_id ON notifications (recipient_user_id);
-- Legacy tables the model still maps (safe to leave empty)
CREATE TABLE IF NOT EXISTS orders (
    id text PRIMARY KEY, sort_order integer NOT NULL, customer_name text, customer_contact text,
    service text, quantity numeric NOT NULL, unit_price numeric NOT NULL, order_date text, due_date text,
    status text, payment_status text, amount_paid numeric NOT NULL, notes text
);
CREATE TABLE IF NOT EXISTS staff_members (
    id text PRIMARY KEY, sort_order integer NOT NULL, name text, email text, phone text, hire_date text, status text
);
