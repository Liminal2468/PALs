# PALs service ports

| Environment | API port | Web application port | Notes |
| --- | ---: | ---: | --- |
| DBSERV local default (`server/`) | `8100` | `5060` | The REST API defaults to `8100`; the developer Vite interface is configured for `5060`. |
| DBSERV on APMAIN | `8432` | `5022` | `palsys.service` runs the API runner and Vite together; Tailscale Serve maps the developer hostname to the Vite listener. |
| DBSERV tailnet hostname | `8432` via Tailscale Serve | `5060` if a Vite process is started | `palsys.softshell-grue.ts.net` currently proxies to APMAIN's API listener on `127.0.0.1:8432`. |
| HOST local (`mentos/`) | `8000` | `5000` | `mentos/api` uses port `8000`; `mentos/run` starts Vite on port `5000`. |

`server/src/runner.js` starts `server/src/server.js`. That executable imports
and binds the Express application exported by `server/src/app.js`; `app.js`
should not be launched as a separate process.


As of the moment the API webapp is at port 50
