import { createDatabaseTelemetry } from "./telemstry/database.js";
import { createRequestTelemetry } from "./telemstry/requests.js";

export function initiateTelemetry({ pool, allowedTables = [] } = {}) {
  const database = createDatabaseTelemetry({ pool, allowedTables });
  const requests = createRequestTelemetry();
  return {
    database,
    requests,
    middleware: requests.middleware(),
  };
}
