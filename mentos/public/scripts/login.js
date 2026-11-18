const loginDialog = document.getElementById("loginDialog");
const loginTrigger = document.getElementById("loginTrigger");
const closeLoginDialog = document.getElementById("closeLoginDialog");
const loginChoices = document.getElementById("loginChoices");
const userLoginForm = document.getElementById("userLoginForm");
const adminLoginForm = document.getElementById("adminLoginForm");
const userEmail = document.getElementById("userEmail");
const acceptTerms = document.getElementById("acceptTerms");
const userLoginSubmit = userLoginForm.querySelector(".login-submit");
const termsDialog = document.getElementById("termsDialog");
const adminEmail = document.getElementById("adminEmail");
const adminPassword = document.getElementById("adminPassword");
const adminMfaLabel = document.getElementById("adminMfaLabel");
const adminMfaCode = document.getElementById("adminMfaCode");
const adminMfaHint = document.getElementById("adminMfaHint");
const schoolEmailDomain = "@cubao.sti.edu.ph";
let adminMfaRequired = false;

function validateSchoolEmail(input, errorElement) {
    const email = input.value.trim();
    let error = "";

    if (email && !input.validity.typeMismatch && !email.toLowerCase().endsWith(schoolEmailDomain)) {
        error = "Use your school email ending in @cubao.sti.edu.ph.";
    } else if (email && input.validity.typeMismatch) {
        error = "Enter a valid email address.";
    }

    input.setCustomValidity(error);
    errorElement.textContent = error;
    return !error && input.validity.valid;
}

function validateAdminEmail(input, errorElement) {
    const email = input.value.trim().toLowerCase();
    const valid = input.validity.valid && (email.endsWith("@cubao.sti.edu.ph") || email.endsWith("@pals.me"));
    const error = valid || !email ? "" : "Use an authorized PALs administrator email.";
    input.setCustomValidity(error);
    errorElement.textContent = error;
    return !error && input.validity.valid;
}

function setLoginView(view) {
    const showChoices = view === "choices";
    loginChoices.hidden = !showChoices;
    userLoginForm.hidden = view !== "user";
    adminLoginForm.hidden = view !== "admin";

    if (view === "user") userEmail.focus();
    if (view === "admin") adminEmail.focus();
}

function setAdminMfaRequired(required) {
    adminMfaRequired = required;
    adminMfaLabel.hidden = !required;
    adminMfaCode.hidden = !required;
    adminMfaHint.hidden = !required;
    adminMfaCode.required = required;
    if (required) adminMfaCode.focus();
}

loginTrigger.addEventListener("click", () => {
    setLoginView("choices");
    loginDialog.showModal();
});
closeLoginDialog.addEventListener("click", () => loginDialog.close());
document.getElementById("openTerms").addEventListener("click", (event) => {
    event.preventDefault();
    termsDialog.showModal();
});
document.getElementById("closeTerms").addEventListener("click", () => termsDialog.close());
document.getElementById("closeTermsConfirm").addEventListener("click", () => termsDialog.close());
termsDialog.addEventListener("cancel", (event) => {
    event.preventDefault();
    termsDialog.close();
});

document.getElementById("userLoginChoice").addEventListener("click", () => setLoginView("user"));
document.getElementById("adminLoginChoice").addEventListener("click", () => setLoginView("admin"));
document.querySelectorAll(".login-back").forEach((button) => {
    button.addEventListener("click", () => {
        setAdminMfaRequired(false);
        adminMfaCode.value = "";
        setLoginView("choices");
    });
});

userEmail.addEventListener("input", () => {
    const valid = validateSchoolEmail(userEmail, document.getElementById("userEmailError"));
    userLoginSubmit.disabled = !userEmail.value.trim() || !valid || !acceptTerms.checked;
    document.getElementById("userLoginStatus").textContent = "";
});

acceptTerms.addEventListener("change", () => {
    const valid = validateSchoolEmail(userEmail, document.getElementById("userEmailError"));
    userLoginSubmit.disabled = !userEmail.value.trim() || !valid || !acceptTerms.checked;
    document.getElementById("userLoginStatus").textContent = "";
});

