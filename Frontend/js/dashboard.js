
/* =====================================================
   PRINTIFY - ADMIN DASHBOARD CONTROLLER
===================================================== */

document.addEventListener("DOMContentLoaded", function () {
    "use strict";

    const body = document.body;

    const sidebar = document.getElementById("sidebar");
    const sidebarOverlay = document.getElementById("sidebarOverlay");
    const menuToggle = document.getElementById("menuToggle");

    const themeToggle = document.getElementById("themeToggle");
    const themeIcon = document.getElementById("themeIcon");

    const currentDate = document.getElementById("currentDate");
    const currentYear = document.getElementById("currentYear");

    const profileButton = document.getElementById("profileButton");
    const profileDropdown = document.getElementById("profileDropdown");

    const notificationButton = document.getElementById("notificationButton");
    const notificationDot = document.getElementById("notificationDot");

    const logoutButton = document.getElementById("logoutButton");
    const sidebarLogoutButton = document.getElementById("sidebarLogoutButton");

    const salesPeriod = document.getElementById("salesPeriod");
    const salesChartCanvas = document.getElementById("salesChart");

    const mobileScreen = window.matchMedia("(max-width: 850px)");

    let salesChart = null;
    let logoutInProgress = false;


    /* =================================================
       ALERT HELPER
    ================================================= */

    function alertMessage(type, title, message) {
        if (window.PrintifyAlert &&
            typeof window.PrintifyAlert[type] === "function") {
            return window.PrintifyAlert[type](title, message);
        }

        if (typeof window.Swal !== "undefined") {
            return window.Swal.fire({
                icon: type === "error" ? "error" : "info",
                title: title,
                text: message,
                confirmButtonText: "Okay"
            });
        }

        window.alert(title + (message ? "\n\n" + message : ""));
        return Promise.resolve({ isConfirmed: true });
    }


    function confirmAction(title, message) {
        if (window.PrintifyAlert &&
            typeof window.PrintifyAlert.confirm === "function") {
            return window.PrintifyAlert.confirm(
                title,
                message,
                "Yes, log out",
                "Stay"
            );
        }

        if (typeof window.Swal !== "undefined") {
            return window.Swal.fire({
                icon: "question",
                title: title,
                text: message,
                showCancelButton: true,
                confirmButtonText: "Yes, log out",
                cancelButtonText: "Stay",
                reverseButtons: true
            });
        }

        return Promise.resolve({
            isConfirmed: window.confirm(title + "\n\n" + message)
        });
    }


    /* =================================================
       DATE AND YEAR
    ================================================= */

    function updateDate() {
        if (!currentDate) return;

        const dateText = new Date().toLocaleDateString("en-PH", {
            weekday: "long",
            year: "numeric",
            month: "long",
            day: "numeric"
        });

        const dateSpan = currentDate.querySelector("span");

        if (dateSpan) {
            dateSpan.textContent = dateText;
        } else {
            currentDate.textContent = dateText;
        }
    }

    if (currentYear) {
        currentYear.textContent = new Date().getFullYear();
    }

    updateDate();


    /* =================================================
       SIDEBAR
    ================================================= */

    function setSidebarState(isOpen) {
        if (!sidebar) return;

        if (mobileScreen.matches) {
            sidebar.classList.toggle("is-hidden", !isOpen);
            body.classList.toggle("sidebar-hidden", !isOpen);

            if (sidebarOverlay) {
                sidebarOverlay.classList.toggle("show", isOpen);
            }
        } else {
            sidebar.classList.toggle("is-hidden", !isOpen);
            body.classList.toggle("sidebar-hidden", !isOpen);

            if (sidebarOverlay) {
                sidebarOverlay.classList.remove("show");
            }
        }

        try {
            localStorage.setItem(
                "printify-sidebar-hidden",
                String(!isOpen)
            );
        } catch (error) {
            console.warn("Unable to save sidebar preference.", error);
        }
    }


    function initializeSidebar() {
        let isHidden = true;

        try {
            const saved = localStorage.getItem("printify-sidebar-hidden");

            if (saved !== null) {
                isHidden = saved === "true";
            }
        } catch (error) {
            console.warn("Unable to read sidebar preference.", error);
        }

        setSidebarState(!isHidden);
    }


    if (menuToggle) {
        menuToggle.addEventListener("click", function () {
            const currentlyOpen =
                sidebar && !sidebar.classList.contains("is-hidden");

            setSidebarState(!currentlyOpen);
        });
    }


    if (sidebarOverlay) {
        sidebarOverlay.addEventListener("click", function () {
            setSidebarState(false);
        });
    }


    function handleScreenChange() {
        if (mobileScreen.matches) {
            setSidebarState(false);
        } else {
            if (sidebarOverlay) {
                sidebarOverlay.classList.remove("show");
            }

            let isHidden = true;

            try {
                isHidden =
                    localStorage.getItem("printify-sidebar-hidden") === "true";
            } catch (error) {
                isHidden = true;
            }

            if (sidebar) {
                sidebar.classList.toggle("is-hidden", isHidden);
            }

            body.classList.toggle("sidebar-hidden", isHidden);
        }
    }


    if (typeof mobileScreen.addEventListener === "function") {
        mobileScreen.addEventListener("change", handleScreenChange);
    } else if (typeof mobileScreen.addListener === "function") {
        mobileScreen.addListener(handleScreenChange);
    }

    initializeSidebar();


    /* =================================================
       THEME TOGGLE
    ================================================= */

    function applyTheme(theme) {
        const isDark = theme === "dark";

        body.classList.toggle("dark-mode", isDark);

        if (themeIcon) {
            themeIcon.src = isDark
                ? "image/nightmode_icon.png"
                : "image/lightmode_icon.png";

            themeIcon.alt = isDark ? "Dark mode" : "Light mode";
        }

        if (themeToggle) {
            themeToggle.setAttribute(
                "aria-label",
                isDark ? "Switch to light mode" : "Switch to dark mode"
            );

            themeToggle.title = isDark
                ? "Switch to light mode"
                : "Switch to dark mode";
        }

        try {
            localStorage.setItem("printify-theme", theme);
        } catch (error) {
            console.warn("Unable to save theme preference.", error);
        }

        updateChartTheme();
    }


    let savedTheme = "light";

    try {
        savedTheme = localStorage.getItem("printify-theme") || "light";
    } catch (error) {
        savedTheme = "light";
    }

    applyTheme(savedTheme);


    if (themeToggle) {
        themeToggle.addEventListener("click", function () {
            const nextTheme = body.classList.contains("dark-mode")
                ? "light"
                : "dark";

            applyTheme(nextTheme);
        });
    }


    /* =================================================
       PROFILE DROPDOWN
    ================================================= */

    function closeProfileDropdown() {
        if (!profileDropdown) return;

        profileDropdown.classList.remove("show");

        if (profileButton) {
            profileButton.setAttribute("aria-expanded", "false");
        }
    }


    if (profileButton && profileDropdown) {
        profileButton.addEventListener("click", function (event) {
            event.stopPropagation();

            const isOpen = profileDropdown.classList.toggle("show");

            profileButton.setAttribute("aria-expanded", String(isOpen));
        });

        profileDropdown.addEventListener("click", function (event) {
            event.stopPropagation();
        });

        document.addEventListener("click", function () {
            closeProfileDropdown();
        });

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                closeProfileDropdown();
            }
        });
    }


    /* =================================================
       LOGOUT
    ================================================= */

    async function performLogout() {
        if (logoutInProgress) return;

        const result = await confirmAction(
            "Log out of Printify?",
            "Are you sure you want to end your administrator session?"
        );

        if (!result || !result.isConfirmed) {
            return;
        }

        logoutInProgress = true;

        if (logoutButton) logoutButton.disabled = true;
        if (sidebarLogoutButton) sidebarLogoutButton.disabled = true;

        try {
            if (
                window.PrintifyAPI &&
                typeof window.PrintifyAPI.logout === "function"
            ) {
                const logoutResult = window.PrintifyAPI.logout();

                if (logoutResult &&
                    typeof logoutResult.then === "function") {
                    await logoutResult;
                }
            } else {
                try {
                    localStorage.removeItem("token");
                    localStorage.removeItem("accessToken");
                    localStorage.removeItem("user");
                    sessionStorage.clear();
                } catch (error) {
                    console.warn(
                        "Unable to clear local session data.",
                        error
                    );
                }
            }

            window.location.replace("login.html");

        } catch (error) {
            console.error("Logout error:", error);

            logoutInProgress = false;

            if (logoutButton) logoutButton.disabled = false;
            if (sidebarLogoutButton) sidebarLogoutButton.disabled = false;

            alertMessage(
                "error",
                "Logout failed",
                "We could not complete your logout. Please try again."
            );
        }
    }


    if (logoutButton) {
        logoutButton.addEventListener("click", performLogout);
    }

    if (sidebarLogoutButton) {
        sidebarLogoutButton.addEventListener("click", performLogout);
    }


    /* =================================================
       NOTIFICATIONS
    ================================================= */

    function getSummary() {
        return window.PrintifySummary || {};
    }


    function showNotifications() {
        const summary = getSummary();

        const lowStockCount = Number(summary.lowStockCount) || 0;
        const pendingOrders = Number(summary.pendingOrders) || 0;

        const notifications = [];

        if (lowStockCount > 0) {
            notifications.push({
                type: "warning",
                icon: "fa-box-open",
                title: "Low Stock Alert",
                message: lowStockCount +
                    " inventory item(s) need restocking."
            });
        }

        if (pendingOrders > 0) {
            notifications.push({
                type: "info",
                icon: "fa-clock",
                title: "Pending Orders",
                message: pendingOrders +
                    " order(s) are waiting for processing."
            });
        }

        if (notifications.length === 0) {
            notifications.push({
                type: "success",
                icon: "fa-circle-check",
                title: "All Clear",
                message: "No low-stock or pending-order alerts were reported."
            });
        }


        const content = `
            <div class="notification-popup-content">
                <p class="notification-popup-subtitle">
                    Here's the latest information from your dashboard.
                </p>

                ${notifications.map(function (item) {
                    return `
                        <div class="notification-item notification-${item.type}">
                            <div class="notification-item-icon">
                                <i class="fa-solid ${item.icon}"></i>
                            </div>

                            <div>
                                <strong>${item.title}</strong>
                                <span>${item.message}</span>
                            </div>
                        </div>
                    `;
                }).join("")}
            </div>
        `;


        if (typeof window.Swal !== "undefined") {
            window.Swal.fire({
                title: "Notifications",
                html: content,
                confirmButtonText: "Close",
                customClass: {
                    popup: "printify-swal-popup",
                    title: "printify-swal-title",
                    htmlContainer: "printify-swal-content",
                    confirmButton: "printify-swal-confirm"
                },
                buttonsStyling: false,
                backdrop: "rgba(0, 8, 3, 0.30)",
                heightAuto: false
            });
        } else {
            alert(
                "PRINTIFY NOTIFICATIONS\n\n" +
                lowStockCount + " low-stock item(s)\n" +
                pendingOrders + " pending order(s)"
            );
        }
    }


    if (notificationButton) {
        notificationButton.addEventListener("click", showNotifications);
    }


    function updateNotificationDot() {
        const summary = getSummary();

        const hasAlerts =
            (Number(summary.lowStockCount) || 0) > 0 ||
            (Number(summary.pendingOrders) || 0) > 0;

        if (notificationDot) {
            notificationDot.style.display = hasAlerts ? "block" : "none";
        }
    }

    updateNotificationDot();


    /* =================================================
       SALES CHART - DAILY / WEEKLY / MONTHLY / YEARLY
    ================================================= */

    /*
       Actual backend response:

       summary.sales = {
           daily: {
               labels: [...],
               values: [...]
           },
           weekly: {
               labels: [...],
               values: [...]
           },
           monthly: {
               labels: [...],
               values: [...]
           },
           yearly: {
               labels: [...],
               values: [...]
           }
       }
    */


    /* DAILY DEFAULT */

    function ensureDailySalesOption() {
        if (!salesPeriod) return;

        const hasDaily = Array.from(salesPeriod.options).some(function (option) {
            return String(option.value).toLowerCase() === "daily";
        });

        if (!hasDaily) {
            const dailyOption = document.createElement("option");

            dailyOption.value = "daily";
            dailyOption.textContent = "Daily";

            salesPeriod.insertBefore(
                dailyOption,
                salesPeriod.firstElementChild
            );
        }

        // Always select Daily when dashboard opens.
        salesPeriod.value = "daily";
    }

    ensureDailySalesOption();


    /* =================================================
       GET CHART VALUES FROM BACKEND
    ================================================= */

    function getChartValues(period) {
        const summary = getSummary();

        const salesData = summary.sales || {};
        const selectedData = salesData[period] || {};

        const labels = Array.isArray(selectedData.labels)
            ? selectedData.labels
            : [];

        const values = Array.isArray(selectedData.values)
            ? selectedData.values
            : [];

        return {
            labels: labels.map(function (label) {
                return String(label == null ? "" : label);
            }),

            values: values.map(function (value) {
                return Number(value) || 0;
            })
        };
    }


    /* =================================================
       CHART COLORS
    ================================================= */

    function getChartColors() {
        const isDark = body.classList.contains("dark-mode");

        return {
            text: isDark ? "#cbd5ce" : "#ffffff",

            grid: isDark
                ? "rgba(255,255,255,0.09)"
                : "rgba(255,255,255,0.14)"
        };
    }


    /* =================================================
       UPDATE CHART THEME
    ================================================= */

    function updateChartTheme() {
        if (!salesChart) return;

        const colors = getChartColors();

        salesChart.options.scales.x.ticks.color = colors.text;
        salesChart.options.scales.y.ticks.color = colors.text;

        salesChart.options.scales.x.grid.color = colors.grid;
        salesChart.options.scales.y.grid.color = colors.grid;

        salesChart.update();
    }


    /* =================================================
       RENDER SALES CHART
    ================================================= */

    function renderSalesChart() {
        if (!salesChartCanvas) {
            console.warn("Sales chart canvas not found.");
            return;
        }

        if (typeof window.Chart === "undefined") {
            console.error("Chart.js is not loaded.");
            return;
        }

        const period = salesPeriod && salesPeriod.value
            ? salesPeriod.value.toLowerCase()
            : "daily";

        const chartData = getChartValues(period);
        const colors = getChartColors();


        /* Update existing chart */

        if (salesChart) {
            salesChart.data.labels = chartData.labels;

            salesChart.data.datasets[0].data = chartData.values;

            salesChart.options.scales.x.ticks.color = colors.text;
            salesChart.options.scales.y.ticks.color = colors.text;

            salesChart.options.scales.x.grid.color = colors.grid;
            salesChart.options.scales.y.grid.color = colors.grid;

            salesChart.update();

            return;
        }


        /* Create chart */

        salesChart = new Chart(salesChartCanvas, {
            type: "line",

            data: {
                labels: chartData.labels,

                datasets: [{
                    label: "Sales",

                    data: chartData.values,

                    borderColor: "#19e51b",
                    backgroundColor: "rgba(25, 229, 27, 0.12)",

                    borderWidth: 2.5,

                    fill: true,

                    tension: 0.4,

                    pointRadius: 4,
                    pointHoverRadius: 6,

                    pointBackgroundColor: "#19e51b",
                    pointBorderColor: "#ffffff",
                    pointBorderWidth: 1
                }]
            },

            options: {
                responsive: true,

                maintainAspectRatio: false,

                interaction: {
                    intersect: false,
                    mode: "index"
                },

                plugins: {
                    legend: {
                        display: false
                    },

                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                return "Sales: " +
                                    new Intl.NumberFormat("en-PH", {
                                        style: "currency",
                                        currency: "PHP"
                                    }).format(context.parsed.y || 0);
                            }
                        }
                    }
                },

                scales: {
                    x: {
                        ticks: {
                            color: colors.text,

                            font: {
                                family: "Inter",
                                size: 10
                            }
                        },

                        grid: {
                            display: false,
                            color: colors.grid
                        }
                    },

                    y: {
                        beginAtZero: true,

                        ticks: {
                            color: colors.text,

                            font: {
                                family: "Inter",
                                size: 10
                            },

                            callback: function (value) {
                                return "₱" +
                                    Number(value).toLocaleString("en-PH");
                            }
                        },

                        grid: {
                            color: colors.grid
                        }
                    }
                }
            }
        });

        console.log("Sales chart rendered:", period, chartData);
    }


    /* =================================================
       SALES PERIOD DROPDOWN
    ================================================= */

    if (salesPeriod) {
        salesPeriod.addEventListener("change", function () {
            renderSalesChart();
        });
    }

    renderSalesChart();


    /* =================================================
       ACTIVE SIDEBAR LINK
    ================================================= */

    const currentPage = window.location.pathname
        .split("/")
        .pop()
        .toLowerCase();

    document.querySelectorAll(".sidebar .nav-link").forEach(function (link) {
        const href = (link.getAttribute("href") || "").toLowerCase();

        link.classList.toggle("active", href === currentPage);
    });


    /* =================================================
       UPDATE AFTER DASHBOARD DATA LOADS
    ================================================= */

    window.addEventListener("printify:summary-updated", function () {
        updateNotificationDot();
        renderSalesChart();
    });


    console.log("Printify dashboard controller initialized.");

});
