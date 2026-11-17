export function createRequestTelemetry() {
  const incoming = [];
  const outgoing = [];
  const record = (list, event) => {
    list.push({ ...event, at: event.at ?? Date.now() });
    if (list.length > 1000) list.shift();
  };

  return {
    incoming(event) { record(incoming, event); },
    outgoing(event) { record(outgoing, event); },
    snapshot() { return { incoming: [...incoming], outgoing: [...outgoing] }; },
    middleware() {
      return (request, response, next) => {
        const started = process.hrtime.bigint();
        response.once("finish", () => {
          record(incoming, {
            method: request.method,
            path: request.originalUrl ?? request.url,
            status: response.statusCode,
            durationMs: Number(process.hrtime.bigint() - started) / 1_000_000,
            userId: request.user?.id ?? request.jwt?.sub ?? null,
          });
        });
        next();
      };
    },
  };
}
