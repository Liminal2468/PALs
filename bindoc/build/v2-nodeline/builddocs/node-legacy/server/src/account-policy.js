const ORGANIZATION_ACCOUNT_PATTERN = /^[A-Za-z][A-Za-z'-]*\.[0-9]{6,}@cubao\.sti\.edu\.ph$/i;

export function isOrganizationAccountEmail(email) {
  return typeof email === "string"
    && email.length <= 254
    && ORGANIZATION_ACCOUNT_PATTERN.test(email);
}
