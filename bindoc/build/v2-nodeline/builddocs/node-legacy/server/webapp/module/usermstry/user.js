import { appendFile, mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const historyRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../../..", ".history");

async function recordHistory(event, payload, root = historyRoot) {
  await mkdir(root, { recursive: true, mode: 0o700 });
  await appendFile(
    resolve(root, "users.jsonl"),
    `${JSON.stringify({ event, at: new Date().toISOString(), ...payload })}\n`,
    { mode: 0o600 },
  );
}

export function createUserManager({ database, historyDirectory = historyRoot } = {}) {
  if (!database?.edit) throw new Error("A database telemetry editor is required.");
  return {
    async edit(userId, changes, actor = null) {
      const result = await database.edit({ table: "users", id: userId, changes });
      await recordHistory("user_edit", { userId, actor, changes }, historyDirectory);
      return result;
    },
    async update(userId, changes, actor = null) {
      return this.edit(userId, changes, actor);
    },
  };
}
