export function getHealthBaseUrl(host, port, override) {
  return override ?? `http://${host}:${port}`;
}

export function unexpectedChildExitCode(code) {
  return code === 0 ? 1 : code ?? 1;
}
