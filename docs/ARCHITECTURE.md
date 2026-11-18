# Architecture

DevKnowledge is an Angular 22.2.6 standalone SPA and one ASP.NET Core API (.NET 10 / C# 14). Angular CLI/build 22.2.7, TypeScript 6.0.2 and Node 24.21.0 are pinned. Angular's compatibility requirements were checked against https://angular.dev/reference/versions and package peer dependencies. Exact transitive dependencies are in npm and NuGet lock files.

## Repository

- `frontend/src`: standalone application component, typed API/auth service, safe Markdown renderer and validation; accessible responsive library, reading, author and moderation screens.
- `backend/DevKnowledge.Api`: minimal hosting, HTTP handlers, authentication and server-side permissions.
- `backend/DevKnowledge.Domain`: records and business validation/ownership rules.
- `backend/DevKnowledge.Infrastructure`: local persistence and Cosmos adapters behind a narrow storage boundary.
- `backend/DevKnowledge.Tests`: business rules and HTTP integration tests, including the real JWT Bearer handler with isolated local signing keys.
- `frontend/e2e`: Playwright Chromium journeys with development identities.
- `infrastructure/terraform`: one gated stack, two environment databases and identities.
- `pipelines`: PR validation, main release, manual feature DEV and manual PROD promotion.

An empty Application layer would add indirection without business value. Rules live in Domain and endpoint orchestration stays in Api. There is no generic repository framework, CQRS or event bus.

## Authentication and authorization

Production uses OIDC authorization code with PKCE through angular-oauth2-oidc. The API validates JWT signature, exact issuer, audience, expiration and subject using HTTPS discovery. Identity keys are the exact `iss|sub` pair, never names, emails, client roles or request payloads. The provider subject is specific to the API token: do not substitute a directory object ID or an ID-token subject without verifying it matches.

The configured `Auth:AdminSubject` is the immutable bootstrap administrator. Additional Editor grants are persisted server-side. Token role claims have no authority. There is no public role-management or admin-removal endpoint, so the application cannot demote its last admin. Additional administrators require a separately reviewed deployment/bootstrap configuration change; the UI only grants Editor.

Anonymous users can list, read and submit an editorial application. Authenticated members without a grant have the same read rights. Editors can publish and edit/delete their own articles. Admin can publish and edit/delete any article and review applications. Every mutation checks authentication, effective server role, immutable author ownership and validation. The UI hides actions as a convenience. Unauthorized requests return 401, forbidden operations 403, absent articles 404, and invalid input 400 Problem Details.

Development uses a fixed `X-Dev-User` allowlist: admin, editor, other, reader. It is available only with environment `Development` and explicit development auth configuration. Production startup fails if local auth or persistence is enabled. Never expose the development server to the internet. Headers are ignored by production JWT authentication.

## Editorial approval

Applications contain private contact information and motivation. Only admin can list them. Public submission returns a generic receipt, validates input, uses a honeypot and throttles to five requests per minute per connection IP. Forwarded headers are deliberately not trusted; behind a proxy this can group visitors under the proxy address. There is one configured API replica; rate-limit state resets on restarts and is not a distributed anti-abuse service.

Automated identity provisioning is not implemented. The supported manual fallback requires the administrator to independently verify the applicant's provider account and API `iss|sub`, then enter that pair when approving. The server restricts the pair to the configured issuer and grants Editor only. Matching an application's email is never sufficient. See the identity verification procedure in the runbook. Grant and application writes are separate operations; a failed request may be retried with the same subject. Administrators must avoid concurrent decisions for the same application. Review the stored grant/application if a network failure leaves the result uncertain.

## Data and search

Each environment has database `devknowledge-dev` or `devknowledge-prod`, with a `content` container partitioned by `/kind`. Partitions are `articles`, `applications`, `roles`. Documents wrap typed `value`, `kind`, `sortDate` and a SHA-256 storage ID; hashing avoids forbidden Cosmos ID characters in issuer URLs. API article IDs remain random UUIDs. Article authors never change during an edit.

Public list and author list use ten-item pages, partition-scoped queries and indexed publication dates, author IDs and tags. Page numbers are bounded to 10,000. Cosmos OFFSET cost grows for deep pages; this is a modest learning knowledge base, not a large publishing engine. `GET /api/me/articles?page=N` returns the same paginated shape as the public list.

Search reads at most 201 recent article documents, searches the first 200 in memory by case-insensitive title/content substring, then filters tags and paginates. `limited=true` indicates older results were excluded. No stemming, relevance ranking, full-text index or search SaaS. Articles older than this search window remain browsable and directly readable. Reading full Markdown candidates can consume significant RU; the 100 KiB content limit bounds document size, not query cost.

Partitioning avoids cross-partition fan-out for public and author queries and makes role lookups point reads. A single articles partition can become hot or reach a logical partition's size limits. This tradeoff is intentional for a small application and requires redesign before large scale. Only required paths are indexed; Markdown is excluded. DEV/PROD share the eligible Cosmos account to use one Free Tier allowance, but have distinct databases, containers and database-scoped managed identities.

LocalStore persists JSON using a serialized mutation lock and atomic file replacement. It is single-process development storage, not an emulator and not proof of Cosmos query/index/RBAC behavior. Normal tests never contact Azure. Article edits use last-writer-wins semantics; no optimistic conflict UI or version history is provided.

## Content safety

Titles have 1–160 characters, tags at most five lowercase 1–24-character values, and Markdown at most 102,400 UTF-8 bytes. Both clients and API validate. Browser file import accepts only `.md`, enforces size and decodes UTF-8 strictly; it fills the same textarea and sends the same JSON API. There is no upload endpoint or blob storage.

Marked disables author HTML and images; DOMPurify removes unsafe markup/URLs; Angular sanitizes the resulting HTML binding again. No trust-bypass API is used. Highlight.js handles only registered code languages and escapes code content. Executable HTML and embedded media do not render. Code can be copied by clicking a code block or using the keyboard-accessible copy button. Clipboard access depends on browser permission and secure context.

## Deployment boundaries

The API container runs unprivileged. Compose binds host ports to loopback and uses a named local-data volume. Production uses Container Apps managed identity, Cosmos data-plane RBAC and no database keys. Frontend OIDC configuration is public runtime configuration, not a secret. CORS is restricted to the environment's SPA origin.

Terraform is disabled by default and pipelines never apply it. No ACR, Log Analytics, Application Insights, custom networking or remote state storage is created. A public approved external image registry is an explicit prerequisite. This deployment target cannot guarantee zero charges: see the cost review before enabling anything.
