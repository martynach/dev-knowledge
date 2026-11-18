output "api_origins" {
  value = { for name, api in azurerm_container_app.api : name => "https://${api.ingress[0].fqdn}" }
}
output "frontend_origins" {
  value = { for name, web in azurerm_static_web_app.frontend : name => "https://${web.default_host_name}" }
}
