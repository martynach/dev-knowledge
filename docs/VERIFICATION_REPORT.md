# Local verification report

## Startup handoff correction

A user subsequently reported `address already in use` on port 5080. Inspection confirmed the Release API process left running by this chat's screenshot verification (parent command: `dotnet run --project backend/DevKnowledge.Api -c Release --no-build --no-launch-profile --urls http://localhost:5080`). That instance was stopped. The documented `dotnet run --project backend/DevKnowledge.Api --no-launch-profile --urls http://localhost:5080` command was then rerun with the Development environment and a writable local CLI home: startup succeeded, `/health` returned HTTP 200 with `{"status":"ok"}`, and `/api/articles` returned HTTP 200. The verification instance was stopped afterwards and port 5080 was confirmed to have zero listeners. The frontend process left by screenshot verification was also stopped. This corrects the original handoff's failure to release development ports; it does not change application or authentication behavior.

Date: **2026-11-18**. All verification below was local. No Azure account was authenticated, no cloud resources were provisioned, no image was pushed, no Terraform plan/apply ran, and no deployment occurred.

## Environment

Windows; .NET SDK 10.0.401 / runtime 10.0.12; Node 24.21.0 / npm 11.19.0. Angular 22.2.6 with CLI/build 22.2.7 and TypeScript 6.0.2. Chromium installed through Playwright 1.65.1. Terraform 1.14.0 was downloaded to ignored `.local/tools` and AzureRM provider 4.53.0 initialized without a backend; Windows/Linux provider hashes are committed in the provider lock file.

Docker was not installed (`docker` command not found). Terraform was initially missing and subsequently installed locally for offline checks. Python was unavailable and was not needed.

Restricted-shell network access failed with ENOTFOUND and the .NET testhost could not connect through its restricted process channel. Public package installation and local test processes succeeded with elevated execution outside those sandbox restrictions. A writable `DOTNET_CLI_HOME` was used in this workspace. Those tool permissions did not authorize or perform cloud operations.

## Executed checks and final results

From the repository root (PowerShell, writable `DOTNET_CLI_HOME`):

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.local/dotnet"
dotnet restore backend/DevKnowledge.slnx --locked-mode
dotnet format backend/DevKnowledge.slnx --no-restore
dotnet format backend/DevKnowledge.slnx --verify-no-changes --no-restore
dotnet build backend/DevKnowledge.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test backend/DevKnowledge.slnx -c Release --no-build --no-restore -m:1
dotnet list backend/DevKnowledge.slnx package --vulnerable --include-transitive
node --test scripts/release.test.mjs
node scripts/check-config.mjs
```

- Locked restore and formatting verification: passed.
- Release backend build: passed, **0 warnings and 0 errors**.
- Backend suite: **16 passed, 0 failed, 0 skipped**. Includes validation/UTF-8 boundaries, owner/admin checks, HTTP 400/401/403/404, create/edit/delete, private applications, approval/rejection, honeypot/throttle, persistence restart, pagination/search and browsing past the 200-article search window.
- Real JWT Bearer handler tests use isolated local signing keys and a local discovery configuration: valid subject-based admin bootstrap, client role claims ignored, invalid issuer/audience/expiry/signature/malformed token rejection, dev headers ignored in Production, and fail-closed Production adapter flags. **These are not tests against Entra.**
- NuGet audit: no vulnerable packages reported by the configured public source, including transitive packages.
- Release safety tests: **2 passed**. Altered frontend artifacts and feature-branch PROD provenance are rejected; runtime config disables mock auth and emits hosting security headers. No deployment script was executed against a cloud service.
- YAML parser successfully parsed all five pipeline/template YAML files and Compose. This verifies syntax, not Azure DevOps semantics.
- `C:\Program Files\Git\bin\bash.exe -n scripts/deploy.sh`: passed shell syntax checking.

From `frontend`:

```powershell
npm install
npm ci
npm run lint
npm run typecheck
npm test
npm run build
npm run format:check
npx playwright install chromium
npm run e2e
npm audit --omit=dev
npm audit --json
```

- Public dependencies installed and lockfile clean installation passed. A clean-install attempt initially failed because the running Angular server held `esbuild.exe`; after stopping it, `npm ci` passed. The formatter was subsequently added as a pinned development dependency.
- ESLint: passed, zero warnings. TypeScript type check: passed. Prettier formatting verification: passed after formatting the browser tests.
- Vitest: **4 tests passed in 2 files** (Markdown XSS/HTML/media/link protection, highlighting, UTF-8 validation and service/UI permission decisions).
- Angular production build: passed. Final emitted initial bundle approximately **513 kB raw / 139 kB estimated transfer**, within configured budget. No external font download is needed.
- Playwright Chromium: **5 passed, 0 failed** on the final application. Both API and Angular servers were actually started; the suite starts its own local servers and isolated storage when existing servers are absent. No request to Cosmos or Entra was needed.
- Runtime npm audit (`--omit=dev`): **0 vulnerabilities**.
- Full npm audit: **20 high findings in development dependency chains**, tracing to the published picomatch repeated-extglob ReDoS advisory (GHSA-fc5q-39xc-vjpf). Latest available picomatch was 4.0.7 and the advisory included it. No supported upstream patch was available in the checked registry. Runtime dependencies are unaffected by this audit result. Do not run exposed development servers or feed untrusted glob patterns into tooling. Recheck before release. Earlier Vitest advisories were removed by updating to 4.1.12; Vite was pinned to 8.3.8. Automatic downgrade suggestions to unsupported Angular versions were not applied.

Terraform checks:

```powershell
.local/tools/terraform/terraform.exe -chdir=infrastructure/terraform init -backend=false
.local/tools/terraform/terraform.exe -chdir=infrastructure/terraform providers lock -platform=linux_amd64 -platform=windows_amd64
.local/tools/terraform/terraform.exe -chdir=infrastructure/terraform fmt -check -recursive
.local/tools/terraform/terraform.exe -chdir=infrastructure/terraform validate
```

All passed. Validation initially caught an invalid `logs_destination="none"`; the pinned provider accepts an empty destination to disable logging, which was corrected and revalidated. No provider authentication, resource plan or cloud calls were used by these checks.

## Browser flows actually verified

1. Public homepage, seeded article, detail Markdown rendering, search with matching/no results, tag filter, library navigation, pagination next/previous, hidden visitor edit controls and mobile width without horizontal overflow.
2. Editor creates by Markdown paste; rendered raw scripts and dangerous links are absent and no script marker executes; code highlighting and clipboard copy (highlighted-token click and accessible button); owner edit; safe preview; deletion cancellation and confirmed owner deletion.
3. Invalid `.txt` import rejected; valid UTF-8 `.md` import fills the textarea and publishes correctly; another editor sees no edit control; direct edit route denied; actual non-owner DELETE API returns 403; Admin edits and deletes the other author's article.
4. Reader denied the admin route; public application submission; admin sees private application, approves `local|reader`; admin rejects another application; newly approved reader publishes and deletes their article.
5. Twelve temporary articles exercise ten-item public pages and author-list navigation. Test-created articles are removed in cleanup.

Actual local desktop (1440 px) and mobile (390 px) screenshots were captured with Playwright and visually inspected. They are stored in ignored `.local/desktop.png` and `.local/mobile.png`; they are local runtime evidence, not cloud screenshots. The final browser run's HTML report is in ignored `frontend/playwright-report/`. Failure traces/screenshots were produced during earlier failed runs; passing reruns replaced the report.

The final API was also started from its Release build on `http://localhost:5080` and the Angular server on `http://localhost:4200` for screenshot inspection. These use Development configuration and local persistence only.

