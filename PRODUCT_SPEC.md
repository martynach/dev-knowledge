# DevKnowledge — Product & technical specification

## 1. Goal
Build a simple, polished, publicly readable technical knowledge base. Only approved authors can publish Markdown articles. A public visitor may read and search without signing in. This is a learning project emphasizing modern ASP.NET Core, Angular, Azure, Terraform, Docker and robust authorization. Keep business features intentionally small; deliver a complete functional application, not just a prototype.

## 2. Functional scope
- Public homepage: article list with title, author, publication date, tags, pagination, search by title/content (document and explain search limitations), tag filtering, and article detail pages with readable typography, headings, code highlighting, code copy and navigation for long content (table of contents is optional if it adds complexity).
- Every created article is public immediately; no drafts, private articles, comments, likes, attachments, version history, or rich-text/WYSIWYG editor.
- Authorized author form: title, plain multiline Markdown textarea, tags and either paste Markdown or choose a local `.md` UTF-8 file. The browser reads the file and fills the same textarea. One JSON create/update API; no file-upload endpoint or blob storage. Support editing article Markdown in the same simple textarea. Provide a safe rendered preview only if trivial; do not build a fancy editor.
- Max Markdown size: 100 KiB UTF-8; validate both client and server. Validate title/tag limits, reject empty/invalid input, display useful errors. Do not allow arbitrary file types or embedded files. Store Markdown as text in database. Safe Markdown rendering: disable raw HTML or sanitize it; block dangerous links/scripts, prevent XSS.
- Public 'Join the editorial team' application: minimal form with name, email and short motivation. Server-side validation, anti-spam/rate limits and generic response; do not expose application data publicly. Administrator lists, approves or rejects applications. Approval MUST NOT grant rights solely based on unverified email. Use a secure invitation/account-linking flow based on validated identity provider subject/tenant, with documented manual fallback if provider provisioning cannot be automated.
- Initial admin is configured securely by a verified identity-provider subject identifier via deployment configuration/bootstrap, never by client-selected role or publicly writable email. No hard-coded production credentials. Only admin may grant Editor or Admin roles; default authenticated users have no editor permissions. Prevent last-admin lockout where relevant.
- Role semantics: anonymous Visitor may read only; Editor may create, edit and delete only their own articles; Admin may create and edit/delete any article and approve/reject applications. Admin implicitly has editor permissions. Ownership is checked against immutable authenticated subject ID on every update/delete. Return appropriate 401/403/404 (avoid unnecessary disclosure).
- UI: responsive, accessible, clean and minimal. Display edit/delete controls only when permitted, but enforce all rules in API. Confirmation for delete. Loading, empty, error and success states.

## 3. Technology and architecture
- Monorepo: `frontend/` Angular SPA; `backend/` ASP.NET Core Web API on .NET 10/C#14; `infrastructure/terraform/`; `pipelines/`; test projects; `docker-compose.yml`; README.
- Backend is ONE deployable API, not four microservices. Prefer modest clean layering: `DevKnowledge.Api`, `DevKnowledge.Application`, `DevKnowledge.Domain`, `DevKnowledge.Infrastructure` as sibling projects inside `backend/` plus `backend/DevKnowledge.slnx`. Simplify layers if they produce needless boilerplate, document rationale.
- Azure Cosmos DB for NoSQL, store articles, editorial applications and app role grants. Design partition keys and indexes for public list/search and per-author operations; explain RU/cross-partition tradeoffs. No paid full-text/search service. For search, implement a bounded practical solution and document limitations; avoid claiming Cosmos DB supports arbitrary scalable full-text search by default.
- External authentication: Microsoft Entra External ID for production if compatible with current offerings and free usage; Angular OIDC Authorization Code + PKCE using supported library; API JWT Bearer token validation with strict issuer/audience/signature/lifetime checks. Do not implement a custom password database. App role grants reside server-side, keyed to verified identity subject; use policy/resource-based authorization. Separate local test/dev identity mechanism, strictly unavailable in production.
- Docker: multi-stage backend Dockerfile, optional frontend Dockerfile for local Compose, local `docker compose up` path. For local database, support Cosmos DB emulator if reliably feasible; otherwise offer a deterministic local test storage adapter for development only, with integration tests and clearly documented Cosmos-specific verification gap. Do not pretend local adapter proves cloud Cosmos behavior.
- Azure deployment target (subject to free-only gate): Azure Static Web Apps Free for Angular, Azure Container Apps Consumption for API, Cosmos DB Free Tier where eligible. Terraform for resources, separate dev/prod app configuration and data, least-privilege identities, secure secrets. Azure Container Registry, monitoring, Log Analytics, storage for Terraform state, identity services, networking and deployment prerequisites must each be assessed for billing; do not provision silently. If a required component cannot be free with bounded exposure, STOP and present alternatives for user approval. Do not assume 0-cost because of a monthly free grant.
- Terraform: modular but not overengineered, remote state strategy documented (remote backend may have costs), dev/prod separated logically, `fmt` and `validate` passing, explicit safe defaults. No automatic `apply`.

