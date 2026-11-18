# Deployment runbook

These are reproducible human-operated steps, not evidence of a completed deployment. No Azure account, identity tenant, service connection or remote environment was used during implementation.

## 1. Resolve the cost gate before creating anything

1. Read `AZURE_COST_REVIEW.md`. Container Apps consumption, Cosmos storage/backup/egress, identity usage, pipeline entitlements and external registry eligibility cannot guarantee zero charges.
2. Confirm subscription offer, spending restrictions, Free Tier eligibility, region availability, existing shared allowance consumption and whether a credit card is required. Check DEV + PROD together. Confirm the single Cosmos Free Tier account is available.
3. Under a strict free-only policy, stop here and continue locally. Do not provision based on trial credit or a budget alert. Obtain explicit account-owner approval for an acceptable alternative or exposure before proceeding.
4. Confirm Azure DevOps free hosted parallelism has actually been granted, or separately approve an existing self-hosted agent. Do not buy capacity.
5. Choose an approved public image registry with sufficient free storage, bandwidth and retention. The reference pipeline supports a public registry with `docker login` (for example an eligible public GHCR repository). It does not provision ACR. Registry eligibility and source/image publicity require the owner's decision. Never bake secrets into an image. If public images are unacceptable, stop and review private-registry costs/authentication rather than changing services silently.

## 2. Configure Entra External ID

