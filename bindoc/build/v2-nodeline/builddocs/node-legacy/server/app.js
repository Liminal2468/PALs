import express from "express";
import rateLimit from "express-rate-limit";
import helmet from "helmet";
import { router as accountsRouter } from "@mentos/accounts-api";
import { router as analystRouter } from "@mentos/analyst-api";
import { router as authRouter } from "@mentos/authentication-api";
import { router as chatRouter } from "@mentos/chat-api";
import { router as connectRouter } from "@mentos/connect-api";
import { router as databaseRouter } from "@mentos/database-api";
import { router as feedbackRouter } from "@mentos/feedback-api";
import { router as schedulerRouter } from "@mentos/scheduler-api";
import { developerLoginRouter } from "./webapp/module/dev.login.js";

export const app = express();

const errorCodes = {
  400: "bad_request",
  401: "unauthorized",
  403: "forbidden",
  404: "not_found",
  413: "payload_too_large",
};

const errorMessages = {
  400: "Invalid request.",
  401: "Authentication required.",
  403: "Insufficient permission.",
  404: "API endpoint not found.",
  413: "Request body too large.",
};

export function serializeApiError(error) {
  const status =
    Number.isInteger(error.status) && error.status >= 400 && error.status < 500
      ? error.status
      : 500;
  return {
    status,
    body: {
      error: {
        code: errorCodes[status] ?? (status < 500 ? "client_error" : "internal_error"),
        message: errorMessages[status] ?? (status < 500 ? "Request rejected." : "Request failed."),
      },
    },
  };
}

app.set("trust proxy", "loopback");
app.disable("x-powered-by");
app.use(helmet());
app.use(express.json({ limit: "256kb" }));

const rateLimitHandler = (_req, res) => {
  res.status(429).json({
    error: { code: "rate_limited", message: "Too many requests." },
  });
};
const apiRateLimit = rateLimit({
  windowMs: 15 * 60 * 1000,
  limit: 120,
  standardHeaders: "draft-8",
  legacyHeaders: false,
  handler: rateLimitHandler,
});
const databaseRateLimit = rateLimit({
  windowMs: 15 * 60 * 1000,
  limit: 60,
  skipSuccessfulRequests: false,
  standardHeaders: "draft-8",
  legacyHeaders: false,
  handler: rateLimitHandler,
});

app.use((req, res, next) => {
  if (req.path === "/db" || req.path.startsWith("/db/")) return next();
  return apiRateLimit(req, res, next);
});
app.use("/db", databaseRateLimit, databaseRouter);
app.use("/acc", accountsRouter);
app.use("/sched", schedulerRouter);
app.use("/fb", feedbackRouter);
app.use("/adata", analystRouter);
app.use("/connect", connectRouter);
app.use("/auth/v1", authRouter);
app.use("/chat/v1", chatRouter);
app.use("/dev/login", developerLoginRouter);

app.use((req, res) => {
  res.status(404).json({
    error: { code: "not_found", message: "API endpoint not found." },
  });
});

app.use((error, _req, res, _next) => {
  console.error("Unhandled API error:", error);
  const response = serializeApiError(error);
  res.status(response.status).json(response.body);
});
