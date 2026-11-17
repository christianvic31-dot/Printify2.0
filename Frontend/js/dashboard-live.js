
/* =====================================================
   PRINTIFY - DASHBOARD LIVE DATA
   GET /api/dashboard/summary
   PostgreSQL + ASP.NET Core API
===================================================== */

document.addEventListener("DOMContentLoaded", function () {
    "use strict";

    /* =================================================
       HELPERS
    ================================================= */

    function escapeHTML(value) {
        return String(value == null ? "" : value).replace(/[&<>"']/g, function (character) {
            return {
                "&": "&amp;",
                "<": "&lt;",
                ">": "&gt;",
                '"': "&quot;",
                "'": "&#039;"
            }[character];
        });
    }

    function peso(value) {
        return new Intl.NumberFormat("en-PH", {
            style: "currency",
            currency: "PHP"
        }).format(Number(value) || 0);
    }

    function setText(id, value) {
        const element = document.getElementById(id);

        if (element) {
            element.textContent = value == null ? "0" : value;
        }
    }

    function getArray(value) {
        return Array.isArray(value) ? value : [];
    }

    function emptyRow(columns, message) {
        return `
            <tr>
                <td colspan="${columns}">
                    ${escapeHTML(message)}
                </td>
            </tr>
        `;
    }


    /* =================================================
       LOAD DASHBOARD SUMMARY
       SUPPORTS ASYNC AND NORMAL API RESPONSES
    ================================================= */

    async function loadSummary() {

        // Use existing summary if already loaded.
        if (
            window.PrintifySummary &&
            typeof window.PrintifySummary === "object"
        ) {
            return window.PrintifySummary;
        }

        if (
            !window.PrintifyAPI ||
            typeof window.PrintifyAPI.request !== "function"
        ) {
            console.error("PrintifyAPI.request is not available.");
            return null;
        }

        try {

            const response = await window.PrintifyAPI.request(
                "GET",
                "/api/dashboard/summary"
            );

            if (!response) {
                throw new Error("Empty API response.");
            }

            // Check HTTP status when available.
            if (
                typeof response.status === "number" &&
                (response.status < 200 || response.status >= 300)
            ) {
                throw new Error(
                    "Dashboard API returned status: " + response.status
                );
            }

            let data;

            // Handle native Fetch Response.
            if (typeof response.json === "function") {
                data = await response.json();
            }

            // Handle custom API response.
            else if (response.data !== undefined) {
                data = response.data;
            }

            // Handle direct JSON object.
            else {
                data = response;
            }

            if (!data || typeof data !== "object") {
                throw new Error("Invalid dashboard data.");
            }

            console.log("Dashboard API data:", data);

            return data;

        } catch (error) {

            console.error(
                "Unable to load dashboard summary:",
                error
            );

            return null;
        }
    }


    /* =================================================
       SUMMARY CARDS
    ================================================= */

    function renderSummaryCards(summary) {

        setText(
            "totalCustomers",
            Number(summary.totalCustomers) || 0
        );

        setText(
            "totalOrders",
            Number(summary.totalOrders) || 0
        );

        setText(
            "totalSales",
            peso(summary.totalSales)
        );

        // Total Staff means all registered staff.
        setText(
            "totalStaff",
            Number(summary.totalStaff) || 0
        );

        setText(
            "currentlyWorking",
            Number(summary.currentlyWorking) || 0
        );

        setText(
            "completedStaffWork",
            Number(summary.completedStaffWork) || 0
        );
    }


    /* =================================================
       LOW STOCK ITEMS
    ================================================= */

    function renderLowStock(summary) {

        const list = document.getElementById("lowStockList");

        const lowStock = getArray(summary.lowStock);

        const lowStockCount = Number(summary.lowStockCount) || 0;

        setText(
            "lowStockCount",
            lowStockCount +
            (lowStockCount === 1 ? " Item" : " Items")
        );

        if (!list) return;

        if (lowStock.length === 0) {

            list.innerHTML = `
                <p class="dashboard-empty-message">
                    All items are above their low-stock level.
                </p>
            `;

            return;
        }

        list.innerHTML = lowStock.map(function (item) {

            const category = String(
                item.category || ""
            ).toLowerCase();

            let icon = "inventory_icon.png";
            let iconClass = "";
            let badgeClass = "warning-badge";
            let label = "Reorder Soon";

            if (category.includes("ink")) {

                icon = "ink_icon.png";
                iconClass = "ink-product";

            } else if (
                category.includes("paper") ||
                category.includes("bond")
            ) {

                icon = "paper_icon.png";
            }

            if (item.level === "out") {

                badgeClass = "critical-badge";
                label = "Out of Stock";

            } else if (item.level === "critical") {

                badgeClass = "critical-badge";
                label = "Low Stock";

            } else if (item.level === "low") {

                badgeClass = "warning-badge";
                label = "Reorder Soon";
            }

            return `
                <div class="low-stock-item">

                    <div class="product-icon ${iconClass}">
                        <img
                            src="image/${icon}"
                            alt=""
                        >
                    </div>

                    <div class="product-information">
                        <strong>
                            ${escapeHTML(item.name)}
                        </strong>

                        <span>
                            ${escapeHTML(item.category)}
                        </span>
                    </div>

                    <div class="stock-information">

                        <strong>
                            ${escapeHTML(item.quantity)}
                            ${escapeHTML(item.unit || "")}
                        </strong>

                        <span class="stock-badge ${badgeClass}">
                            ${label}
                        </span>

                    </div>

                </div>
            `;

        }).join("");
    }


    /* =================================================
       RECENT ORDERS
    ================================================= */

    function renderRecentOrders(summary) {

        const ordersBody = document.getElementById(
            "recentOrdersBody"
        );

        if (!ordersBody) return;

        const orders = getArray(summary.recentOrders);

        if (orders.length === 0) {

            ordersBody.innerHTML = emptyRow(
                6,
                "No orders yet."
            );

            return;
        }

        ordersBody.innerHTML = orders.map(function (order) {

            return `
                <tr>

                    <td>${escapeHTML(order.id)}</td>

                    <td>${escapeHTML(order.customer)}</td>

                    <td>${escapeHTML(order.service)}</td>

                    <td>${peso(order.total)}</td>

                    <td>${escapeHTML(order.status)}</td>

                    <td>${escapeHTML(order.paymentStatus)}</td>

                </tr>
            `;

        }).join("");
    }


    /* =================================================
       RECENT SYSTEM ACTIVITY
    ================================================= */

    function renderRecentActivity(summary) {

        const activityBody = document.getElementById(
            "staffRecordsBody"
        );

        if (!activityBody) return;

        const activities = getArray(
            summary.recentActivity
        );

        if (activities.length === 0) {

            activityBody.innerHTML = emptyRow(
                5,
                "No activity recorded yet."
            );

            return;
        }

        activityBody.innerHTML = activities.map(function (activity) {

            const date = activity.createdAt
                ? new Date(activity.createdAt)
                : null;

            const formattedDate =
                date && !isNaN(date.getTime())
                    ? date.toLocaleString("en-PH")
                    : "—";

            return `
                <tr>

                    <td>${escapeHTML(activity.username)}</td>

                    <td>${escapeHTML(activity.role)}</td>

                    <td>${escapeHTML(activity.action)}</td>

                    <td>${escapeHTML(activity.details)}</td>

                    <td>${escapeHTML(formattedDate)}</td>

                </tr>
            `;

        }).join("");
    }


    /* =================================================
       HANDLE API ERROR
    ================================================= */

    function showDashboardError() {

        setText("totalCustomers", "—");
        setText("totalOrders", "—");
        setText("totalSales", "—");
        setText("totalStaff", "—");
        setText("currentlyWorking", "—");
        setText("completedStaffWork", "—");

        setText("lowStockCount", "—");

        const lowStockList = document.getElementById(
            "lowStockList"
        );

        const ordersBody = document.getElementById(
            "recentOrdersBody"
        );

        const activityBody = document.getElementById(
            "staffRecordsBody"
        );

        if (lowStockList) {
            lowStockList.innerHTML = `
                <p class="dashboard-empty-message">
                    Unable to load inventory information.
                </p>
            `;
        }

        if (ordersBody) {
            ordersBody.innerHTML = emptyRow(
                6,
                "Unable to load recent transactions."
            );
        }

        if (activityBody) {
            activityBody.innerHTML = emptyRow(
                5,
                "Unable to load recent system activity."
            );
        }

        window.dispatchEvent(
            new CustomEvent("printify:summary-error")
        );
    }


    /* =================================================
       INITIALIZE DASHBOARD
    ================================================= */

    async function initializeDashboard() {

        console.log("Loading Printify dashboard...");

        const summary = await loadSummary();

        if (!summary) {

            showDashboardError();

            return;
        }

        // Save summary for other dashboard scripts.
        window.PrintifySummary = summary;

        // Render dashboard information.
        renderSummaryCards(summary);

        renderLowStock(summary);

        renderRecentOrders(summary);

        renderRecentActivity(summary);

        // Notify dashboard chart and other components.
        window.dispatchEvent(
            new CustomEvent(
                "printify:summary-updated",
                {
                    detail: summary
                }
            )
        );

        console.log(
            "Printify dashboard successfully initialized."
        );
    }


    /* =================================================
       START
    ================================================= */

    initializeDashboard();

});
