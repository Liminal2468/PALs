export const ALGORITHM_VERSION = "weighted-baseline-v1-draft";

const SCORE_WEIGHTS = Object.freeze({
  proficiency: 0.35,
  availability: 0.35,
  preferences: 0.2,
  format: 0.1,
});
const PROFICIENCY_MIN = 1;
const PROFICIENCY_MAX = 5;

export class MatchingInputError extends Error {
  constructor(message) {
    super(message);
    this.name = "MatchingInputError";
    this.status = 400;
  }
}

function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function requireRecord(value, name) {
  if (!isRecord(value)) {
    throw new MatchingInputError(`${name} must be a JSON object.`);
  }
}

function requireId(value, name) {
  if (
    (typeof value !== "string" && typeof value !== "number")
    || String(value).trim() === ""
    || (typeof value === "number" && !Number.isFinite(value))
  ) {
    throw new MatchingInputError(`${name} must be a non-empty string or number.`);
  }
  return String(value);
}

function requireProficiency(value, name) {
  if (!Number.isInteger(value) || value < PROFICIENCY_MIN || value > PROFICIENCY_MAX) {
    throw new MatchingInputError(
      `${name} must be an integer from ${PROFICIENCY_MIN} to ${PROFICIENCY_MAX}.`,
    );
  }
  return value;
}

function requireArray(value, name, { allowEmpty = true } = {}) {
  if (!Array.isArray(value) || (!allowEmpty && value.length === 0)) {
    throw new MatchingInputError(
      `${name} must be ${allowEmpty ? "an array" : "a non-empty array"}.`,
    );
  }
  return value;
}

function normalizedSet(values, name) {
  return new Set(requireArray(values ?? [], name).map((value) => {
    if (typeof value !== "string" || value.trim() === "") {
      throw new MatchingInputError(`${name} values must be non-empty strings.`);
    }
    return value.trim().toLocaleLowerCase("en");
  }));
}

function overlapMinutes(firstSlots, secondSlots) {
  const first = validateAvailability(firstSlots, "request.availability");
  const second = validateAvailability(secondSlots, "tutor.availability");
  let totalMilliseconds = 0;
  const overlaps = [];
  for (const left of first) {
    for (const right of second) {
      const start = Math.max(left.start, right.start);
      const end = Math.min(left.end, right.end);
      if (end > start) overlaps.push({ start, end });
    }
  }

  overlaps.sort((left, right) => left.start - right.start);
  let current = null;
  for (const interval of overlaps) {
    if (!current || interval.start > current.end) {
      if (current) totalMilliseconds += current.end - current.start;
      current = { ...interval };
    } else {
      current.end = Math.max(current.end, interval.end);
    }
  }
  if (current) totalMilliseconds += current.end - current.start;
  return totalMilliseconds / 60_000;
}

function requireFormat(value, name) {
  if (!["one_to_one", "group", "either"].includes(value)) {
    throw new MatchingInputError(`${name} must be one_to_one, group, or either.`);
  }
  return value;
}

function requireRoles(value, name, role) {
  const roles = requireArray(value, name);
  if (!roles.every((entry) => ["tutor", "tutee", "admin"].includes(entry))) {
    throw new MatchingInputError(`${name} contains an unsupported role.`);
  }
  if (!roles.includes(role)) {
    throw new MatchingInputError(`${name} must include the ${role} role.`);
  }
}

function validateAvailability(slots, name) {
  const value = requireArray(slots, name, { allowEmpty: false });
  return value.map((slot, index) => {
    requireRecord(slot, `${name}[${index}]`);
    const hasTimezone = (date) => (
      typeof date === "string"
      && /(?:Z|[+-]\d{2}:\d{2})$/i.test(date)
      && Number.isFinite(Date.parse(date))
    );
    if (!hasTimezone(slot.start) || !hasTimezone(slot.end) || Date.parse(slot.end) <= Date.parse(slot.start)) {
      throw new MatchingInputError(
        `${name}[${index}] must have valid ISO timestamps with a timezone and end after start.`,
      );
    }
    return { start: Date.parse(slot.start), end: Date.parse(slot.end) };
  });
}

function validateRequest(request) {
  requireRecord(request, "request");
  requireId(request.learning_request_id, "request.learning_request_id");
  requireId(request.tutee_user_id, "request.tutee_user_id");
  requireId(request.subject_id, "request.subject_id");
  requireRoles(request.roles, "request.roles", "tutee");
  if (!["open", "matched", "closed", "cancelled"].includes(request.status)) {
    throw new MatchingInputError("request.status must be open, matched, closed, or cancelled.");
  }
  requireProficiency(request.current_proficiency, "request.current_proficiency");
  requireFormat(request.preferred_format, "request.preferred_format");
  validateAvailability(request.availability, "request.availability");
  normalizedSet(request.preferences ?? [], "request.preferences");
  if (request.topic_id != null) requireId(request.topic_id, "request.topic_id");
}

function validateTutor(tutor) {
  requireRecord(tutor, "tutor");
  requireId(tutor.tutor_user_id, "tutor.tutor_user_id");
  requireId(tutor.subject_id, "tutor.subject_id");
  requireRoles(tutor.roles, "tutor.roles", "tutor");
  if (!["active", "suspended", "disabled"].includes(tutor.status)) {
    throw new MatchingInputError("tutor.status must be active, suspended, or disabled.");
  }
  requireProficiency(tutor.proficiency_level, "tutor.proficiency_level");
  requireArray(tutor.formats, "tutor.formats", { allowEmpty: false })
    .forEach((format) => requireFormat(format, "tutor.formats[]"));
  validateAvailability(tutor.availability, "tutor.availability");
  normalizedSet(tutor.preferences ?? [], "tutor.preferences");
  if (tutor.topic_id != null) requireId(tutor.topic_id, "tutor.topic_id");
  if (tutor.topic_ids != null) {
    requireArray(tutor.topic_ids, "tutor.topic_ids")
      .forEach((topicId) => requireId(topicId, "tutor.topic_ids[]"));
  }
}

