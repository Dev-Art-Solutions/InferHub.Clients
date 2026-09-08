import { defineConfig } from "tsup";

// D2 — ESM primary, CJS secondary, one source tree. tsup (esbuild) is a dev-only build tool; it
// never appears in `dependencies` and the emitted output has none either.
export default defineConfig({
  entry: ["src/index.ts"],
  format: ["esm", "cjs"],
  dts: true,
  sourcemap: true,
  clean: true,
  target: "node18",
  platform: "neutral",
  outExtension({ format }) {
    return { js: format === "cjs" ? ".cjs" : ".mjs" };
  },
});
