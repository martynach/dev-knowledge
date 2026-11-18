locals {
  environments = var.enable_cloud ? var.environments : {}
}
resource "terraform_data" "cost_gate" {
  lifecycle {
    precondition {
      condition     = !var.enable_cloud || (var.cost_risk_accepted && length(var.environments) == 2)
      error_message = "STOP: review docs/AZURE_COST_REVIEW.md and obtain explicit approval for billing exposure before cloud setup. Configure both dev and prod."
    }
  }
}
resource "azurerm_resource_group" "app" {
  count      = var.enable_cloud ? 1 : 0
  name       = "${var.prefix}-rg"
  location   = var.location
  depends_on = [terraform_data.cost_gate]
}
resource "azurerm_cosmosdb_account" "app" {
  count                         = var.enable_cloud ? 1 : 0
  name                          = "${var.prefix}-cosmos"
  location                      = azurerm_resource_group.app[0].location
  resource_group_name           = azurerm_resource_group.app[0].name
  offer_type                    = "Standard"
  kind                          = "GlobalDocumentDB"
  free_tier_enabled             = true
  local_authentication_disabled = true
  automatic_failover_enabled    = false
  consistency_policy { consistency_level = "Session" }
  geo_location {
    location          = var.location
    failover_priority = 0
  }
  capacity { total_throughput_limit = 1000 }
}
resource "azurerm_cosmosdb_sql_database" "environment" {
  for_each            = local.environments
  name                = "devknowledge-${each.key}"
  resource_group_name = azurerm_resource_group.app[0].name
  account_name        = azurerm_cosmosdb_account.app[0].name
  throughput          = 400
}
resource "azurerm_cosmosdb_sql_container" "content" {
  for_each              = local.environments
  name                  = "content"
  resource_group_name   = azurerm_resource_group.app[0].name
  account_name          = azurerm_cosmosdb_account.app[0].name
  database_name         = azurerm_cosmosdb_sql_database.environment[each.key].name
  partition_key_paths   = ["/kind"]
  partition_key_version = 2
  indexing_policy {
    indexing_mode = "consistent"
    included_path { path = "/sortDate/?" }
    included_path { path = "/value/authorId/?" }
    included_path { path = "/value/tags/[]/?" }
    excluded_path { path = "/*" }
  }
}
resource "azurerm_container_app_environment" "app" {
  count               = var.enable_cloud ? 1 : 0
  name                = "${var.prefix}-environment"
  location            = var.location
  resource_group_name = azurerm_resource_group.app[0].name
  logs_destination    = ""
}
resource "azurerm_user_assigned_identity" "api" {
  for_each            = local.environments
  name                = "${var.prefix}-${each.key}-api"
  location            = var.location
  resource_group_name = azurerm_resource_group.app[0].name
}
resource "azurerm_cosmosdb_sql_role_assignment" "api" {
  for_each            = local.environments
  resource_group_name = azurerm_resource_group.app[0].name
  account_name        = azurerm_cosmosdb_account.app[0].name
  role_definition_id  = "${azurerm_cosmosdb_account.app[0].id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"
  principal_id        = azurerm_user_assigned_identity.api[each.key].principal_id
  scope               = "${azurerm_cosmosdb_account.app[0].id}/dbs/${azurerm_cosmosdb_sql_database.environment[each.key].name}"
}
resource "azurerm_container_app" "api" {
  for_each                     = local.environments
  name                         = "${var.prefix}-${each.key}-api"
  container_app_environment_id = azurerm_container_app_environment.app[0].id
  resource_group_name          = azurerm_resource_group.app[0].name
  revision_mode                = "Single"
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.api[each.key].id]
  }
  template {
    min_replicas = 0
    max_replicas = 1
    container {
      name   = "api"
      image  = each.value.image
      cpu    = 0.25
      memory = "0.5Gi"
      dynamic "env" {
        for_each = {
          ASPNETCORE_ENVIRONMENT = "Production"
          Auth__Development      = "false"
          Storage__Local         = "false"
          Auth__Issuer           = each.value.issuer
          Auth__Audience         = each.value.audience
          Auth__AdminSubject     = each.value.admin_subject
          Cosmos__Endpoint       = azurerm_cosmosdb_account.app[0].endpoint
          Cosmos__Database       = azurerm_cosmosdb_sql_database.environment[each.key].name
          Frontend__Origin       = each.value.frontend_origin
          AZURE_CLIENT_ID        = azurerm_user_assigned_identity.api[each.key].client_id
        }
        content {
          name  = env.key
          value = env.value
        }
      }
      liveness_probe {
        transport = "HTTP"
        path      = "/health"
        port      = 8080
      }
      readiness_probe {
        transport = "HTTP"
        path      = "/health"
        port      = 8080
      }
    }
  }
  ingress {
    external_enabled           = true
    target_port                = 8080
    allow_insecure_connections = false
    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }
  depends_on = [azurerm_cosmosdb_sql_role_assignment.api]
  lifecycle { ignore_changes = [template[0].container[0].image] }
}
resource "azurerm_static_web_app" "frontend" {
  for_each            = local.environments
  name                = "${var.prefix}-${each.key}-web"
  resource_group_name = azurerm_resource_group.app[0].name
  location            = var.location
  sku_tier            = "Free"
  sku_size            = "Free"
}
