import { config, validateConfig } from "@mentos/shared-core/config";
import { pool } from "@mentos/shared-core/db";
import { app } from "./app.js";

validateConfig();

const server = app.listen(config.port, config.host, () => {
  console.log(`Mentos API server listening on port ${config.port}.`);
});

async function shutdown(signal) {
  console.log(`${signal} received; stopping Mentos API server.`);
  server.close(async (error) => {
    try {
      await pool.end();
    } catch (poolError) {
      console.error("Could not close MariaDB pool:", poolError);
      process.exitCode = 1;
    }
    if (error) {
      console.error("Could not stop HTTP server:", error);
      process.exitCode = 1;
    }
  });
}

process.once("SIGINT", () => void shutdown("SIGINT"));
process.once("SIGTERM", () => void shutdown("SIGTERM"));
