(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", async function () {
        const message = document.getElementById("attendanceMessage");
        const details = document.getElementById("attendanceDetails");
        const url = new URL(window.location.href);
        const staffCode = url.searchParams.get("staffCode") || "";

        window.history.replaceState({}, document.title, url.pathname);

        if (!staffCode) {
            message.textContent = "This attendance QR code is invalid.";
            message.classList.add("error");
            return;
        }

        try {
            const response = await fetch("/api/staff/me/attendance/qr-scan", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                credentials: "same-origin",
                body: JSON.stringify({ staffCode: staffCode })
            });
            const responseText = await response.text();
            const result = responseText ? JSON.parse(responseText) : {};

            if (!response.ok) {
                throw new Error(response.status === 401
                    ? "Sign in to your staff account on this phone first."
                    : result.message || "Unable to record attendance.");
            }

            message.textContent = "Attendance recorded successfully.";
            message.classList.add("success");
            details.textContent = staffCode + " · Clock in: " + result.timeIn;
        } catch (error) {
            message.textContent = error.message || "Unable to record attendance.";
            message.classList.add("error");
        }
    });
})();
