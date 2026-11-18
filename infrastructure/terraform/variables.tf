variable "subscription_id" {
  type        = string
  description = "Explicit subscription. No credentials are stored here."
}
variable "enable_cloud" {
  type        = bool
  default     = false
  description = "Keep false until cost exposure has been explicitly approved."
}
variable "cost_risk_accepted" {
  type        = bool
  default     = false
  description = "Human approval of uncertain billing and free-tier eligibility; not a zero-cost guarantee."
}
variable "prefix" {
  type    = string
  default = "devknowledge"
  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{2,18}$", var.prefix))
    error_message = "Use 3–19 lowercase letters, digits or hyphens, starting with a letter."
  }
}
variable "location" {
  type    = string
  default = "westeurope"
}
variable "environments" {
  description = "Distinct DEV/PROD identities, origins and immutable public registry image digests."
  type        = map(object({ issuer = string, audience = string, admin_subject = string, frontend_origin = string, image = string }))
  default     = {}
  validation {
    condition     = alltrue([for name, env in var.environments : contains(["dev", "prod"], name) && startswith(env.issuer, "https://") && startswith(env.frontend_origin, "https://") && startswith(env.admin_subject, "${env.issuer}|") && can(regex("@sha256:[a-f0-9]{64}$", env.image))])
    error_message = "Only dev/prod; HTTPS issuer/origin, issuer|subject admin and immutable image digest required."
  }
}
