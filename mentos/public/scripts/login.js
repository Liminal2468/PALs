async function initializeLogin() {
    const componentVersion = "20261122-password-otp";
    const [loginComponent, verifyComponent] = await Promise.all([
        fetch(`/components/login/modal.login.htm?v=${componentVersion}`, { cache: "no-store" }).then(response => response.ok ? response.text() : Promise.reject(new Error("Login component unavailable."))),
        fetch(`/components/login/modal.verify.html?v=${componentVersion}`, { cache: "no-store" }).then(response => response.ok ? response.text() : Promise.reject(new Error("Verification component unavailable.")))
    ]);
    document.getElementById("loginComponent").innerHTML = loginComponent;
    document.getElementById("verifyComponent").innerHTML = verifyComponent;

const loginDialog = document.getElementById("loginDialog");
const loginTrigger = document.getElementById("loginTrigger");
const closeLoginDialog = document.getElementById("closeLoginDialog");
const loginChoices = document.getElementById("loginChoices");
const userLoginForm = document.getElementById("userLoginForm");
const studentPasswordLoginForm = document.getElementById("studentPasswordLoginForm");
const adminLoginForm = document.getElementById("adminLoginForm");
const userEmail = document.getElementById("userEmail");
const acceptTerms = document.getElementById("acceptTerms");
const userLoginSubmit = userLoginForm.querySelector(".login-submit");
const studentPasswordEmail = document.getElementById("studentPasswordEmail");
const studentPassword = document.getElementById("studentPassword");
const studentPasswordSubmit = document.getElementById("studentPasswordSubmit");
const termsDialog = document.getElementById("termsDialog");
const adminEmail = document.getElementById("adminEmail");
const adminPassword = document.getElementById("adminPassword");
const adminMfaLabel = document.getElementById("adminMfaLabel");
const adminMfaCode = document.getElementById("adminMfaCode");
const adminMfaHint = document.getElementById("adminMfaHint");
const verifyDialog = document.getElementById("verifyDialog");
const verifyForm = document.getElementById("verifyForm");
const verifyCode = document.getElementById("verifyCode");
const verifyResend = document.getElementById("verifyResend");
const verifyResendStatus = document.getElementById("verifyResendStatus");
const passwordSetupDialog = document.getElementById("passwordSetupDialog");
const passwordSetupForm = document.getElementById("passwordSetupForm");
const schoolEmailDomain = "@cubao.sti.edu.ph";
let adminMfaRequired = false;
let verificationCodeLength = 6;
let resendTimer = null;
let verificationEmail = "";

function setVerificationCodeLength(length) {
    verificationCodeLength = length;
    verifyCode.pattern = `[0-9]{${length}}`;
    verifyCode.minLength = length;
    verifyCode.maxLength = length;
    verifyCode.placeholder = `Enter your ${length}-digit code here`;
    document.getElementById("verifyDialogDescription").textContent =
        `Enter the ${length}-digit code sent to your school email.`;
}

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
    studentPasswordLoginForm.hidden = view !== "studentPassword";
    adminLoginForm.hidden = view !== "admin";

    if (view === "user") {
        const valid = validateSchoolEmail(userEmail, document.getElementById("userEmailError"));
        userLoginSubmit.disabled = !userEmail.value.trim() || !valid || !acceptTerms.checked;
        userEmail.focus();
    }
    if (view === "studentPassword") {
        studentPasswordEmail.value = userEmail.value.trim();
        studentPassword.focus();
    }
    if (view === "admin") adminEmail.focus();
}

function startResendCountdown() {
    if (resendTimer) clearInterval(resendTimer);
    let remaining = 30;
    verifyResend.disabled = true;
    verifyResend.textContent = `Resend code in ${remaining} seconds`;
    verifyResendStatus.textContent = "";
    resendTimer = setInterval(() => {
        remaining -= 1;
        if (remaining <= 0) {
            clearInterval(resendTimer);
            resendTimer = null;
            verifyResend.disabled = false;
            verifyResend.textContent = "Resend code";
            return;
        }
        verifyResend.textContent = `Resend code in ${remaining} ${remaining === 1 ? "second" : "seconds"}`;
    }, 1000);
}

