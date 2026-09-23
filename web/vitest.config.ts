import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import path from "path";

export default defineConfig({
  plugins: [react()],
  test: {
    globals: true,
    environment: "jsdom",
    setupFiles: ["./src/test-setup.ts"],
    // Windows host, 91 test files: the default threads pool spawns one worker per file
    // (~2.8s jsdom cold start each) and floods all CPU cores at once, so under load some
    // files' first assertions blow past the 5000ms default before they even get a turn.
    // Capping concurrent workers removes the contention; raising testTimeout gives slow
    // hosts headroom without hiding a real hang (these are real userEvent/findBy waits).
    testTimeout: 15000,
    maxWorkers: 8,
    coverage: {
      provider: "v8",
      reporter: ["text", "json", "html"],
      exclude: ["node_modules/", "dist/", "**/*.d.ts", "**/*.config.*", "**/mockData/*"],
    },
  },
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
});
