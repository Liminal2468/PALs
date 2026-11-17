export function createMatchingRecorder({ database, audit = () => {} } = {}) {
  if (!database?.edit) throw new Error("A database telemetry editor is required.");
  return {
    async record({ tutorUserId, tuteeUserId, sessionId = null, actor = null } = {}) {
      if (!tutorUserId || !tuteeUserId) throw new Error("Tutor and tutee are required.");
      const result = await database.edit({
        table: "match_recommendations",
        id: sessionId ?? `${tutorUserId}:${tuteeUserId}`,
        changes: { tutor_user_id: tutorUserId, tutee_user_id: tuteeUserId },
      });
      audit({ kind: "match_recorded", tutorUserId, tuteeUserId, sessionId, actor });
      return result;
    },
  };
}
