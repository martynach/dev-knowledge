import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: "http://localhost:4200",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  webServer: [
    {
      command:
        "dotnet run --project ../backend/DevKnowledge.Api --no-launch-profile --urls http://localhost:5080",
      url: "http://localhost:5080/health",
      reuseExistingServer: !process.env["CI"],
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        Storage__Path: `data/e2e-${process.pid}.json`,
      },
    },
    {
      command: "npm start",
      url: "http://localhost:4200",
      reuseExistingServer: !process.env["CI"],
    },
  ],
});
