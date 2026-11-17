import { defineConfig } from "vite";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const serverRoot = resolve(fileURLToPath(new URL(".", import.meta.url)));
const publicRoot = resolve(serverRoot, "public");
const apiTarget = process.env.DEV_API_TARGET ?? "http://127.0.0.1:8100";

export default defineConfig({
    root: publicRoot,
    publicDir: false,
    cacheDir: "/tmp/palsys-vite-cache",
    appType: "mpa",
    build: {
        outDir: resolve(serverRoot, ".web-dist"),
        emptyOutDir: true,
        sourcemap: false
    },
    server: {
        host: "127.0.0.1",
        port: 5022,
        strictPort: true,
        allowedHosts: ["apmain.softshell-grue.ts.net", "pals.noteriety.top","127.0.0.1:5060"],
        fs: {
            strict: true,
            allow: [publicRoot]
        },
        proxy: {
            "/db": apiTarget,
            "/acc": apiTarget,
            "/sched": apiTarget,
            "/fb": apiTarget,
            "/adata": apiTarget,
            "/connect": apiTarget,
            "/auth": apiTarget,
            "/chat": apiTarget,
            "/dev": apiTarget
        }
    }
});
