
/* ==========================================
   PRINTIFY AUTH GUARD
   - Login and role verification
   - Server synchronization
   - Orders archive and restore API
   - Inventory archive and restore API
========================================== */

(function () {

    "use strict";

    var script = document.currentScript;
    var requiredRole = script
        ? script.getAttribute("data-role")
        : "";

    var ORDERS_KEY = "printifyOrders";
    var STAFF_KEY = "printifyStaff";
    var STAFF_ORDERS_KEY = "printify-staff-orders-demo";

    document.documentElement.style.visibility = "hidden";


    /* ==========================================
       API REQUEST
    ========================================== */

    function request(method, url, body) {

        var xhr = new XMLHttpRequest();

        try {

            xhr.open(method, url, false);
            xhr.withCredentials = true;

            if (body !== undefined) {
                xhr.setRequestHeader(
                    "Content-Type",
                    "application/json"
                );
            }

            xhr.send(
                body === undefined
                    ? null
                    : JSON.stringify(body)
            );

        } catch (error) {

            console.error("API request failed:", error);

            return {
                status: 0,
                data: null
            };

        }

        var data = null;

        try {
            data = JSON.parse(xhr.responseText);
        } catch (error) {
            data = null;
        }

        return {
            status: xhr.status,
            data: data
        };

    }


    function homeFor(role) {

        return role === "Admin"
            ? "dashboard.html"
            : "staff-dashboard.html";

    }


    function clearMirrors() {

        [
            ORDERS_KEY,
            STAFF_KEY,
            STAFF_ORDERS_KEY,
            "printify-user-name",
            "printify-user-email",
            "staffName",
            "staffEmail"
        ].forEach(function (key) {

            try {
                localStorage.removeItem(key);
            } catch (error) {
                // Ignore storage errors
            }

        });

    }


    /* ==========================================
       STATUS CONVERSION
    ========================================== */

    function toStaffStatus(status) {

        return status === "In Progress"
            ? "Processing"
            : status;

    }


    function toServerStatus(status) {

        return status === "Processing"
            ? "In Progress"
            : status;

    }


    /* ==========================================
       CHECK LOGIN
    ========================================== */

    var me = request("GET", "/api/auth/me");

    if (me.status !== 200 || !me.data) {

        clearMirrors();

        window.location.replace("login.html");

        return;

    }

    var user = me.data;

    if (requiredRole && user.role !== requiredRole) {

        window.location.replace(
            homeFor(user.role)
        );

        return;

    }


    /* ==========================================
       ORDER DATA
    ========================================== */

    var ordersSnapshot = {};


    function snapshotOf(order) {

        return JSON.stringify([

            order.customerName,
            order.customerContact,
            order.printingService,

            Number(order.quantity),
            Number(order.unitPrice),
            Number(order.amountPaid),

            order.orderDate,
            order.dueDate,

            order.orderStatus,
            order.paymentStatus,

            order.notes || "",
            order.assignedStaffId || null

        ]);

    }


    function orderPayload(order) {

        return {

            id: order.id,

            customerName: order.customerName,
            customerContact: order.customerContact || "",

            printingService: order.printingService,

            quantity: Number(order.quantity),
            unitPrice: Number(order.unitPrice),
            amountPaid: Number(order.amountPaid) || 0,

            orderDate: order.orderDate || "",
            dueDate: order.dueDate || "",

            orderStatus: order.orderStatus,
            paymentStatus: order.paymentStatus,

            notes: order.notes || "",

            assignedStaffId:
                order.assignedStaffId || null

        };

    }


    /* ==========================================
       LOAD ACTIVE ADMIN ORDERS
    ========================================== */

    function loadAdminOrders() {

        var result = request(
            "GET",
            "/api/orders"
        );

        if (
            result.status !== 200 ||
            !result.data
        ) {
            return null;
        }

        var items = result.data.items || [];

        ordersSnapshot = {};

        items.forEach(function (order) {

            ordersSnapshot[order.id] =
                snapshotOf(order);

        });

        try {

            localStorage.setItem(
                ORDERS_KEY,
                JSON.stringify(items)
            );

        } catch (error) {
            // Ignore storage errors
        }

        return items;

    }


    /* ==========================================
       LOAD ARCHIVED ORDERS
    ========================================== */

    function loadArchivedOrders() {

        var result = request(
            "GET",
            "/api/orders/archived"
        );

        if (
            result.status !== 200 ||
            !result.data
        ) {
            return null;
        }

        return result.data.items || [];

    }


    /* ==========================================
       ARCHIVE ORDER
    ========================================== */

    function archiveOrder(id) {

        if (!id) {

            return {
                ok: false,
                message: "Invalid order ID."
            };

        }

        var result = request(
            "DELETE",
            "/api/orders/" + encodeURIComponent(id)
        );

        var success =
            result.status >= 200 &&
            result.status < 300;

        if (success) {

            delete ordersSnapshot[id];

            return {
                ok: true,
                message:
                    result.data?.message ||
                    "Order moved to Archived Orders."
            };

        }

        return {
            ok: false,
            message:
                (result.data && result.data.message) ||
                "Unable to archive the order."
        };

    }


    /* ==========================================
       RESTORE ARCHIVED ORDER
    ========================================== */

    function restoreOrder(id) {

        if (!id) {

            return {
                ok: false,
                message: "Invalid order ID."
            };

        }

        var result = request(
            "POST",
            "/api/orders/" +
            encodeURIComponent(id) +
            "/restore"
        );

        var success =
            result.status >= 200 &&
            result.status < 300;

        if (success) {

            if (
                result.data &&
                result.data.order
            ) {

                ordersSnapshot[id] =
                    snapshotOf(result.data.order);

            }

            return {
                ok: true,
                message:
                    result.data?.message ||
                    "Order restored successfully."
            };

        }

        return {
            ok: false,
            message:
                (result.data && result.data.message) ||
                "Unable to restore the order."
        };

    }


    /* ==========================================
       PERMANENT DELETE ORDER
    ========================================== */

    function permanentlyDeleteOrder(id) {

        if (!id) {

            return {
                ok: false,
                message: "Invalid order ID."
            };

        }

        var result = request(
            "DELETE",
            "/api/orders/" +
            encodeURIComponent(id) +
            "/permanent"
        );

        var success =
            result.status >= 200 &&
            result.status < 300;

        if (success) {

            delete ordersSnapshot[id];

            return {
                ok: true,
                message:
                    result.data?.message ||
                    "Order permanently deleted."
            };

        }

        return {
            ok: false,
            message:
                (result.data && result.data.message) ||
                "Unable to permanently delete the order."
        };

    }


    /* ==========================================
       LOAD STAFF LIST
    ========================================== */

    function loadStaffList() {

        var result = request(
            "GET",
            "/api/staff"
        );

        if (
            result.status !== 200 ||
            !result.data
        ) {
            return null;
        }

        var items = result.data.items || [];

        try {

            localStorage.setItem(
                STAFF_KEY,
                JSON.stringify(items)
            );

        } catch (error) {
            // Ignore storage errors
        }

        return items;

    }


    /* ==========================================
       LOAD STAFF ORDERS
    ========================================== */

    function loadMyOrders() {

        var result = request(
            "GET",
            "/api/orders/mine"
        );

        if (
            result.status !== 200 ||
            !result.data
        ) {
            return null;
        }

        var items = (result.data.items || []).map(
            function (order) {

                return {

                    id: order.id,

                    customer: order.customerName,
                    contact: order.customerContact,

                    product: order.printingService,

                    quantity: order.quantity,

                    date: order.orderDate,
                    dueDate: order.dueDate,

                    status: toStaffStatus(
                        order.orderStatus
                    ),

                    payment: order.paymentStatus,

                    notes: order.notes,

                    updatedAt: order.updatedAt

                };

            }
        );

        try {

            localStorage.setItem(
                STAFF_ORDERS_KEY,
                JSON.stringify(items)
            );

        } catch (error) {
            // Ignore storage errors
        }

        return items;

    }


    /* ==========================================
       INVENTORY DATA
    ========================================== */

    var inventorySnapshot = {};


    function inventorySnap(product) {

        return JSON.stringify([

            product.name,
            product.category || "",

            Number(product.quantity),

            product.unit || "",

            Number(product.price),
            Number(product.threshold)

        ]);

    }


    function inventoryPayload(product) {

        return {

            id: product.id,

            name: product.name,
            category: product.category || "",

            quantity:
                Number(product.quantity) || 0,

            unit: product.unit || "",

            price:
                Number(product.price) || 0,

            threshold:
                Number(product.threshold) || 0

        };

    }


    /* ==========================================
       LOAD ACTIVE INVENTORY
    ========================================== */

    function loadInventory() {

        var result = request(
            "GET",
            "/api/inventory"
        );

        if (
            result.status !== 200 ||
            !result.data
        ) {
            return null;
        }

        var items = result.data.items || [];

        inventorySnapshot = {};

        items.forEach(function (item) {

            inventorySnapshot[item.id] =
                inventorySnap(item);

        });

        return items;

    }


    /* ==========================================
       LOAD ARCHIVED INVENTORY
    ========================================== */

    function loadArchivedInventory() {

        var result = request(
            "GET",
            "/api/inventory/archived"
        );

        if (
            result.status !== 200 ||
            !result.data
        ) {
            return null;
        }

        return result.data.items || [];

    }


    /* ==========================================
       ARCHIVE INVENTORY
    ========================================== */

    function archiveInventory(id) {

        if (!id) {

            return {
                ok: false,
                message: "Invalid product ID."
            };

        }

        var result = request(
            "DELETE",
            "/api/inventory/" +
            encodeURIComponent(id)
        );

        var success =
            result.status >= 200 &&
            result.status < 300;

        if (success) {

            delete inventorySnapshot[id];

            return {
                ok: true,
                message:
                    (result.data && result.data.message) ||
                    "Product moved to Archived Products."
            };

        }

        return {
            ok: false,
            message:
                (result.data && result.data.message) ||
                "Unable to archive the product."
        };

    }


    /* ==========================================
       RESTORE ARCHIVED INVENTORY
    ========================================== */

    function restoreInventory(id) {

        if (!id) {

            return {
                ok: false,
                message: "Invalid product ID."
            };

        }

        var result = request(
            "POST",
            "/api/inventory/" +
            encodeURIComponent(id) +
            "/restore"
        );

        var success =
            result.status >= 200 &&
            result.status < 300;

        if (success) {

            // Refresh active inventory snapshot.
            loadInventory();

            return {
                ok: true,
                message:
                    (result.data && result.data.message) ||
                    "Product restored successfully."
            };

        }

        return {
            ok: false,
            message:
                (result.data && result.data.message) ||
                "Unable to restore the product."
        };

    }


    /* ==========================================
       PERMANENT DELETE INVENTORY
    ========================================== */

    function permanentlyDeleteInventory(id) {

        if (!id) {

            return {
                ok: false,
                message: "Invalid product ID."
            };

        }

        var result = request(
            "DELETE",
            "/api/inventory/" +
            encodeURIComponent(id) +
            "/permanent"
        );

        var success =
            result.status >= 200 &&
            result.status < 300;

        if (success) {

            delete inventorySnapshot[id];

            return {
                ok: true,
                message:
                    (result.data && result.data.message) ||
                    "Product permanently deleted."
            };

        }

        return {
            ok: false,
            message:
                (result.data && result.data.message) ||
                "Unable to permanently delete the product."
        };

    }


    /* ==========================================
       PUBLIC AUTH OBJECT
    ========================================== */

    window.PrintifyAuth = {
        user: user
    };


    /* ==========================================
       PUBLIC API OBJECT
    ========================================== */

    window.PrintifyAPI = {

        request: request,


        /* ---------- LOGOUT ---------- */

        logout: function () {

            request(
                "POST",
                "/api/auth/logout"
            );

            clearMirrors();

            window.location.replace(
                "login.html"
            );

        },


        /* ======================================
           SYNC ORDERS

           Hindi mag-a-archive ng order
           kapag wala ito sa local array.

           Explicit archiveOrder() lang
           ang puwedeng mag-archive.
        ====================================== */

        syncOrders: function (orders) {

            if (!Array.isArray(orders)) {

                return {
                    ok: false,
                    message: "Invalid orders data."
                };

            }

            for (
                var i = 0;
                i < orders.length;
                i++
            ) {

                var order = orders[i];

                if (!order || !order.id) {
                    continue;
                }

                var current = snapshotOf(order);

                var existed =
                    Object.prototype.hasOwnProperty.call(
                        ordersSnapshot,
                        order.id
                    );

                if (
                    existed &&
                    ordersSnapshot[order.id] === current
                ) {
                    continue;
                }

                var result = existed

                    ? request(
                        "PUT",
                        "/api/orders/" +
                        encodeURIComponent(order.id),
                        orderPayload(order)
                    )

                    : request(
                        "POST",
                        "/api/orders",
                        orderPayload(order)
                    );


                if (
                    result.status < 200 ||
                    result.status >= 300
                ) {

                    return {

                        ok: false,

                        message:
                            (result.data &&
                                result.data.message) ||
                            "Unable to save the order to the server."

                    };

                }


                if (
                    result.data &&
                    typeof result.data === "object"
                ) {

                    Object.assign(
                        order,
                        result.data
                    );

                }


                ordersSnapshot[order.id] =
                    snapshotOf(order);

            }


            // Walang automatic DELETE loop.
            // Explicit archiveOrder() ang gagamitin.


            return {
                ok: true
            };

        },


        /* ---------- ORDER FUNCTIONS ---------- */

        fetchOrders: loadAdminOrders,

        fetchArchivedOrders: loadArchivedOrders,

        archiveOrder: archiveOrder,

        restoreOrder: restoreOrder,

        permanentlyDeleteOrder:
            permanentlyDeleteOrder,


        /* ---------- STAFF ---------- */

        fetchStaff: loadStaffList,


        updateOrderStatus: function (id, status) {

            var result = request(

                "PATCH",

                "/api/orders/" +
                encodeURIComponent(id) +
                "/status",

                {
                    status: toServerStatus(status)
                }

            );

            return {

                ok:
                    result.status >= 200 &&
                    result.status < 300,

                message:
                    result.data &&
                    result.data.message

            };

        },


        fetchMyOrders: loadMyOrders,


        /* ======================================
           INVENTORY FUNCTIONS
        ====================================== */

        fetchInventory: loadInventory,

        fetchArchivedInventory: loadArchivedInventory,

        archiveInventory: archiveInventory,

        restoreInventory: restoreInventory,

        permanentlyDeleteInventory:
            permanentlyDeleteInventory,


        /* ======================================
           SYNC INVENTORY

           Hindi mag-a-archive ng product
           kapag wala ito sa local array.

           Explicit archiveInventory() lang
           ang puwedeng mag-archive.
        ====================================== */

        syncInventory: function (products) {

            if (!Array.isArray(products)) {

                return {
                    ok: false,
                    message: "Invalid inventory data."
                };

            }


            for (
                var i = 0;
                i < products.length;
                i++
            ) {

                var item = products[i];

                if (!item || !item.id) {
                    continue;
                }

                var current = inventorySnap(item);

                var existed =
                    Object.prototype.hasOwnProperty.call(
                        inventorySnapshot,
                        item.id
                    );

                if (
                    existed &&
                    inventorySnapshot[item.id] === current
                ) {
                    continue;
                }


                var result = existed

                    ? request(
                        "PUT",
                        "/api/inventory/" +
                        encodeURIComponent(item.id),
                        inventoryPayload(item)
                    )

                    : request(
                        "POST",
                        "/api/inventory",
                        inventoryPayload(item)
                    );


                if (
                    result.status < 200 ||
                    result.status >= 300
                ) {

                    return {

                        ok: false,

                        message:
                            (result.data &&
                                result.data.message) ||
                            "Unable to save the item to the server."

                    };

                }


                inventorySnapshot[item.id] = current;

            }


            // Walang automatic DELETE loop dito.
            // Hindi dapat ma-archive ang product
            // dahil lamang wala ito sa local array.


            return {
                ok: true
            };

        }

    };


    /* ==========================================
       LOAD DATA BASED ON USER ROLE
    ========================================== */

    if (user.role === "Admin") {

        loadAdminOrders();

        loadStaffList();

    } else {

        loadMyOrders();

        try {

            localStorage.setItem(
                "printify-user-name",
                user.fullName ||
                user.username ||
                "Staff Member"
            );

            localStorage.setItem(
                "printify-user-email",
                user.email || ""
            );

        } catch (error) {
            // Ignore storage errors
        }

    }


    /* ==========================================
       SHOW PAGE
    ========================================== */

    document.documentElement.style.visibility = "";

})();
