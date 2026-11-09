import { Router } from "express";
import rateLimit from "express-rate-limit";
import { getTrustedKeys, requireJwt, requireScope } from "./hostauth.js";
import { getTableDefinition, pool, quoteColumn, validateColumns } from "./db.js";
import { publicJwks } from "./keyresp.js";

export const router = Router();

const authenticatedRequestRateLimit = rateLimit({
  windowMs: 15 * 60 * 1000,
  limit: 3000,
  keyGenerator: (req) => `kid:${req.jwtKeyId}`,
  standardHeaders: "draft-8",
  legacyHeaders: false,
  handler: (_req, res) => res.status(429).json({
    error: { code: "rate_limited", message: "Too many requests." },
  }),
});

router.get("/health", (_req, res) => {
  return res.json({ service: "database", status: "alive" });
});

router.get("/.well-known/jwks.json", async (_req, res, next) => {
  try {
    res.json(await publicJwks(await getTrustedKeys()));
  } catch (error) {
    next(error);
  }
});

router.use(requireJwt, authenticatedRequestRateLimit);

router.get("/health/database", async (_req, res) => {
  try {
    await pool.query("SELECT 1");
    return res.json({ service: "database", status: "ready" });
  } catch (error) {
    console.error("Database health check failed:", error);
    return res.status(503).json({
      error: { code: "service_unavailable", message: "Database is unavailable." },
    });
  }
});

const readScope = requireScope("db:read", "read");
const aggregateScope = requireScope("db:read", "aggregate");
const createScope = requireScope("db:write", "create");
const updateScope = requireScope("db:write", "update");
const deleteScope = requireScope("db:write", "delete");

const DEFAULT_PAGE_SIZE = 100;
const MAX_PAGE_SIZE = 500;

function respondError(res, error) {
  const status = Number.isInteger(error.status) ? error.status : 500;
  const message = status >= 500 ? "Database request failed." : error.message;
  return res.status(status).json({
    error: { code: status === 404 ? "not_found" : status === 400 ? "bad_request" : "internal_error", message },
  });
}

function paginationFromQuery(query) {
  const parseInteger = (value, name, defaultValue, minimum, maximum = Number.MAX_SAFE_INTEGER) => {
    if (value === undefined) return defaultValue;
    if (typeof value !== "string" || !/^\d+$/.test(value)) {
      const error = new Error(`${name} must be an integer from ${minimum} to ${maximum}.`);
      error.status = 400;
      throw error;
    }
    const parsed = Number(value);
    if (!Number.isSafeInteger(parsed) || parsed < minimum || parsed > maximum) {
      const error = new Error(`${name} must be an integer from ${minimum} to ${maximum}.`);
      error.status = 400;
      throw error;
    }
    return parsed;
  };
  return {
    limit: parseInteger(query.limit, "limit", DEFAULT_PAGE_SIZE, 1, MAX_PAGE_SIZE),
    offset: parseInteger(query.offset, "offset", 0, 0),
  };
}

function filtersFromQuery(query, columns) {
  const entries = Object.entries(query);
  for (const [column, value] of entries) {
    if (!columns.has(column)) {
      const error = new Error(`Invalid filter field: ${column}`);
      error.status = 400;
      throw error;
    }
    if (
      typeof value !== "string"
      && typeof value !== "number"
      && typeof value !== "boolean"
    ) {
      const error = new Error(`Filter value must be scalar: ${column}`);
      error.status = 400;
      throw error;
    }
  }
  return entries;
}

router.get("/:resource", readScope, async (req, res) => {
  try {
    const { columns } = await getTableDefinition(req.params.resource);
    const { limit, offset } = paginationFromQuery(req.query);
    const { limit: _limit, offset: _offset, ...filterQuery } = req.query;
    const filters = filtersFromQuery(filterQuery, columns);
    const where = filters.length
      ? ` WHERE ${filters.map(([column]) => `${quoteColumn(column)} = ?`).join(" AND ")}`
      : "";
    const [rows] = await pool.execute(
      `SELECT * FROM \`${req.params.resource}\`${where} LIMIT ? OFFSET ?`,
      [...filters.map(([, value]) => value), limit, offset],
    );
    return res.json({ data: rows });
  } catch (error) {
    return respondError(res, error);
  }
});

router.get("/:resource/:id", readScope, async (req, res) => {
  try {
    const { primaryKey } = await getTableDefinition(req.params.resource);
    const [rows] = await pool.execute(
      `SELECT * FROM \`${req.params.resource}\` WHERE ${quoteColumn(primaryKey)} = ? LIMIT 1`,
      [req.params.id],
    );
    if (rows.length === 0) {
      return res.status(404).json({ error: { code: "not_found", message: "Record not found." } });
    }
    return res.json({ data: rows[0] });
  } catch (error) {
    return respondError(res, error);
  }
});

