const loginDialog = document.getElementById("loginDialog");
const loginTrigger = document.getElementById("loginTrigger");
const closeLoginDialog = document.getElementById("closeLoginDialog");
const loginChoices = document.getElementById("loginChoices");
const adminLoginForm = document.getElementById("adminLoginForm");
const adminEmail = document.getElementById("adminEmail");
const adminPassword = document.getElementById("adminPassword");
const allowedEmailDomains = ["@cubao.sti.edu.ph", "@pals.me"];

function validateSchoolEmail(input, errorElement) {
    const email = input.value.trim();
    let error = "";

    if (
        email
        && !input.validity.typeMismatch
        && !allowedEmailDomains.some((domain) => email.toLowerCase().endsWith(domain))
    ) {
        error = "Use an authorized @cubao.sti.edu.ph or @pals.me account.";
    } else if (email && input.validity.typeMismatch) {
        error = "Enter a valid email address.";
    }

    input.setCustomValidity(error);
    errorElement.textContent = error;
    return !error && input.validity.valid;
}

function setLoginView(view) {
    loginChoices.hidden = view !== "choices";
    adminLoginForm.hidden = view !== "admin";
    if (view === "admin") adminEmail.focus();
}

loginTrigger.addEventListener("click", () => {
    setLoginView("choices");
    loginDialog.showModal();
});
closeLoginDialog.addEventListener("click", () => loginDialog.close());
document.getElementById("adminLoginChoice").addEventListener("click", () => setLoginView("admin"));
document.querySelectorAll(".login-back").forEach((button) => {
    button.addEventListener("click", () => setLoginView("choices"));
});

function updateSubmitState() {
    const emailValid = validateSchoolEmail(adminEmail, document.getElementById("adminEmailError"));
    adminLoginForm.querySelector(".login-submit").disabled = !adminEmail.value.trim() || !emailValid || !adminPassword.value;
    document.getElementById("adminLoginStatus").textContent = "";
}

adminEmail.addEventListener("input", updateSubmitState);
adminPassword.addEventListener("input", updateSubmitState);

adminLoginForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (!validateSchoolEmail(adminEmail, document.getElementById("adminEmailError"))) {
        adminEmail.reportValidity();
        return;
    }
    if (!adminPassword.value) {
        adminPassword.reportValidity();
        return;
    }

    const status = document.getElementById("adminLoginStatus");
    const submit = adminLoginForm.querySelector(".login-submit");
    submit.disabled = true;
    status.textContent = "Signing in…";
    try {
        const response = await fetch("/dev/login", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                email: adminEmail.value.trim(),
                password: adminPassword.value,
            }),
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) {
            status.textContent = result.error?.message ?? "Developer sign-in failed.";
            submit.disabled = false;
            return;
        }
        sessionStorage.setItem("pals.developer.email", result.data.email);
        window.location.assign("pals.html");
    } catch {
        status.textContent = "The developer authentication service is unavailable.";
        submit.disabled = false;
    } finally {
        adminPassword.value = "";
    }
});

loginDialog.addEventListener("cancel", (event) => {
    event.preventDefault();
    loginDialog.close();
});
loginDialog.addEventListener("click", (event) => {
    if (event.target === loginDialog) loginDialog.close();
});
