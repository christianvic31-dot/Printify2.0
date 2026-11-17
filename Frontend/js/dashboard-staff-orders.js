/* =====================================================
   PRINTIFY - DASHBOARD ORDERS PER STAFF
   Displays the order count of every staff member.
===================================================== */

document.addEventListener("DOMContentLoaded", function () {
    "use strict";

    const body = document.getElementById("staffOrdersBody");
    const subtitle = document.getElementById("staffOrdersSubtitle");

    if (!body) return;

    let refreshInProgress = false;

    /* =================================================
       HELPER FUNCTIONS
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

    function readStorage(key) {
        try {
            const value = JSON.parse(localStorage.getItem(key) || "[]");
            return Array.isArray(value) ? value : [];
        } catch (error) {
            console.warn("Unable to read " + key, error);
            return [];
        }
    }

    function initials(name) {
        return String(name || "?")
            .trim()
            .split(/\s+/)
            .slice(0, 2)
            .map(function (part) {
                return part.charAt(0).toUpperCase();
            })
            .join("");
    }

    function getStaffId(member) {
        return member.id || member.staffId || member.userId || "—";
    }

    function getOrderStaffId(order) {
        return order.assignedStaffId ||
            order.staffId ||
            order.assignedTo ||
            null;
    }

    function getOrderStatus(order) {
        return String(
            order.orderStatus ||
            order.status ||
            ""
        ).toLowerCase();
    }

    function isSameId(first, second) {
        return String(first) === String(second);
    }


    /* =================================================
       CALCULATE STAFF ORDER COUNTS
    ================================================= */

    function calculateOrders(member, orders) {
        const staffId = getStaffId(member);

        const assignedOrders = orders.filter(function (order) {
            const assignedId = getOrderStaffId(order);

            return assignedId &&
                isSameId(assignedId, staffId);
        });

        let pending = 0;
        let inProgress = 0;
        let completed = 0;

        assignedOrders.forEach(function (order) {
            const status = getOrderStatus(order);

            if (status === "pending") {
                pending++;
            } else if (
                status === "processing" ||
                status === "in progress" ||
                status === "in-progress"
            ) {
                inProgress++;
            } else if (status === "completed") {
                completed++;
            }
        });

        return {
            total: assignedOrders.length,
            pending: pending,
            inProgress: inProgress,
            completed: completed
        };
    }


    /* =================================================
       RENDER TABLE
    ================================================= */

    function render() {
        const staff = readStorage("printifyStaff");
        const orders = readStorage("printifyOrders");

        if (staff.length === 0) {
            body.innerHTML = `
                <tr>
                    <td colspan="6">
                        No staff members yet. Add staff in Staff Management.
                    </td>
                </tr>
            `;

            if (subtitle) {
                subtitle.textContent = "No staff records available.";
            }

            return;
        }

        const staffRows = staff.map(function (member) {
            return {
                member: member,
                counts: calculateOrders(member, orders)
            };
        });

        staffRows.sort(function (first, second) {
            return second.counts.total - first.counts.total;
        });

        body.innerHTML = staffRows.map(function (row) {
            const member = row.member;
            const counts = row.counts;

            const name = member.name ||
                member.fullName ||
                member.username ||
                "Unnamed Staff";

            const position = member.position ||
                member.role ||
                "Staff";

            return `
                <tr>
                    <td>
                        <div class="table-staff">
                            <div class="table-avatar avatar-purple">
                                ${escapeHTML(initials(name))}
                            </div>

                            <div>
                                <strong>${escapeHTML(name)}</strong>
                                <span>${escapeHTML(position)}</span>
                            </div>
                        </div>
                    </td>

                    <td>${escapeHTML(getStaffId(member))}</td>

                    <td>
                        <strong>${counts.total}</strong>
                    </td>

                    <td>${counts.pending}</td>

                    <td>${counts.inProgress}</td>

                    <td>${counts.completed}</td>
                </tr>
            `;
        }).join("");

        if (subtitle) {
            const unassigned = orders.filter(function (order) {
                const status = getOrderStatus(order);

                return !getOrderStaffId(order) &&
                    status !== "cancelled";
            }).length;

            subtitle.textContent = unassigned > 0
                ? unassigned + " order(s) are not assigned to any staff yet."
                : "How many orders each staff member has received.";
        }
    }


    /* =================================================
       REFRESH DATA
    ================================================= */

    async function refreshData() {
        if (refreshInProgress || document.hidden) return;

        if (!window.PrintifyAPI) {
            render();
            return;
        }

        refreshInProgress = true;

        try {
            const tasks = [];

            if (typeof window.PrintifyAPI.fetchStaff === "function") {
                tasks.push(
                    Promise.resolve(window.PrintifyAPI.fetchStaff())
                );
            }

            if (typeof window.PrintifyAPI.fetchOrders === "function") {
                tasks.push(
                    Promise.resolve(window.PrintifyAPI.fetchOrders())
                );
            }

            await Promise.allSettled(tasks);

            render();

        } catch (error) {
            console.error("Unable to refresh staff orders:", error);
            render();

        } finally {
            refreshInProgress = false;
        }
    }


    /* =================================================
       INITIAL LOAD AND AUTO REFRESH
    ================================================= */

    render();

    refreshData();

    window.setInterval(function () {
        if (!document.hidden) {
            refreshData();
        }
    }, 20000);

    window.addEventListener("storage", function (event) {
        if (
            event.key === "printifyStaff" ||
            event.key === "printifyOrders"
        ) {
            render();
        }
    });

    console.log("Printify staff orders initialized.");
});