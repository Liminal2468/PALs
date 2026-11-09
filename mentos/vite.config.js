import { defineConfig } from "vite";
import { copyFile, mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = dirname(fileURLToPath(import.meta.url));
const publicRoot = resolve(projectRoot, "public");

const redirectLegacyGameRoute = (server) => {
  server.middlewares.use((request, response, next) => {
    if (request.url?.split("?")[0] === "/game.html") {
      response.writeHead(308, { Location: "/" });
      response.end();
      return;
    }

    next();
  });
};

const copyLoginPreview = {
  name: "copy-login-preview",
  apply: "build",
  async closeBundle() {
    const destination = resolve(projectRoot, "dist/components/login/modal.login.htm");
    await mkdir(dirname(destination), { recursive: true });
    await copyFile(
      resolve(publicRoot, "components/login/modal.login.htm"),
      destination,
    );
  },
};

export default defineConfig({
  root: publicRoot,
  publicDir: false,
  appType: "mpa",
  plugins: [
    {
      name: "redirect-legacy-game-route",
      configureServer: redirectLegacyGameRoute,
      configurePreviewServer: redirectLegacyGameRoute,
    },
    copyLoginPreview,
  ],
  build: {
    outDir: resolve(projectRoot, "dist"),
    emptyOutDir: true,
    rollupOptions: {
      input: {
        main: resolve(publicRoot, "index.html"),
        "pals-home": resolve(publicRoot, "pages/home.html"),
        "pals-tutors": resolve(publicRoot, "pages/tutors.html"),
        "pals-sessions": resolve(publicRoot, "pages/sessions.html"),
        "pals-resources": resolve(publicRoot, "pages/resources.html"),
        "pals-analytics": resolve(publicRoot, "pages/analytics.html"),
        "pals-settings": resolve(publicRoot, "pages/settings.html"),
        "pals-help": resolve(publicRoot, "pages/help.html"),
        "pals-feedback": resolve(publicRoot, "pages/feedback.html"),
      },
    },
  },
  server: {
    proxy: {
      "/api": "http://127.0.0.1:8000",
    },
  },
});
