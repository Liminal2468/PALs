import { Router } from "express";
import {
  ALGORITHM_VERSION,
  MatchingInputError,
  rankRequestsForTutor,
  rankTutorsForRequest,
} from "./matching.js";

export const router = Router();

router.get("/health", (_req, res) => {
  res.json({ service: "connect", status: "ready", algorithm: ALGORITHM_VERSION });
});

router.post(["/match", "/recommendations"], (req, res) => {
  try {
    const matches = rankTutorsForRequest(
      req.body?.request,
      req.body?.tutors,
      req.body?.limit,
    );
    return res.json({ data: matches, algorithm: ALGORITHM_VERSION });
  } catch (error) {
    if (error instanceof MatchingInputError) {
      return res.status(400).json({
        error: { code: "bad_request", message: error.message },
      });
    }
    console.error("Tutor recommendation failed:", error);
    return res.status(500).json({
      error: { code: "internal_error", message: "Matching request failed." },
    });
  }
});

router.post("/tutorreco", (req, res) => {
  try {
    const matches = rankRequestsForTutor(
      req.body?.tutor,
      req.body?.requests,
      req.body?.limit,
    );
    return res.json({ data: matches, algorithm: ALGORITHM_VERSION });
  } catch (error) {
    if (error instanceof MatchingInputError) {
      return res.status(400).json({
        error: { code: "bad_request", message: error.message },
      });
    }
    console.error("Tutee recommendation failed:", error);
    return res.status(500).json({
      error: { code: "internal_error", message: "Matching request failed." },
    });
  }
});

router.use((_req, res) => {
  res.status(404).json({
    error: { code: "not_found", message: "Connect endpoint not found." },
  });
});