function stopResendCountdown() {
    if (resendTimer) clearInterval(resendTimer);
    resendTimer = null;
    verifyResend.disabled = true;
}

async function requestVerificationCode(email) {
    const response = await fetch("/api/v1/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({ email, termsAccepted: true })
    });
    const body = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(body?.error?.message || "Sign-in failed.");
    if (body.passwordRequired) return true;
    configureVerificationAttempt(body, email);
    return false;
}

function configureVerificationAttempt(body, email) {
    if (!body.verificationRequired) throw new Error("The authentication service did not start a verification attempt.");
    if (!Number.isInteger(body.codeLength) || body.codeLength !== 6) {
        throw new Error("The authentication service returned an invalid verification code length.");
    }
    verificationEmail = email;
    setVerificationCodeLength(body.codeLength);
    startResendCountdown();
}

function showVerificationDialog() {
    loginDialog.close();
    verifyCode.value = "";
    document.getElementById("verifyCodeError").textContent = "";
    document.getElementById("verifyStatus").textContent = "";
    verifyDialog.showModal();
    verifyCode.focus();
}

function setAdminMfaRequired(required) {
    adminMfaRequired = required;
    adminMfaLabel.hidden = !required;
    adminMfaCode.hidden = !required;
    adminMfaHint.hidden = !required;
    adminMfaCode.required = required;
    if (required) adminMfaCode.focus();
}

function openLoginChoices() {
    setLoginView("choices");
    loginDialog.showModal();
}

loginTrigger.addEventListener("click", openLoginChoices);
document.querySelector("[data-login-aware]")?.addEventListener("click", async event => {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();

    const link = event.currentTarget;
    if (link.dataset.checkingSession === "true") return;
    link.dataset.checkingSession = "true";
    link.setAttribute("aria-busy", "true");
    try {
        const response = await fetch("/api/v1/session", {
            credentials: "same-origin",
            cache: "no-store"
        });
        const contentType = response.headers.get("content-type") || "";
        if (response.status === 401 || !contentType.includes("application/json")) {
            openLoginChoices();
            return;
        }
        if (!response.ok) throw new Error(`Session check failed (${response.status}).`);
        window.location.assign(link.href);
    } catch (error) {
        console.error("Unable to verify the PALs session before navigation:", error);
        window.location.assign(link.href);
    } finally {
        delete link.dataset.checkingSession;
        link.removeAttribute("aria-busy");
    }
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
    requestVerificationCode(userEmail.value.trim()).then(passwordRequired => {
        if (passwordRequired) {
            document.getElementById("studentPasswordError").textContent = "";
            studentPassword.value = "";
            setLoginView("studentPassword");
            return;
        }
        showVerificationDialog();
    }).catch(error => {
        userLoginStatus.textContent = error.message;
        userLoginSubmit.disabled = false;
    });
});

verifyResend.addEventListener("click", async () => {
    if (verifyResend.disabled) return;
    verifyResend.disabled = true;
    verifyResend.textContent = "Sending a new code...";
    try {
        const response = await fetch("/api/v1/auth/resend", {
            method: "POST",
            credentials: "same-origin"
        });
        const body = await response.json().catch(() => ({}));
        if (!response.ok) {
            if (response.status === 410) {
                stopResendCountdown();
                verifyDialog.close();
                setLoginView("user");
                userLoginStatus.textContent = body?.error?.message || "This verification attempt expired. Start a new sign-in.";
                loginDialog.showModal();
                return;
            }
            throw new Error(body?.error?.message || "A new code could not be sent.");
        }
        configureVerificationAttempt(body, verificationEmail);
        verifyCode.value = "";
        document.getElementById("verifyCodeError").textContent = "";
        verifyResendStatus.textContent = "A new code was sent to your school email.";
        verifyCode.focus();
    } catch (error) {
        startResendCountdown();
        verifyResendStatus.textContent = error.message;
    }
});

