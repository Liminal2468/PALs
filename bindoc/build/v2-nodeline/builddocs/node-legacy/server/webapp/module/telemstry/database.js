const identifierPattern = /^[A-Za-z_][A-Za-z0-9_]*$/;

function checkedIdentifier(value, label) {
  if (typeof value !== "string" || !identifierPattern.test(value)) {
    throw new Error(`Invalid ${label}.`);
  }
  return `\`${value}\``;
}

export function createDatabaseTelemetry({ pool, allowedTables = [] } = {}) {
  const counters = new Map();
  const record = (kind, userId, at = Date.now()) => {
    const key = `${kind}:${userId ?? "anonymous"}:${at}`;
    const count = (counters.get(key) ?? 0) + 1;
    counters.set(key, count);
    return count;
  };

  return {
    async database_health() {
      if (!pool?.query) throw new Error("A database pool is required.");
      await pool.query("SELECT 1");
      return { status: "alive" };
    },
    reads({ userId = null, at = Date.now() } = {}) {
      return record("read", userId, at);
    },
    writes({ userId = null, at = Date.now() } = {}) {
      return record("write", userId, at);
    },
    async edit({ table, id, changes, idColumn = "id" } = {}) {
      if (!pool?.execute) throw new Error("A database pool is required.");
      if (!allowedTables.includes(table)) throw new Error("Table is not allowlisted.");
      if (!changes || typeof changes !== "object" || Array.isArray(changes)) {
        throw new Error("Changes must be an object.");
      }
      const entries = Object.entries(changes);
      if (entries.length === 0) throw new Error("At least one change is required.");
      const assignments = entries.map(([column]) => `${checkedIdentifier(column, "column")} = ?`);
      const [result] = await pool.execute(
        `UPDATE ${checkedIdentifier(table, "table")} SET ${assignments.join(", ")} WHERE ${checkedIdentifier(idColumn, "id column")} = ?`,
        [...entries.map(([, value]) => value), id],
      );
      return { affectedRows: result.affectedRows };
    },
    async update(options) {
      return this.edit(options);
    },
  };
}

export async function database_health(pool) {
  if (!pool?.query) throw new Error("A database pool is required.");
  await pool.query("SELECT 1");
  return { status: "alive" };
}

export function reads(counter, options) {
  return counter.reads(options);
}

export function writes(counter, options) {
  return counter.writes(options);
}
