#!/usr/bin/env node

/**
 * Generate TypeScript API client from backend OpenAPI specification
 *
 * This script delegates to the api-client package's generation script.
 * Usage: pnpm generate:api-client [--api-url http://localhost:5000]
 */

import { spawn } from "child_process";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const apiUrl = process.argv[2] || "http://localhost:5000/swagger/v1/swagger.json";

console.log("Generating API client from OpenAPI specification...");
console.log(`API URL: ${apiUrl}`);
console.log("");

// Run the generation script from api-client package
const generate = spawn("node", [
  path.resolve(__dirname, "../../packages/api-client/scripts/generate-from-openapi.mjs"),
  apiUrl,
]);

generate.stdout.on("data", (data) => {
  process.stdout.write(data);
});

generate.stderr.on("data", (data) => {
  process.stderr.write(data);
});

generate.on("close", (code) => {
  process.exit(code || 0);
});