document.getElementById("closeVerifyDialog").addEventListener("click", () => verifyDialog.close());
verifyForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const code = verifyCode.value.trim();
    const errorElement = document.getElementById("verifyCodeError");
    const statusElement = document.getElementById("verifyStatus");
    errorElement.textContent = "";
    statusElement.textContent = "";
    if (!new RegExp(`^\\d{${verificationCodeLength}}$`).test(code)) {
        errorElement.textContent = `Enter the ${verificationCodeLength}-digit verification code.`;
        verifyCode.reportValidity();
        return;
    }
    const submitButton = verifyForm.querySelector(".login-submit");
    submitButton.disabled = true;
    try {
        const response = await fetch("/api/v1/verify", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "same-origin",
            body: JSON.stringify({ code })
        });
        const body = await response.json().catch(() => ({}));
        if (!response.ok) {
            if (response.status === 410) {
                submitButton.disabled = false;
                verifyCode.value = "";
                stopResendCountdown();
                verifyDialog.close();
                setLoginView("user");
                const emailValid = validateSchoolEmail(userEmail, document.getElementById("userEmailError"));
                userLoginSubmit.disabled = !userEmail.value.trim() || !emailValid || !acceptTerms.checked;
                document.getElementById("userLoginStatus").textContent = body?.error?.message || "This verification attempt expired. Start a new sign-in.";
                loginDialog.showModal();
                return;
            }
            throw new Error(body?.error?.message || "Verification failed.");
        }
        stopResendCountdown();
        verifyDialog.close();
        if (body.passwordSetupRequired) {
            passwordSetupForm.reset();
            document.getElementById("passwordSetupError").textContent = "";
            passwordSetupDialog.showModal();
            document.getElementById("newStudentPassword").focus();
            return;
        }
        window.location.assign("/pals");
    } catch (error) {
        statusElement.textContent = error.message;
        submitButton.disabled = false;
    }
});

document.getElementById("skipPasswordSetup").addEventListener("click", () => {
    passwordSetupDialog.close();
    window.location.assign("/pals");
});
passwordSetupDialog.addEventListener("cancel", event => {
    event.preventDefault();
    document.getElementById("skipPasswordSetup").click();
});

passwordSetupForm.addEventListener("submit", async event => {
    event.preventDefault();
    const password = document.getElementById("newStudentPassword").value;
    const confirmation = document.getElementById("confirmStudentPassword").value;
    const errorElement = document.getElementById("passwordSetupError");
    errorElement.textContent = "";
    if (password !== confirmation) {
        errorElement.textContent = "The passwords do not match.";
        return;
    }
    if (password.length < 8 || !/[a-z]/.test(password) || !/[A-Z]/.test(password)
        || !/[0-9]/.test(password) || !/[\p{P}\p{S}]/u.test(password)) {
        errorElement.textContent = "Use at least 8 characters with uppercase, lowercase, a number, and a symbol.";
        return;
    }

    const submitButton = passwordSetupForm.querySelector(".login-submit");
    submitButton.disabled = true;
    try {
        const response = await fetch("/api/v1/auth/password", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "same-origin",
            body: JSON.stringify({ password })
        });
        const body = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(body?.error?.message || "The password could not be saved.");
        passwordSetupDialog.close();
        window.location.assign("/pals");
    } catch (error) {
        errorElement.textContent = error.message;
        submitButton.disabled = false;
    }
});

studentPasswordLoginForm.addEventListener("submit", async event => {
    event.preventDefault();
    const email = studentPasswordEmail.value.trim();
    const validEmail = validateSchoolEmail(studentPasswordEmail, document.getElementById("studentPasswordEmailError"));
    const errorElement = document.getElementById("studentPasswordError");
    errorElement.textContent = "";
    if (!validEmail || !studentPassword.value) {
        if (!studentPassword.value) studentPassword.reportValidity();
        return;
    }

    studentPasswordSubmit.disabled = true;
    try {
        const response = await fetch("/api/v1/auth/password/login", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "same-origin",
            body: JSON.stringify({ email, password: studentPassword.value, termsAccepted: acceptTerms.checked })
        });
        const body = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(body?.error?.message || "The school email or password is incorrect.");
        userEmail.value = email;
        configureVerificationAttempt(body, email);
        studentPassword.value = "";
        showVerificationDialog();
    } catch (error) {
        errorElement.textContent = error.message;
        studentPasswordSubmit.disabled = false;
        studentPassword.value = "";
        studentPassword.focus();
    }
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
        window.location.assign("/pals");
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
}

initializeLogin().catch(error => {
    const status = document.getElementById("loginComponent");
    if (status) status.textContent = error.message;
});
