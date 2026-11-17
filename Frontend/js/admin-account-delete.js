document.addEventListener("DOMContentLoaded", () => {
  const form = document.getElementById("deleteAdminAccountForm");
  if (!form) return;
  const message = document.getElementById("deleteAdminAccountMessage");
  const button = document.getElementById("deleteAdminAccountButton");
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    const confirmation = document.getElementById("deleteAdminConfirmation").value;
    const password = document.getElementById("deleteAdminPassword").value;
    if (confirmation !== "DELETE MY ADMIN ACCOUNT") {
      message.textContent = "Type the exact confirmation phrase.";
      return;
    }
    if (!window.confirm("Permanently delete your Admin account? You will be signed out. This cannot be undone.")) return;
    button.disabled = true;
    message.textContent = "Deleting Admin account...";
    try {
      const response = await fetch("/api/auth/delete-admin-account", {
        method: "POST", credentials: "same-origin",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ password, confirmation })
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.message || "Could not delete Admin account.");
      window.location.replace("login.html");
    } catch (error) {
      message.textContent = error.message;
      button.disabled = false;
    }
  });
});