## 4. REST API outline (adjust naming consistently)
- `GET /api/articles` public paginated search/filter
- `GET /api/articles/{id}` public detail
- `POST /api/articles` Editor/Admin
- `PUT /api/articles/{id}` owner or Admin
- `DELETE /api/articles/{id}` owner or Admin
- `GET /api/me/articles` Editor/Admin
- `POST /api/editor-applications` public, throttled
- `GET /api/admin/editor-applications` Admin
- `POST /api/admin/editor-applications/{id}/approve` Admin; approval/invite safely bound to verified identity
- `POST /api/admin/editor-applications/{id}/reject` Admin
- `GET /api/me` authenticated identity and effective app permissions
Use RFC 7807 Problem Details, validation and consistent response contracts. Avoid leaking personal application data in public endpoints.

## 5. CI/CD — Azure DevOps / Azure Repos
- Main branch protected: PR required, required build/test status, no direct pushes for regular contributors. Ready PRs (not drafts) trigger validation; build frontend/backend, lint/type-check, run fast tests, and required integration checks. Failure blocks merge. No automatic PR deployment and NO ephemeral PR environment.
- Manual 'Deploy DEV' pipeline supports a selected feature branch/commit; build/test and deploy that revision to shared DEV, with commit/branch visible. This may temporarily replace `main` on DEV; provide easy 'Restore DEV from main' action.
- Merge to `main` triggers automatic DEV deployment with smoke checks; serialize deployments and ensure stale runs cannot overwrite newer `main` revisions. A DEV deploy of a feature branch must not silently trigger production.
- PROD is manual and requires approval, using an immutable previously validated artifact from `main` (container image digest and frontend artifact), preferably promoted without rebuilding. Approval must occur before production change. Never auto-deploy PROD on merge.
- Azure Pipelines YAML under `pipelines/`, plus instructions for Azure Repos branch policies, service connections, environments/approvals, permissions and pipeline creation (some controls may require UI/CLI setup and cannot be fully expressed in YAML). If Azure DevOps free parallel jobs are not granted, clearly document that as a deployment blocker; no paid agents without consent.
- No production deployment or remote infrastructure changes from Codex without explicit approval. CI/CD must not apply Terraform automatically by default. Terraform plan may be part of review, apply is manual/approved.

## 6. Cost-safety — non-negotiable
- User requires free Azure resources only. If ANY necessary resource or operation is billable, could exceed a free quota, requires credit card/subscription, or has uncertain pricing/eligibility, flag it clearly BEFORE proposing provisioning. Never choose a paid SKU or enable a paid feature without explicit user consent.
- Produce `docs/AZURE_COST_REVIEW.md` listing every proposed Azure resource and dependency, SKU, free entitlement, quotas, cost/overage risk, eligibility prerequisites, whether hard spending caps exist, and links to current official pricing/limits to verify. No invented zero-cost guarantees. A budget alert is NOT a hard cap.
- Assess DEV + PROD together: free allowances may be shared per subscription; Cosmos Free Tier may allow only one eligible account. Verify whether required separation can remain free. If not, provide an alternative and request approval before any cloud setup.
- Never provision Azure infrastructure or execute cloud-consuming tests automatically. Use local verification first.

## 7. Test and acceptance criteria
- Backend unit tests: article validation, role checks, owner vs admin access, unapproved users denied, bootstrap admin, application workflow, pagination/search, Markdown size, failure responses.
- Integration tests: real HTTP API with isolated test persistence and auth; unauthorized/forbidden/not found, create/edit/delete, admin moderation; optional Cosmos emulator integration if available. Ensure no cloud calls in normal tests.
- Angular tests for components/services and key permission-based UI; Playwright E2E with deterministic local seed/test accounts covering public view, Markdown paste and `.md` import, owner edit/delete, admin edit/delete another author's article, application approval and responsive layout. Verify rendered Markdown cannot execute scripts.
- Initial implementation: execute full locally available suite, build, lint, run Compose and exercise browser interactions; fix failures. Explicitly state anything not verifiable without Azure credentials, real Entra configuration, billing or remote environment.
- Later cosmetic changes: focused checks only; security/data/API/deployment changes: broader suite. See `AGENTS.md`.

## 8. Deliverables
- Full source code, tests, Docker/Compose, Terraform, Azure Pipelines YAML, `.env.example`, seed data, accessible UI, README and docs.
- `README.md`: exact prerequisites, local quick start, mock-auth warning, user journeys, build/test commands, Azure setup checklist, and troubleshooting.
- `docs/DEPLOYMENT_RUNBOOK.md`: step-by-step human tasks, account/subscription eligibility, Entra External ID configuration, verified admin bootstrap, Azure DevOps connection and PR policy setup, Terraform plan/apply only after cost approval, pipeline setup, DEV deployment, manual PROD approval, rollback, cleanup and cost verification.
- `docs/VERIFICATION_REPORT.md`: actual commands/tests/browser flows executed, results, failures, remaining cloud verification and honest limitations.
- `docs/ARCHITECTURE.md`: repo tree, request/auth flow, data model, authorization matrix and design tradeoffs.

## 9. Scope boundaries and decision policy
No private articles, drafts, WYSIWYG, comments, media attachments, notifications, separate microservices, search SaaS, or preview environments. Do not expand scope without consent. Where Azure free-tier prerequisites are unknown, implement deployable configuration and document blockers rather than guessing. Ask only when an external account choice, payment, permission, destructive action or irreversible decision is required; otherwise choose sensible documented defaults and finish implementation.
