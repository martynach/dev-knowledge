# DevKnowledge — Codex working instructions

Read `PRODUCT_SPEC.md` completely before implementing. This file governs all Codex work in this repository. Keep implementation simple, secure, maintainable and production-oriented. Use English for source code, names, comments, documentation, UI copy and commit messages.

## Initial assignment
Build the complete DevKnowledge application described in `PRODUCT_SPEC.md`, including frontend, backend, automated tests, local Docker development, Terraform IaC, Azure DevOps pipeline YAML and deployment documentation. Work autonomously within the repository; do not stop after scaffolding. Do not claim that Azure deployment or browser flows were verified unless they actually were. Never provision cloud resources, run `terraform apply`, change remote settings, create paid services, or deploy to Azure without explicit approval. Assume no Azure credentials are available. Deliver a locally runnable, tested product plus reproducible, documented cloud deployment steps. If tools or dependencies are unavailable, report the exact unverified items.

## Engineering principles
- Target .NET 10 LTS and C# 14, with ASP.NET Core minimal hosting (`Program.cs`), modern idioms, async I/O, DI, structured logging, nullable reference types and cancellation tokens.
- Use a current stable supported Angular release and TypeScript with standalone components. Pin versions and record them; check compatibility when possible.
- Favor readable code, small modules, explicit boundaries and minimal dependencies. Avoid unnecessary CQRS frameworks, MediatR, microservices, event buses, generic repositories, premature abstractions and elaborate editors.
- Enforce authentication, authorization, ownership and validation on the server, never only in the UI. Follow OWASP guidance, least privilege, secure configuration, no secrets in Git, safe Markdown rendering and no unsafe raw HTML.
- Use `AGENTS.md` for durable agent instructions and `PRODUCT_SPEC.md` as the functional/technical contract. Keep docs synchronized with decisions and implementation.
- Prefer deterministic reproducible builds; pin dependency versions, supply `.env.example` without secrets, and include useful README commands.
- Do not silently substitute paid Azure services or assume a free-tier entitlement. Cost safety is a hard requirement; see spec.

## Verification policy
### Initial full-product implementation (this assignment)
- Run restore/install, formatting/linting/type checks, backend and frontend builds, all available unit tests, integration tests, and browser E2E/smoke tests where tools are available.
- Start the local stack and exercise actual flows using browser automation (prefer Playwright): public list/detail/search, editor create by paste and .md import, editor edit/delete own article, admin edit/delete another author's article, access denied cases, editor application/approval, and basic navigation/responsiveness.
- Provide deterministic local development/test identities or an isolated mock authentication adapter ONLY for development and automated tests. Never enable mock auth in production. Production must use real external identity provider and validate real access tokens.
- Fix failures and rerun affected checks. Report commands, pass/fail, evidence, and any blocked checks. Never assert 'fully working' for paths that were not exercised.
- Do not execute real Azure deployments or tests that incur cloud costs. `terraform fmt`/`validate` and offline checks are fine; `plan` only when safe and credentials are explicitly available, and never `apply` without approval.

### Subsequent small changes
- Run focused relevant tests, formatting/linting and build of affected component when warranted. For cosmetic-only UI changes, do not automatically run the entire integration/E2E suite.
- Run the full suite for auth/security, persistence, deployment, API-contract changes, major refactors, or before release. Ask before unusually slow/expensive checks or cloud-backed tests.

## Completion report
Summarize what was built, exact local start commands, tests actually run and results, key architecture choices, cloud resources not provisioned, manual setup/permissions still needed, cost risks and known limitations. Clearly distinguish generated IaC from deployed infrastructure. No invented credentials, endpoints or screenshots.
