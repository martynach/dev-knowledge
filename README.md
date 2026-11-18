# DevKnowledge

A public technical knowledge library with Angular, ASP.NET Core, safe Markdown publishing and server-enforced editorial permissions.

## Run locally

Prerequisites: .NET SDK **10.0.401**, Node **24.21.0**, npm, and optionally Docker Compose v2. Angular **22.2.6**, CLI/build **22.2.7**, TypeScript **6.0.2** and package versions are pinned. No Azure account is needed locally.

From the repository root, in PowerShell:

```powershell
dotnet restore backend/DevKnowledge.slnx --locked-mode
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/DevKnowledge.Api --no-launch-profile --urls http://localhost:5080
```

In another terminal:

```powershell
cd frontend  
npm ci
npm start
```

Open http://localhost:4200. The API is http://localhost:5080 and its liveness endpoint is `/health`. On bash use `ASPNETCORE_ENVIRONMENT=Development dotnet run ...` instead of the PowerShell environment assignment.

Alternative, with Docker installed:

```powershell
docker compose up --build
```

Compose uses the same URLs, loopback-bound ports and a durable named volume. `docker compose down` stops it and retains data. Docker/Compose execution was **not verified in the implementation environment because Docker is absent**. Native .NET/Angular startup and Chromium flows were verified.

## Local identities and journeys

The **Local identity** selector supplies deterministic development accounts: Visitor (anonymous), Reader (no publishing grant), Editor and Other editor (different owners), Admin (all articles and applications). The header is a test mechanism, not a credential. **Never publish Development configuration.** Production refuses local authentication/storage and validates real external access tokens. Local JSON data lives in `backend/DevKnowledge.Api/data/local.json`; use only one local API process with this path.

Browse the seeded welcome article, search content or filter tags. Select Editor, choose Write an article, paste Markdown or import a UTF-8 `.md` file, preview and publish. Edit/delete your own article, then switch owners to check action visibility. Admin can moderate any article. Join the team submits a private application; Admin reviews it and can approve a separately verified subject (local test subject `local|reader`) or reject it. Refresh/select Reader after approval to publish.

Articles are public immediately; there are no drafts or uploads. Content is limited to 100 KiB UTF-8, titles to 160 characters and tags to five lowercase 1–24-character values. Search scans only the latest 200 articles using case-insensitive title/Markdown substring matching; it is not ranked full-text search. Ordinary public and author listings paginate independently. HTML and images are disabled; sanitized Markdown includes highlighted code and copy controls.

## Build and test

From the root:

```powershell
dotnet restore backend/DevKnowledge.slnx --locked-mode
dotnet format backend/DevKnowledge.slnx --verify-no-changes --no-restore
dotnet build backend/DevKnowledge.slnx -c Release --no-restore
dotnet test backend/DevKnowledge.slnx -c Release --no-build --no-restore
node --test scripts/release.test.mjs
```

From `frontend`:

```powershell
npm ci
npm run lint
npm run format:check
npm run typecheck
npm test
npm run build
npx playwright install chromium
npm run e2e
```

Playwright starts local API/SPA servers when needed and uses isolated development persistence for its own API process. It can reuse existing local servers; stop them first for an isolated run. In CI, reuse is disabled. Failure screenshots/traces and an HTML report are written to ignored test directories. Tests use no Azure services.

With Terraform 1.14.x installed:

```powershell
terraform -chdir=infrastructure/terraform init -backend=false
terraform -chdir=infrastructure/terraform fmt -check -recursive
terraform -chdir=infrastructure/terraform validate
```

Never run cloud plans or applies as part of normal local tests. See [verification evidence](docs/VERIFICATION_REPORT.md) for what actually ran and [architecture](docs/ARCHITECTURE.md) for tradeoffs.

## Azure setup checklist

1. Read [the cost review](docs/AZURE_COST_REVIEW.md). Guaranteed zero-cost Azure hosting is **not established**. Keep all infrastructure/deployment gates disabled under a strict free-only requirement.
2. Verify subscription/region eligibility, one available Cosmos Free Tier account, combined DEV/PROD allowances, registry entitlement, Azure DevOps job entitlement and every ancillary cost. Obtain explicit approval before any potentially billable setup.
3. Configure Entra External ID SPA/API registrations, code + PKCE, delegated scope, exact issuer/audience/redirect URIs and a cryptographically verified bootstrap admin `iss|sub`.
4. Choose approved state storage and image registry. Configure separate environment databases, identities, origins and immutable image digests in Terraform. Review the plan and obtain separate human approval before apply. No resources have been deployed.
5. Configure protected main/required PR validation, federated service connections, secret variable groups, DEV exclusive lock and PROD exclusive lock plus predeployment manual approval. Create all four pipelines; restrict artifact provenance to the main release pipeline.
6. Enable DEV only after approval. Verify real Entra and Cosmos flows. Manual feature DEV deployment and Restore DEV from main use `deploy-dev.yml` with the selected revision.
7. Promote a retained validated main artifact to PROD with approval, using its container digest and unchanged compiled frontend. Verify smoke tests, access rules, release metadata, rollback and actual usage/costs.

Follow the full [deployment runbook](docs/DEPLOYMENT_RUNBOOK.md); it contains exact configuration/permission tasks, editor identity verification, rollback and cleanup requirements.

## Troubleshooting and limitations

- `address already in use` means another process already occupies the requested port. Run only one API instance on port 5080 (PowerShell or Rider). Stop the previous instance with Ctrl+C or Rider's Stop button before starting another. The frontend likewise needs port 4200 available. In PowerShell, inspect the listener with `Get-NetTCPConnection -LocalPort 5080 -State Listen` and identify its `OwningProcess` before stopping anything; do not terminate unrelated processes.
- API startup rejects Production without required identity configuration, or with local adapter flags. Set `ASPNETCORE_ENVIRONMENT=Development` only for local work.
- If SDK first-run/template or compiler-server permissions fail in a restricted workstation, use a writable `DOTNET_CLI_HOME` and `dotnet build -m:1 -p:UseSharedCompilation=false`. Do not weaken production security.
- For CORS errors, use exactly `http://localhost:4200`; `127.0.0.1` is a different origin. Runtime API/OIDC settings are in `frontend/public/config.json` locally and are overlaid by deployment scripts in Azure.
- A 403 means the authenticated identity has no grant or does not own the article. Client-selected roles do not grant production rights. A 429 application response requires waiting a minute.
- `.md` import rejects wrong extensions, invalid UTF-8 and files larger than 100 KiB. Clipboard requires browser support/permission.
- Local persistence does not validate Cosmos behavior. Real Entra login, Docker execution, Azure pipelines and cloud deployment remain unverified. Read the verification report before deployment.
- Current development tooling has a published picomatch ReDoS advisory with no available patched latest release in the checked registry. Runtime npm and NuGet audits found no known vulnerabilities. Do not expose the development server or run untrusted glob patterns; recheck upstream before release.
