import { Router } from "express";

export const router = Router();

router.get("/health", (_req, res) => {
  res.json({ service: "feedback", status: "ready" });
});

router.use((_req, res) => {
  res.status(501).json({
    error: { code: "not_implemented", message: "Feedback endpoints are not implemented yet." },
  });
});
