import { Router } from "express";
import { requireJwt } from "../../database/src/hostauth.js";
import { adminsRouter } from "./admins.js";
import { providersRouter } from "./providers.js";

export const router = Router();

export function createAuthenticationPermissionGate(authenticate = requireJwt) {
  return (req, res, next) => {
    let forbiddenResponse;
    let protectedResponse;
    protectedResponse = new Proxy(res, {
      get(target, property) {
        if (property === "status") {
          return (status) => {
            if (status === 401) {
              forbiddenResponse = true;
              target.status(403);
            } else {
              target.status(status);
            }
            return protectedResponse;
          };
        }
        if (property === "json") {
          return (body) => target.json(
            forbiddenResponse
              ? {
                error: {
                  code: "forbidden",
                  message: "Insufficient permission.",
                },
              }
              : body,
          );
        }
        const value = Reflect.get(target, property, target);
        return typeof value === "function" ? value.bind(target) : value;
      },
    });
    return authenticate(req, protectedResponse, next);
  };
}

router.use(createAuthenticationPermissionGate());

router.get("/health", (_req, res) => {
  res.json({ service: "authentication", status: "ready" });
});

router.use("/providers", providersRouter);
router.use("/admins", adminsRouter);

router.use((_req, res) => {
  res.status(501).json({
    error: { code: "not_implemented", message: "Authentication flow is not defined yet." },
  });
});
