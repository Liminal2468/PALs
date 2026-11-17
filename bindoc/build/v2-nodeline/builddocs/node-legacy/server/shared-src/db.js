import mysql from "mysql2/promise";
import { config, validateConfig } from "./config.js";

let poolInstance;

function getPool() {
  if (!poolInstance) {
    validateConfig();
    poolInstance = mysql.createPool({
      host: config.database.host,
      port: config.database.port,
      user: config.database.user,
      password: config.database.password,
      database: config.database.name,
      waitForConnections: true,
      connectionLimit: 10,
      decimalNumbers: true,
      timezone: config.database.timezone,
    });
  }
  return poolInstance;
}

export const pool = {
  query(...args) {
    return getPool().query(...args);
  },
  execute(...args) {
    return getPool().execute(...args);
  },
  end(...args) {
    return poolInstance ? poolInstance.end(...args) : Promise.resolve();
  },
};