router.post("/:resource/aggregate", aggregateScope, async (req, res) => {
  try {
    const { columns } = await getTableDefinition(req.params.resource);
    const groupBy = req.body?.group_by == null
      ? []
      : Array.isArray(req.body.group_by)
        ? req.body.group_by
        : [req.body.group_by];
    const metrics = req.body?.metrics ?? [{ operation: "count", field: "*" }];
    if (!Array.isArray(metrics) || metrics.length === 0) {
      const error = new Error("metrics must be a non-empty array.");
      error.status = 400;
      throw error;
    }

    const selections = groupBy.map((column) => {
      if (!columns.has(column)) {
        const error = new Error(`Invalid group_by field: ${column}`);
        error.status = 400;
        throw error;
      }
      return quoteColumn(column);
    });
    const allowedOperations = new Set(["count", "sum", "avg", "min", "max"]);
    for (const metric of metrics) {
      const operation = String(metric?.operation ?? "").toLowerCase();
      const field = metric?.field ?? "*";
      if (!allowedOperations.has(operation) || (field !== "*" && !columns.has(field))) {
        const error = new Error("Invalid aggregation metric.");
        error.status = 400;
        throw error;
      }
      const alias = metric.alias ?? `${operation}_${field === "*" ? "all" : field}`;
      if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(alias)) {
        const error = new Error("Invalid aggregation alias.");
        error.status = 400;
        throw error;
      }
      if (operation === "count" && field === "*") {
        selections.push(`COUNT(*) AS ${quoteColumn(alias)}`);
      } else {
        selections.push(`${operation.toUpperCase()}(${quoteColumn(field)}) AS ${quoteColumn(alias)}`);
      }
    }

    const rawFilters = req.body?.filters ?? {};
    if (!rawFilters || typeof rawFilters !== "object" || Array.isArray(rawFilters)) {
      const error = new Error("filters must be a JSON object.");
      error.status = 400;
      throw error;
    }
    const filters = filtersFromQuery(rawFilters, columns);
    const where = filters.length
      ? ` WHERE ${filters.map(([column]) => `${quoteColumn(column)} = ?`).join(" AND ")}`
      : "";
    const group = groupBy.length ? ` GROUP BY ${groupBy.map(quoteColumn).join(", ")}` : "";
    const [rows] = await pool.execute(
      `SELECT ${selections.join(", ")} FROM \`${req.params.resource}\`${where}${group}`,
      filters.map(([, value]) => value),
    );
    return res.json({ data: rows });
  } catch (error) {
    return respondError(res, error);
  }
});

router.post("/:resource", createScope, async (req, res) => {
  try {
    const { columns, primaryKey } = await getTableDefinition(req.params.resource);
    const entries = validateColumns(req.body, columns);
    const names = entries.map(([column]) => quoteColumn(column)).join(", ");
    const placeholders = entries.map(() => "?").join(", ");
    const [result] = await pool.execute(
      `INSERT INTO \`${req.params.resource}\` (${names}) VALUES (${placeholders})`,
      entries.map(([, value]) => value),
    );
    return res.status(201).json({
      data: { [primaryKey]: result.insertId, affected_rows: result.affectedRows },
    });
  } catch (error) {
    return respondError(res, error);
  }
});

router.patch("/:resource/:id", updateScope, async (req, res) => {
  try {
    const { columns, primaryKey } = await getTableDefinition(req.params.resource);
    const entries = validateColumns(req.body, columns, { allowPrimaryKey: false, primaryKey });
    const assignments = entries.map(([column]) => `${quoteColumn(column)} = ?`).join(", ");
    const [result] = await pool.execute(
      `UPDATE \`${req.params.resource}\` SET ${assignments} WHERE ${quoteColumn(primaryKey)} = ?`,
      [...entries.map(([, value]) => value), req.params.id],
    );
    if (result.affectedRows === 0) {
      const [rows] = await pool.execute(
        `SELECT 1 FROM \`${req.params.resource}\` WHERE ${quoteColumn(primaryKey)} = ? LIMIT 1`,
        [req.params.id],
      );
      if (rows.length === 0) {
        return res.status(404).json({ error: { code: "not_found", message: "Record not found." } });
      }
    }
    return res.json({ data: { affected_rows: result.affectedRows } });
  } catch (error) {
    return respondError(res, error);
  }
});

router.delete("/:resource/:id", deleteScope, async (req, res) => {
  try {
    const { primaryKey } = await getTableDefinition(req.params.resource);
    const [result] = await pool.execute(
      `DELETE FROM \`${req.params.resource}\` WHERE ${quoteColumn(primaryKey)} = ?`,
      [req.params.id],
    );
    if (result.affectedRows === 0) {
      return res.status(404).json({ error: { code: "not_found", message: "Record not found." } });
    }
    return res.status(204).end();
  } catch (error) {
    return respondError(res, error);
  }
});