1. In a verified eligible External ID external tenant, create separate DEV and PROD registrations/configurations. Use supported customer sign-in/user flows and check [current External ID documentation](https://learn.microsoft.com/en-us/entra/external-id/customers/overview-customers-ciam). No tenant is created by Terraform.
2. Register an API with a delegated scope such as `Articles.Access`. Set tokens to v2, record the exact token issuer and API audience from provider documentation and a cryptographically validated token.
3. Register an SPA client per environment. Configure exact generated site HTTPS redirect URI ending in `/` and logout URI; enable authorization code + PKCE, not implicit flow. Authorize the API's delegated scope and configure consent/user flow as required. A SPA has no client secret.
4. Confirm the discovery document issuer matches both `Auth__Issuer` and Angular `issuer` exactly. If provider discovery advertises endpoints on another host, assess the provider/library compatibility explicitly; do not turn off issuer or signature checks to make login work.
5. Record SPA client ID and scopes (`openid profile` plus the API scope; request `offline_access` only if the tenant's refresh-token configuration supports it). The browser must send the API access token, not its ID token or Microsoft Graph token.
6. Validate this login with real tenant credentials after deployment: sign in/out, refresh/expiry, wrong audience and wrong tenant rejection, CORS and callback behavior. Local test-token checks do not prove these provider interactions.

## 3. Establish the bootstrap administrator and manual editor linking

1. Choose the initial administrator's verified provider account. Independently verify account control using an authenticated provider session and an established trusted communication channel.
2. Have this account obtain an access token for this API. Validate it against the configured provider discovery keys, issuer and audience. Read `iss` and `sub` only after validation. The admin key is the literal `iss|sub`; do not assume `oid`, an email, an ID-token `sub`, a display name or a token decoded without signature verification is equivalent.
3. Set the environment's `Auth__AdminSubject` to this pair using protected deployment configuration. Keep an audited, protected recovery procedure for changing the bootstrap subject. The app has no bootstrap-by-email or self-service admin endpoint and cannot remove its bootstrap administrator.
4. For editor applications, review the private application in `/admin`. Independently contact the applicant, verify account control and obtain the applicant's validated API-token `iss|sub`. A signed-in member can read their own `/api/me` subject; the administrator must still verify that the account belongs to the applicant. Do not ask applicants to send bearer tokens by email/chat.
5. Enter that exact issuer/subject in the approval form. Only the configured issuer is accepted. Approval writes an Editor grant; the subject signs in/refreshes to see effective rights. No email is used as an authorization key. Do not process concurrent decisions for the same application.
6. If a request fails mid-approval, inspect the private application and role document through an authorized operator, then retry the same subject. Role and application writes are not one Cosmos transaction. Admin grant changes beyond the bootstrap are intentionally an audited operator task, not a public role-management API.

## 4. Prepare infrastructure and state

1. Install .NET SDK 10.0.401, Node 24.21.0, Docker, Azure CLI and Terraform 1.14.x on an approved workstation/agent. Log in only after the cost decision. Use short-lived federated Azure DevOps service connections rather than saved client secrets.
2. Manually register required Azure resource providers (`Microsoft.App`, `Microsoft.Web`, `Microsoft.DocumentDB`, `Microsoft.ManagedIdentity`) with appropriate subscription permission. Terraform disables automatic provider registration.
3. Review state strategy: the default backend is local, with confidential state excluded from Git. Back it up encrypted with exclusive operator access. For teamwork, explicitly approve and configure an Azure Blob backend or another approved backend, accounting for its storage/transaction charges and locking. Remote state storage is not provisioned by this repository. Never share a state file through a public artifact.
4. Copy `terraform.example.tfvars` to a local ignored file. Set the subscription and globally unique lowercase prefix. Supply both `dev` and `prod` environment maps with exact issuer, audience, verified admin subject, expected frontend origin and an approved public `repository@sha256:<digest>` image.
5. Obtain initial image digests by building the tested API container and publishing it to the approved registry. This is a human operation after approval; the implementation did not do it. Keep tag-to-digest provenance.
6. Predict or obtain generated frontend names, then confirm actual origins from Terraform outputs. If necessary, update only the environment's CORS origin in an approved follow-up plan. Do not use wildcard CORS. Apply creates no SWA deployment bindings, secrets, ACR or logging workspace.
7. First run offline validation:

   ```powershell
   terraform -chdir=infrastructure/terraform init -backend=false
   terraform -chdir=infrastructure/terraform fmt -check -recursive
   terraform -chdir=infrastructure/terraform validate
   ```

8. Only after explicit cost and provisioning approval, set `enable_cloud=true` and `cost_risk_accepted=true`. An authorized human may then run `terraform plan -out=approved.tfplan`, inspect every SKU/resource/region and proposed action, and separately approve `terraform apply approved.tfplan`. These cloud operations were not run by Codex. Do not commit a saved plan; it can contain sensitive values. Do not wire apply into CI.
9. Confirm Cosmos Free Tier is enabled, database throughput totals 800 RU/s, no extra regions/autoscale were added, API managed identities have only their own database role, and local database keys are disabled. Wait for RBAC propagation. Test that DEV identity cannot read PROD data.
10. Keep API `ASPNETCORE_ENVIRONMENT=Production`, `Auth__Development=false`, `Storage__Local=false`; set the exact issuer/audience/admin key, database/endpoint, frontend origin and managed identity client ID. Verify the app fails closed if a dev flag is enabled.

## 5. Configure Azure Repos and Azure Pipelines

1. Create pipelines for `pipelines/validate.yml`, `release.yml`, `deploy-dev.yml`, and `deploy-prod.yml` from the protected main branch. Set the default branch for manual runs to `refs/heads/main`.
2. On main, require a PR, reviewers, resolved comments and successful required automatic Build Validation using `validate.yml`. Azure Repos uses branch policies, not YAML `pr` triggers. Draft PRs must stay drafts until ready for required validation. Confirm policy behavior in the organization UI.
3. Remove regular contributors' direct push and bypass-policy permissions, including build service bypass permissions. Limit validation pipelines to read-only repository access; restrict job authorization scope and protect YAML changes. Do not allow PR builds access to deployment variable groups, service connections or environments. PR validation performs only local builds/tests and no PR deployment.
4. Create `devknowledge-dev` and `devknowledge-prod` environments. Add **exclusive lock checks to both** and use sequential locking. Add **required manual approval before production**, with separation from the deploy requester and no self-approval. Add protected-main branch checks to PROD and restrict the PROD service connection to the PROD pipeline only. Checks and approvals are Azure DevOps environment settings; YAML cannot enforce them alone. Do not enable deployment before these checks exist.
5. Create separate federated Azure Resource Manager service connections named `devknowledge-dev` and `devknowledge-prod`, scoped to the intended resources. The deployer needs Container Apps update and SWA deploy permissions, not Owner or Terraform provisioning permissions. Restrict service connections to explicitly authorized pipelines.
6. Create protected variable group `devknowledge-deployment`: `CloudDeploymentEnabled=false`, `CostSafetyApproved=false`, `RegistryUser`, secret `RegistryToken`, `ImageRepository` (including registry host), `ResourceGroup`, `DevContainerApp`, `DevApiOrigin`, `DevWebOrigin`, `DevOidcIssuer`, `DevOidcClientId`, `DevOidcScope`, secret `DevSwaToken`. Authorize only release and manual DEV pipelines.
7. Create protected `devknowledge-production`: gate flags default false, `ImageRepository`, `ResourceGroup`, `ProdContainerApp`, `ProdApiOrigin`, `ProdWebOrigin`, `ProdOidcIssuer`, `ProdOidcClientId`, `ProdOidcScope`, secret `ProdSwaToken`. Authorize only the production pipeline. PROD doesn't need registry push credentials.
8. In the PROD DownloadPipelineArtifact task, configure the **trusted main release pipeline definition ID** as documented in the YAML. Restrict artifact/run selection to that pipeline and retain validated main runs plus the `promotion` manifest. The promotion manifest is published only after DEV deployment/smokes succeed. Preserve the image by digest in the registry; never garbage-collect an in-use rollback digest.
9. Review all YAML and scripts, configure permissions, and test with cloud gate flags false first. Offline schema parsing is not proof Azure DevOps accepted or executed YAML. Feature deployments are limited to trusted revisions and authorized operators; do not give untrusted PR code deployment secrets.

## 6. DEV deployment, feature testing and restore

1. After approval, enable deployment gate flags for DEV. Merge to main validates and packages a container tar plus frontend with checksums and source metadata. Automatic DEV deployment is serialized through the shared environment lock; under that lock it compares current remote main HEAD and refuses stale main revisions.
2. The deploy step loads the validated container artifact, pushes it to the explicitly approved registry, resolves its digest, injects public runtime frontend configuration, deploys both artifacts, and checks health, public article list, frontend response and release commit metadata. No Terraform is applied.
3. A manual `deploy-dev` run from protected main accepts `revision` as a feature ref or exact commit, validates that revision, and deploys it to the same shared DEV under the same lock. The visible footer and `/release.json` identify the commit/source. This temporarily replaces main on DEV; it cannot trigger PROD.
4. Restore DEV by manually running `deploy-dev` with `revision=refs/heads/main`. A subsequent main merge also restores main through the automatic pipeline. No ephemeral PR environment exists.
5. Perform real Entra sign-in and every authorization/browser flow against DEV. Verify Cosmos persistence across revisions, application privacy, wrong-role denial, index/query behavior and throttling. Record results before promoting.

## 7. Manual PROD promotion and rollback

1. Select a retained, successfully DEV-validated **main release** run. Run `deploy-prod` using its immutable numeric run ID. Do not select a manual feature DEV run. The download task is pinned to the trusted main pipeline definition.
2. Approve the production environment check **before any mutation**. The script checks artifact hashes, main source ref and promotion provenance, then uses the previously resolved container digest. It does not rebuild Angular or the API. Only environment-specific public config differs.
3. Confirm PROD release metadata, health, real login, public content, editor ownership and admin moderation. A health response is liveness, not proof of Cosmos access; article listing and write/read/delete smoke with a real approved identity are also required.
4. Roll back by promoting a previous retained successful main release run through the same approval. The stack uses no migrations; keep data compatibility in future changes. Backend and frontend changes are sequential, not atomic: if one deployment fails, roll back both. No automatic cloud-consuming rollback is enabled.

## 8. Operations, privacy, cleanup

1. Decide application-data retention and deletion policy before opening public applications. Motivation and email are private personal data, not public articles. Cosmos backups and operator access require a privacy review. There is no automatic email notification or account provisioning.
2. No paid log sink is created. Structured console logs are available operationally only as supported by the hosting platform. Adding Log Analytics/Application Insights, Defender, alerts or backups requires a separate cost decision. Do not log access tokens, application bodies or secrets.
3. Check actual subscription usage frequently. Budgets warn; they do not prevent charges. Storage growth, unexpected traffic, other apps and DEV/PROD shared grants remain risks. Stop services/remove public access when required; do not rely on scale-to-zero as a spending cap.
4. Cleanup is an explicit human-approved destructive task: retain/export any required articles and application data securely, revoke deployment tokens/connections, remove registry artifacts after retention review, inspect a destroy plan, and approve resource deletion. Check for retained state storage, registry storage, backups and independently created services after cleanup. No cleanup command is run automatically.

## Known deployment verification gaps

Azure credentials, real External ID discovery/PKCE flows, Cosmos queries/indexes/RBAC, registry pushes, container execution, Azure Repos policy enforcement, pipeline tasks/locks/approvals, deployment CLI behavior and live billing eligibility remain unverified. Terraform format/schema validation is local evidence only.


## After all above next prompt:
The DevKnowledge application has been implemented locally.

I have now configured the required Azure and Azure DevOps accounts and permissions.

Review the deployment documentation, Terraform configuration, and Azure Pipelines.

Help me complete the deployment setup step by step, using the existing repository configuration.

Do not assume that any Azure resource already exists. Verify prerequisites before proceeding.

Do not execute terraform apply, create potentially billable resources, or deploy to production without my explicit approval.

First, help me deploy and verify DEV. Once DEV is working correctly, help me configure and validate the manually approved PROD deployment.

Ensure that the configured CI/CD behavior matches PRODUCT_SPEC.md.