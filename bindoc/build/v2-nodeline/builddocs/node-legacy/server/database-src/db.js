import { config } from "@mentos/shared-core/config";
import { pool } from "@mentos/shared-core/db";

export { pool };

export async function getTableDefinition(resource) {
  if (!config.database.allowedTables.includes(resource)) {
    const error = new Error("Unknown resource.");
    error.status = 404;
    throw error;
  }

  const [columns] = await pool.query(`SHOW COLUMNS FROM \`${resource}\``);
  const primaryKeys = columns.filter((column) => column.Key === "PRI");
  if (columns.length === 0 || primaryKeys.length !== 1) {
    throw new Error(`Resource ${resource} must exist and have one primary key.`);
  }
  return {
    columns: new Set(columns.map((column) => column.Field)),
    primaryKey: primaryKeys[0].Field,
  };
}

export function quoteColumn(column) {
  if (typeof column !== "string" || !/^[A-Za-z_][A-Za-z0-9_]*$/.test(column)) {
    const error = new Error("Invalid column name.");
    error.status = 400;
    throw error;
  }
  return `\`${column}\``;
}

export function validateColumns(data, columns, { allowPrimaryKey = true, primaryKey } = {}) {
  if (!data || typeof data !== "object" || Array.isArray(data)) {
    const error = new Error("Expected a JSON object.");
    error.status = 400;
    throw error;
  }
  const entries = Object.entries(data);
  if (entries.length === 0) {
    const error = new Error("At least one field is required.");
    error.status = 400;
    throw error;
  }
  for (const [column] of entries) {
    if (!columns.has(column) || (!allowPrimaryKey && column === primaryKey)) {
      const error = new Error(`Invalid field: ${column}`);
      error.status = 400;
      throw error;
    }
  }
  return entries;
}
