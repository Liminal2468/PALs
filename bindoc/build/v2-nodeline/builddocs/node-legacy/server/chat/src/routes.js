import { Router } from "express";

export const router = Router();

router.get("/health", (_req, res) => {
  res.json({ service: "chat", status: "skeleton" });
});

router.use((_req, res) => {
  res.status(501).json({
    error: { code: "not_implemented", message: "Messaging endpoints are not implemented yet." },
  });
});