adminEmail.addEventListener("input", () => {
    const emailValid = validateAdminEmail(adminEmail, document.getElementById("adminEmailError"));
    adminLoginForm.querySelector(".login-submit").disabled = !adminEmail.value.trim() || !emailValid || !adminPassword.value;
    document.getElementById("adminLoginStatus").textContent = "";
});

adminPassword.addEventListener("input", () => {
    const emailValid = validateAdminEmail(adminEmail, document.getElementById("adminEmailError"));
    const mfaReady = !adminMfaRequired || adminMfaCode.value.replaceAll(" ", "").length >= 6;
    adminLoginForm.querySelector(".login-submit").disabled = !adminEmail.value.trim() || !emailValid || (!adminMfaRequired && !adminPassword.value) || !mfaReady;
    document.getElementById("adminLoginStatus").textContent = "";
});

adminMfaCode.addEventListener("input", () => {
    const emailValid = validateAdminEmail(adminEmail, document.getElementById("adminEmailError"));
    const mfaReady = adminMfaCode.value.replaceAll(" ", "").length >= 6;
    adminLoginForm.querySelector(".login-submit").disabled = !adminEmail.value.trim() || !emailValid || !mfaReady;
    document.getElementById("adminLoginStatus").textContent = "";
});

userLoginForm.addEventListener("submit", (event) => {
    event.preventDefault();
    const userLoginStatus = document.getElementById("userLoginStatus");
    if (!validateSchoolEmail(userEmail, document.getElementById("userEmailError")) || !acceptTerms.checked) {
        if (!acceptTerms.checked) acceptTerms.reportValidity();
        userEmail.reportValidity();
        return;
    }

    userLoginSubmit.disabled = true;
    fetch("/api/v1/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({ email: userEmail.value.trim(), termsAccepted: true })
    }).then(async response => {
        const body = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(body?.error?.message || "Sign-in failed.");
        window.location.assign("pals.html");
    }).catch(error => {
        userLoginStatus.textContent = error.message;
        userLoginSubmit.disabled = false;
    });
});

adminLoginForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const adminLoginStatus = document.getElementById("adminLoginStatus");
    const adminAddress = adminEmail.value.trim().toLowerCase();
    const adminEmailError = document.getElementById("adminEmailError");
    const adminValid = adminEmail.validity.valid && (adminAddress.endsWith("@cubao.sti.edu.ph") || adminAddress.endsWith("@pals.me"));
    adminEmail.setCustomValidity(adminValid ? "" : "Use an authorized PALs administrator email.");
    adminEmailError.textContent = adminValid ? "" : "Use an authorized PALs administrator email.";
    if (!adminValid) {
        adminEmail.reportValidity();
        return;
    }
    if (!adminPassword.value) {
        adminPassword.reportValidity();
        return;
    }

    const submitButton = adminLoginForm.querySelector(".login-submit");
    submitButton.disabled = true;
    try {
        const response = await fetch("/api/v1/auth/admin/login", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "same-origin",
            body: JSON.stringify({
                email: adminAddress,
                password: adminPassword.value,
                mfaCode: adminMfaRequired ? adminMfaCode.value : undefined,
            })
        });
        const body = await response.json().catch(() => ({}));
        if (!response.ok) {
            if (body?.error?.code === "mfa_required") {
                setAdminMfaRequired(true);
                adminLoginStatus.textContent = "Enter the current code from your authenticator app.";
                submitButton.disabled = true;
                return;
            }
            throw new Error(body?.error?.message || "Administrator sign-in failed.");
        }
        window.location.assign("pals.html");
    } catch (error) {
        adminLoginStatus.textContent = error.message;
        submitButton.disabled = false;
    } finally {
        if (!adminMfaRequired) adminPassword.value = "";
    }
});

loginDialog.addEventListener("cancel", (event) => {
    event.preventDefault();
    loginDialog.close();
});
loginDialog.addEventListener("click", (event) => {
    if (event.target === loginDialog) {
        loginDialog.close();
    }
});
