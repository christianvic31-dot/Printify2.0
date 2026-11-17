const forgotPasswordForm = document.getElementById("forgotPasswordForm");
const emailInput = document.getElementById("email");
const statusMessage = document.getElementById("statusMessage");
const submitButton = document.getElementById("submitButton");

forgotPasswordForm.addEventListener("submit", function (event) {
    event.preventDefault();

    const email = emailInput.value.trim();

    // Reset previous message
    statusMessage.textContent = "";
    statusMessage.className = "message";

    // Check if email is empty
    if (email === "") {
        showMessage("Please enter your email address.", "error");
        emailInput.focus();
        return;
    }

    // Basic email format validation
    const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

    if (!emailPattern.test(email)) {
        showMessage("Please enter a valid email address.", "error");
        emailInput.focus();
        return;
    }

    submitButton.disabled = true;
    submitButton.textContent = "Please wait...";

    fetch("/api/auth/forgot-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: email })
    })
        .then(function (response) {
            return response.json().then(function (data) {
                return { ok: response.ok, data: data };
            });
        })
        .then(function (result) {
            showMessage(
                (result.data && result.data.message) || "Unable to process the request.",
                result.ok ? "success" : "error"
            );
        })
        .catch(function () {
            showMessage("Unable to reach the server. Please try again.", "error");
        })
        .finally(function () {
            submitButton.disabled = false;
            submitButton.textContent = "Send Reset Instructions";
        });
});

function showMessage(message, type) {
    statusMessage.textContent = message;
    statusMessage.className = "message " + type;
}