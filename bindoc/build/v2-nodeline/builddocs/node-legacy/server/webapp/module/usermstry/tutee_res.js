export function createTuteeResourceManager({ database, audit = () => {} } = {}) {
  if (!database?.edit) throw new Error("A database telemetry editor is required.");
  return {
    async edit(resourceId, changes, actor = null) {
      const result = await database.edit({ table: "resources", id: resourceId, changes });
      audit({ kind: "tutee_resource_edit", resourceId, actor, changes });
      return result;
    },
    async check(resource) {
      const safe = resource && typeof resource === "object" && resource.content !== undefined;
      return { safe, resourceId: resource?.id ?? null };
    },
  };
}
