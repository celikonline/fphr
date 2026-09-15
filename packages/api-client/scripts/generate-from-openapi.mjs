#!/usr/bin/env node

/**
 * Generate TypeScript API client from OpenAPI specification
 *
 * Usage:
 *   node scripts/generate-from-openapi.mjs [openapi-spec-url]
 *
 * Example:
 *   node scripts/generate-from-openapi.mjs http://localhost:5000/swagger/v1/swagger.json
 */

import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const specUrl = process.argv[2] || "http://localhost:5000/swagger/v1/swagger.json";
const outputDir = path.resolve(__dirname, "../src/generated");

console.log("API Client Generation");
console.log("====================");
console.log(`OpenAPI Spec: ${specUrl}`);
console.log(`Output Directory: ${outputDir}`);
console.log("");

// Create output directory if it doesn't exist
if (!fs.existsSync(outputDir)) {
  fs.mkdirSync(outputDir, { recursive: true });
  console.log(`✓ Created output directory: ${outputDir}`);
}

console.log("");
console.log("TODO: Implement OpenAPI-to-TypeScript code generation");
console.log("");
console.log("Options:");
console.log("  1. Use NSwag CLI (if .NET available)");
console.log("  2. Use OpenAPI Generator (Docker)");
console.log("  3. Use @openapitools/openapi-generator-cli (npm)");
console.log("  4. Use orval (TypeScript-first, recommended)");
console.log("");
console.log("For now, API clients should be generated and committed to the repository.");
console.log("After backend is running, run:");
console.log(`  npx orval --input ${specUrl} --output ${outputDir}`);
