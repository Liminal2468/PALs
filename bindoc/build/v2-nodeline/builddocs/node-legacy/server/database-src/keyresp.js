import { createHash } from "node:crypto";
import { chmod, mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  calculateJwkThumbprint,
  exportJWK,
  exportPKCS8,
  exportSPKI,
  generateKeyPair,
  importSPKI,
  jwtVerify,
} from "jose";

const modulePath = fileURLToPath(import.meta.url);
const moduleDirectory = dirname(modulePath);
const defaultOutputDir = resolve(moduleDirectory, "../keys/generated");

export async function generateHostKeyPair(outputDir = defaultOutputDir) {
  const directory = resolve(outputDir);
  const { privateKey, publicKey } = await generateKeyPair("RS256", {
    extractable: true,
    modulusLength: 3072,
  });
  const publicJwk = await exportJWK(publicKey);
  const kid = await calculateJwkThumbprint(publicJwk);
  const privatePath = join(directory, `${kid}.private.pem`);
  const publicPath = join(directory, `${kid}.public.pem`);

  await mkdir(directory, { recursive: true, mode: 0o700 });
  await writeFile(privatePath, await exportPKCS8(privateKey), { mode: 0o600, flag: "wx" });
  await writeFile(publicPath, await exportSPKI(publicKey), { mode: 0o644, flag: "wx" });
  await chmod(directory, 0o700);

  return { kid, privatePath, publicPath };
}

export async function loadTrustedPublicKeys(directory) {
  const filenames = (await readdir(directory)).filter((name) => name.endsWith(".pem"));
  const keys = new Map();

  for (const filename of filenames) {
    const pem = await readFile(join(directory, filename), "utf8");
    const key = await importSPKI(pem, "RS256");
    const jwk = await exportJWK(key);
    const kid = await calculateJwkThumbprint(jwk);
    if (basename(filename, ".pem") !== kid) {
      throw new Error(`Trusted public key filename does not match its fingerprint: ${filename}`);
    }
    keys.set(kid, { key, jwk: { ...jwk, kid, alg: "RS256", use: "sig" } });
  }

  return keys;
}

export async function verifyHostToken(token, trustedKeys, { issuer, audience }) {
  const { payload, protectedHeader } = await jwtVerify(
    token,
    async (header) => {
      if (header.alg !== "RS256" || typeof header.kid !== "string") {
        throw new Error("Unsupported JWT signing key.");
      }
      const entry = trustedKeys.get(header.kid);
      if (!entry) throw new Error("Unknown JWT signing key.");
      return entry.key;
    },
    { issuer, audience, algorithms: ["RS256"] },
  );
  const now = Math.floor(Date.now() / 1000);
  if (
    typeof payload.iat !== "number"
    || typeof payload.exp !== "number"
    || payload.iat > now + 30
    || payload.exp <= payload.iat
    || payload.exp - payload.iat > 900
    || typeof payload.sub !== "string"
    || typeof payload.scope !== "string"
    || typeof payload.resource !== "string"
    || typeof payload.action !== "string"
  ) {
    throw new Error("JWT claims are invalid or exceed the allowed token lifetime.");
  }
  return { payload, protectedHeader };
}

export async function publicJwks(trustedKeys) {
  return {
    keys: [...trustedKeys.values()].map(({ jwk }) => jwk),
  };
}

if (process.argv[1] && resolve(process.argv[1]) === modulePath) {
  if (process.argv[2] !== "generate") {
    console.error("Usage: npm run keygen [-- --out-dir <directory>]");
    process.exitCode = 2;
  } else {
    const outDirIndex = process.argv.indexOf("--out-dir");
    const outputDir = outDirIndex >= 0 ? process.argv[outDirIndex + 1] : defaultOutputDir;
    try {
      const generated = await generateHostKeyPair(outputDir);
      const fingerprint = createHash("sha256")
        .update(await readFile(generated.publicPath))
        .digest("hex");
      console.log(`kid: ${generated.kid}`);
      console.log(`public-key SHA-256: ${fingerprint}`);
      console.log(`private key (copy securely to the host, then remove here): ${generated.privatePath}`);
      console.log(`public key (install in keys/trusted/${generated.kid}.pem): ${generated.publicPath}`);
    } catch (error) {
      console.error("Could not generate key pair:", error);
      process.exitCode = 1;
    }
  }
}
