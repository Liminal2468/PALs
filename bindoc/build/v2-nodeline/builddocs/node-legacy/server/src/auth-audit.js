function ipPrefix(req) {
  const address = req.ip;
  if (typeof address !== "string") return null;

  if (/^(?:\d{1,3}\.){3}\d{1,3}$/.test(address)) {
    const octets = address.split(".");
    if (octets.some((part) => Number(part) > 255)) return null;
    return `${octets.slice(0, 3).join(".")}.0/24`;
  }

  if (!address.includes(":")) return null;
  const [beforeCompression, afterCompression = ""] = address.toLowerCase().split("::");
  const before = beforeCompression ? beforeCompression.split(":") : [];
  const after = afterCompression ? afterCompression.split(":") : [];
  if ([...before, ...after].some((part) => !/^[0-9a-f]{1,4}$/.test(part))) return null;
  const groups = address.includes("::")
    ? [...before, ...Array(8 - before.length - after.length).fill("0"), ...after]
    : before;
  if (groups.length !== 8) return null;
  return `${groups.slice(0, 4).map((part) => part.padStart(4, "0")).join(":")}::/64`;
}

export async function recordAuthAttempt(database, req, {
  event,
  provider,
  outcome,
  reasonCode,
  userId = null,
}) {
  await database.execute(
    `INSERT INTO auth_attempts
      (event, provider, user_id, outcome, reason_code, client_ip_prefix)
     VALUES (?, ?, ?, ?, ?, ?)`,
    [event, provider, userId, outcome, reasonCode, ipPrefix(req)],
  );
}

