import { defineConfig, devices } from "@playwright/test";

const isCi = Boolean(process.env.CI);

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  retries: isCi ? 1 : 0,
  use: {
    baseURL: process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:3000",
    trace: "on-first-retry",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  // CI: next start tras build con NEXT_PUBLIC_E2E_*. Local: next dev con las mismas vars.
  webServer: {
    command: isCi ? "npm run start" : "npm run dev",
    url: "http://localhost:3000",
    reuseExistingServer: !isCi,
    timeout: 120_000,
    env: {
      ...process.env,
      NEXT_PUBLIC_E2E_TEST_MODE: "true",
      NEXT_PUBLIC_E2E_SEED: process.env.NEXT_PUBLIC_E2E_SEED ?? "12345",
    },
  },
});