## Failures corrected

- Compiler error from an unused authorization-policy prototype was removed; permissions are explicitly checked against server roles and resources in handlers.
- Malformed JSON initially returned generic 500; BadHttpRequestException status handling now returns 400 Problem Details.
- Approval/rejection tests were corrected to the implemented 204 No Content contract.
- Production JWT tests initially supplied configuration too late in WebApplicationFactory startup and did not configure the isolated test signing key correctly; test setup was corrected, then all signature/issuer/audience tests passed.
- Markdown file import initially failed to refresh the zoneless Angular textarea; explicit change detection fixed it and the browser flow passed.
- A remote-font build dependency was removed after offline build failure.
- Ordinary browsing originally inherited the search window; it now paginates independently, verified with 205 local adapter articles and browser pagination.
- Vulnerable available test tool versions were updated; the remaining upstream development advisory is disclosed above.

## Unverified functionality and limitations

- **Docker/Compose execution and container builds**: not run because Docker was unavailable. Compose YAML was parsed, but that does not validate image tags, volume permissions, Linux execution or networking. CI includes Compose configuration/build checks; it has not run remotely.
- **Cosmos**: no emulator or Azure integration ran. SQL query syntax, indexes, partition behavior, RU cost, managed-identity RBAC, consistency and DEV/PROD isolation need actual Cosmos verification after approval. The local adapter does not prove them.
- **Entra External ID**: no tenant/client registrations or real PKCE/discovery/login/logout/refresh flows were available. Confirm provider/library discovery compatibility and the API-token `iss|sub` before rollout.
- **Cloud deployment and billing**: no resources or endpoints exist from this work. Free-tier eligibility, shared quotas, registry availability, SWA CLI deployment, Azure pipeline execution/policies/checks, locks, stale-run behavior, approval enforcement, smoke/rollback and effective permissions remain unverified remotely.
- **Search**: latest 200 articles only, case-insensitive substring matching, no ranking. Public/author page numbers are limited to 10,000; deep offset queries can be RU-expensive. The admin application screen currently reads the latest 200 applications; older pending applications require an authorized operator until pagination is added.
- **Approval**: documented manual identity verification is required. No automated invitations/provisioning/email notification. Role/application writes are separate and require same-subject retry/operator review after partial failure. Do not make concurrent approval decisions for the same application.
- **Storage/operations**: local adapter is single-process; article edits are last-writer-wins. No paid monitoring, automated application-data retention, distributed anti-spam service or zero-spend enforcement exists. The connection-IP rate limit can group traffic behind a proxy.
- **Browser coverage**: Chromium desktop/mobile viewport only; no Firefox/WebKit/device or full assistive-technology audit. Code copy was verified in Chromium with clipboard permissions granted; permission-denied browser behavior remains environment-dependent.

Read `DEPLOYMENT_RUNBOOK.md` and `AZURE_COST_REVIEW.md` before any cloud action. Generated and locally validated IaC is not deployed infrastructure.