function formatScore(requestedFormat, tutorFormats) {
  const formats = requireArray(tutorFormats, "tutor.formats", { allowEmpty: false });
  const normalizedFormats = formats.map((format) => requireFormat(format, "tutor.formats[]"));
  if (requestedFormat === "either" || normalizedFormats.includes("either")) return 1;
  return normalizedFormats.includes(requestedFormat) ? 1 : 0;
}

function preferenceSimilarity(requestPreferences, tutorPreferences) {
  const requested = normalizedSet(requestPreferences ?? [], "request.preferences");
  const offered = normalizedSet(tutorPreferences ?? [], "tutor.preferences");
  if (requested.size === 0 || offered.size === 0) return 0;
  let intersection = 0;
  for (const preference of requested) {
    if (offered.has(preference)) intersection += 1;
  }
  return intersection / new Set([...requested, ...offered]).size;
}

function validateTutorForRequest(request, tutor) {
  validateRequest(request);
  validateTutor(tutor);

  const requestId = requireId(request.learning_request_id, "request.learning_request_id");
  const tuteeId = requireId(request.tutee_user_id, "request.tutee_user_id");
  const tutorId = requireId(tutor.tutor_user_id, "tutor.tutor_user_id");
  if (tuteeId === tutorId) return null;

  if (request.status !== "open" || tutor.status !== "active") return null;
  requireId(request.subject_id, "request.subject_id");
  requireId(tutor.subject_id, "tutor.subject_id");
  if (String(request.subject_id) !== String(tutor.subject_id)) return null;

  const tutorTopicIds = tutor.topic_ids ?? (
    tutor.topic_id == null ? [] : [tutor.topic_id]
  );
  if (
    request.topic_id != null
    && !tutorTopicIds.some((topicId) => String(topicId) === String(request.topic_id))
  ) {
    return null;
  }

  const learnerLevel = requireProficiency(
    request.current_proficiency,
    "request.current_proficiency",
  );
  const tutorLevel = requireProficiency(tutor.proficiency_level, "tutor.proficiency_level");
  const proficiencyGap = tutorLevel - learnerLevel;
  if (proficiencyGap <= 0) return null;

  const requestedFormat = requireFormat(request.preferred_format, "request.preferred_format");
  const matchingFormat = formatScore(requestedFormat, tutor.formats);
  if (matchingFormat === 0) return null;

  const sharedMinutes = overlapMinutes(request.availability, tutor.availability);
  if (sharedMinutes <= 0) return null;
  const availabilityScore = Math.min(sharedMinutes / 30, 1);
  const proficiencyScore = proficiencyGap / (PROFICIENCY_MAX - PROFICIENCY_MIN);
  const preferencesScore = preferenceSimilarity(request.preferences, tutor.preferences);
  const score = (
    proficiencyScore * SCORE_WEIGHTS.proficiency
    + availabilityScore * SCORE_WEIGHTS.availability
    + preferencesScore * SCORE_WEIGHTS.preferences
    + matchingFormat * SCORE_WEIGHTS.format
  );

  return {
    learning_request_id: requestId,
    tutee_user_id: tuteeId,
    tutor_user_id: tutorId,
    score: Number(score.toFixed(4)),
    score_components: {
      subject: 1,
      proficiency_gap: Number(proficiencyScore.toFixed(4)),
      availability: Number(availabilityScore.toFixed(4)),
      preferences: Number(preferencesScore.toFixed(4)),
      format: matchingFormat,
    },
    explanation: [
      "subject_match",
      `proficiency_gap_${proficiencyGap}`,
      `shared_availability_${Math.floor(sharedMinutes)}_minutes`,
      ...(preferencesScore > 0 ? ["shared_learning_preferences"] : []),
      `format_${requestedFormat}`,
    ],
  };
}

function requireLimit(limit) {
  if (limit === undefined) return 10;
  if (!Number.isInteger(limit) || limit < 1 || limit > 50) {
    throw new MatchingInputError("limit must be an integer from 1 to 50.");
  }
  return limit;
}

export function rankTutorsForRequest(request, tutors, limit) {
  validateRequest(request);
  requireArray(tutors, "tutors");
  tutors.forEach(validateTutor);
  const maxResults = requireLimit(limit);
  return tutors
    .map((tutor) => validateTutorForRequest(request, tutor))
    .filter(Boolean)
    .sort((left, right) => (
      right.score - left.score
      || left.tutor_user_id.localeCompare(right.tutor_user_id)
    ))
    .slice(0, maxResults);
}

export function rankRequestsForTutor(tutor, requests, limit) {
  validateTutor(tutor);
  requireArray(requests, "requests");
  requests.forEach(validateRequest);
  const maxResults = requireLimit(limit);
  return requests
    .map((request) => validateTutorForRequest(request, tutor))
    .filter(Boolean)
    .sort((left, right) => (
      right.score - left.score
      || left.learning_request_id.localeCompare(right.learning_request_id)
    ))
    .slice(0, maxResults);
}
