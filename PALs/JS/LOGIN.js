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
const schoolEmailDomain = "@cubao.sti.edu.ph";

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

function setLoginView(view) {
    const showChoices = view === "choices";
    loginChoices.hidden = !showChoices;
    userLoginForm.hidden = view !== "user";
    adminLoginForm.hidden = view !== "admin";

    if (view === "user") userEmail.focus();
    if (view === "admin") adminEmail.focus();
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
    button.addEventListener("click", () => setLoginView("choices"));
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
    const emailValid = validateSchoolEmail(adminEmail, document.getElementById("adminEmailError"));
    adminLoginForm.querySelector(".login-submit").disabled = !adminEmail.value.trim() || !emailValid || !adminPassword.value;
    document.getElementById("adminLoginStatus").textContent = "";
});

adminPassword.addEventListener("input", () => {
    const emailValid = validateSchoolEmail(adminEmail, document.getElementById("adminEmailError"));
    adminLoginForm.querySelector(".login-submit").disabled = !adminEmail.value.trim() || !emailValid || !adminPassword.value;
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

    const signInEvent = new CustomEvent("pals:entra-sign-in", {
        cancelable: true,
        detail: { email: userEmail.value.trim() }
    });

    if (userLoginForm.dispatchEvent(signInEvent)) {
        userLoginStatus.textContent = "Microsoft Entra ID authentication must be configured before sign-in can continue.";
    }
});

adminLoginForm.addEventListener("submit", (event) => {
    event.preventDefault();
    const adminLoginStatus = document.getElementById("adminLoginStatus");
    if (!validateSchoolEmail(adminEmail, document.getElementById("adminEmailError"))) {
        adminEmail.reportValidity();
        return;
    }
    if (!adminPassword.value) {
        adminPassword.reportValidity();
        return;
    }

    const adminSignInEvent = new CustomEvent("pals:admin-sign-in", {
        cancelable: true,
        detail: {
            email: adminEmail.value.trim(),
            password: adminPassword.value
        }
    });

    const authenticationNotConfigured = adminLoginForm.dispatchEvent(adminSignInEvent);
    adminPassword.value = "";
    adminLoginForm.querySelector(".login-submit").disabled = true;

    if (authenticationNotConfigured) {
        adminLoginStatus.textContent = "Admin authentication is not configured. Set and verify admin credentials in a secure server-side authentication service.";
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