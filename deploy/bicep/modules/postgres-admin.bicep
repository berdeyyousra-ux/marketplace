// The PostgreSQL server's Microsoft Entra administrator. A separate module because the resource is named after the
// identity's object id, which is only known once the identity exists; a module parameter may carry such a value.

param serverName string
param principalId string
param principalName string
param tenantId string

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: serverName
}

resource admin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = {
  parent: server
  name: principalId
  properties: {
    principalType: 'ServicePrincipal'
    principalName: principalName
    tenantId: tenantId
  }
}
