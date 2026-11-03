// Section 15.5: the Azure resources for one environment, deployed into an existing resource group
// (marketplace-staging-rg or marketplace-prod-rg; see docs/spec-changelog.md).
//
// Two stages. With deployApps=false only the infrastructure is created: network, PostgreSQL, storage, Key Vault,
// identities and the Container Apps environment. The Key Vault secrets are then set by hand (deploy/bicep/README.md),
// because Container Apps refuse to start with a secret reference that doesn't resolve. With deployApps=true the
// three apps are added. Images come from GHCR (public repository); until the CD pipeline (T-062) publishes them the
// image parameters default to a placeholder.
//
// No passwords or keys exist: the apps reach PostgreSQL (Entra authentication only) and Blob storage (shared keys
// disabled) with their user-assigned managed identities (T-061a).

targetScope = 'resourceGroup'

@allowed(['staging', 'prod'])
param environmentName string

param location string = resourceGroup().location

@description('PostgreSQL compute: Standard_B1ms (staging) or Standard_B2s (production), Burstable tier.')
param postgresSku string

param postgresStorageGb int = 32

@description('Purge protection keeps a deleted vault for 90 days and cannot be turned off. On for production only.')
param keyVaultPurgeProtection bool

param deployApps bool = false

param apiImage string = 'mcr.microsoft.com/k8se/quickstart:latest'
param workerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'
param webImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@allowed(['Fake', 'Anthropic', 'OpenAi'])
param aiProvider string = 'OpenAi'
param aiModel string = 'gpt-4o-mini'

param smtpHost string = ''
param smtpPort int = 587
param smtpUser string = ''
param emailFromAddress string = 'noreply@marketplace.dev'

param adminEmail string = 'admin@marketplace.dev'

@description('Monthly budget for the resource group, in the billing currency (section 15.5: 20 EUR).')
param budgetAmount int = 20

@description('First day of the budget period. Fixed, because a budget\'s start date cannot change once created.')
param budgetStartDate string = '2026-11-01T00:00:00Z'

@description('Who receives budget alerts. Passed from the BUDGET_ALERT_EMAIL GitHub variable; no budget without it.')
param budgetContactEmail string = ''

var suffix = take(uniqueString(resourceGroup().id), 6)
var aspnetEnvironment = environmentName == 'prod' ? 'Production' : 'Staging'
var tags = { app: 'marketplace', environment: environmentName }

var roles = {
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
}

// ---- Monitoring ----

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-marketplace-${environmentName}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ---- Network: the apps' subnet, and PostgreSQL reachable only from inside the network ----

resource network 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-marketplace-${environmentName}'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: ['10.40.0.0/16'] }
    subnets: [
      {
        name: 'apps'
        properties: {
          addressPrefix: '10.40.0.0/23'
          delegations: [{ name: 'container-apps', properties: { serviceName: 'Microsoft.App/environments' } }]
        }
      }
      {
        name: 'postgres'
        properties: {
          addressPrefix: '10.40.2.0/28'
          delegations: [{ name: 'postgres', properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' } }]
        }
      }
    ]
  }
}

resource postgresDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'marketplace-${environmentName}.private.postgres.database.azure.com'
  location: 'global'
  tags: tags
}

resource postgresDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: postgresDns
  name: 'vnet-marketplace-${environmentName}'
  location: 'global'
  properties: {
    virtualNetwork: { id: network.id }
    registrationEnabled: false
  }
}

// ---- Identities: one per app, plus the migration identity that administers the database ----

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-marketplace-api-${environmentName}'
  location: location
  tags: tags
}

resource workerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-marketplace-worker-${environmentName}'
  location: location
  tags: tags
}

// The PostgreSQL Entra administrator. The migration job (T-062) runs as it: it applies the migration bundle and
// creates the api and worker database roles with pgaadauth_create_principal.
resource migrateIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-marketplace-migrate-${environmentName}'
  location: location
  tags: tags
}

// ---- PostgreSQL 17, Entra authentication only, private network only ----

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'psql-marketplace-${environmentName}-${suffix}'
  location: location
  tags: tags
  sku: { name: postgresSku, tier: 'Burstable' }
  properties: {
    version: '17'
    storage: { storageSizeGB: postgresStorageGb, autoGrow: 'Disabled' }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Disabled'
      tenantId: subscription().tenantId
    }
    network: {
      delegatedSubnetResourceId: '${network.id}/subnets/postgres'
      privateDnsZoneArmResourceId: postgresDns.id
      publicNetworkAccess: 'Disabled'
    }
  }
  dependsOn: [postgresDnsLink]
}

module postgresAdmin 'modules/postgres-admin.bicep' = {
  name: 'postgres-admin'
  params: {
    serverName: postgres.name
    principalId: migrateIdentity.properties.principalId
    principalName: migrateIdentity.name
    tenantId: subscription().tenantId
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'marketplace'
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

// ---- Storage: private product files, public product covers (T-085), no shared keys ----

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take('stmkt${environmentName}${uniqueString(resourceGroup().id)}', 24)
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowSharedKeyAccess: false
    // Needed for the product-covers container only; product-files stays private.
    allowBlobPublicAccess: true
    defaultToOAuthAuthentication: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 7 }
  }
}

resource filesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'product-files'
  properties: { publicAccess: 'None' }
}

resource coversContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'product-covers'
  properties: { publicAccess: 'Blob' }
}

// ---- Key Vault: RBAC; the apps read secrets through their identities ----

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-mkt-${environmentName}-${suffix}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: keyVaultPurgeProtection ? true : null
  }
}

// ---- Role assignments ----

resource apiBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, apiIdentity.id, roles.storageBlobDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource workerBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, workerIdentity.id, roles.storageBlobDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource apiSecretsAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, apiIdentity.id, roles.keyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource workerSecretsAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, workerIdentity.id, roles.keyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---- Container Apps environment, inside the network so the apps reach PostgreSQL ----

resource appsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-marketplace-${environmentName}'
  location: location
  tags: tags
  properties: {
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
    vnetConfiguration: {
      infrastructureSubnetId: '${network.id}/subnets/apps'
      internal: false
    }
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// App addresses are known before the apps exist, so the api and web can each name the other without a cycle.
var apiFqdn = 'marketplace-api.${appsEnvironment.properties.defaultDomain}'
var webFqdn = 'marketplace-web.${appsEnvironment.properties.defaultDomain}'
var blobEndpoint = storage.properties.primaryEndpoints.blob

func secretRef(vaultUri string, name string, identityId string) object => {
  name: toLower(name)
  keyVaultUrl: '${vaultUri}secrets/${name}'
  identity: identityId
}

// Each app signs in to PostgreSQL as the database role named after its identity (created by the migration job).
func identitySettings(postgresHost string, identityName string, identityClientId string) array => [
  { name: 'ConnectionStrings__Database', value: 'Host=${postgresHost};Database=marketplace;Username=${identityName};SSL Mode=Require' }
  { name: 'Azure__ManagedIdentityClientId', value: identityClientId }
]

// Settings both .NET processes read; each validates its options at startup (section 16).
var sharedSettings = [
  { name: 'ConnectionStrings__BlobStorage', value: blobEndpoint }
  { name: 'Storage__CoverPublicBaseUrl', value: '${blobEndpoint}product-covers/' }
  { name: 'Payments__Provider', value: 'Stripe' }
  { name: 'Payments__Stripe__SecretKey', secretRef: 'stripesecretkey' }
  { name: 'Payments__Stripe__WebhookSecret', secretRef: 'stripewebhooksecret' }
  { name: 'Ai__Provider', value: aiProvider }
  { name: 'Ai__Model', value: aiModel }
  { name: 'Ai__ApiKey', secretRef: 'aiapikey' }
  { name: 'Email__SmtpHost', value: smtpHost }
  { name: 'Email__SmtpPort', value: string(smtpPort) }
  { name: 'Email__SmtpUser', value: smtpUser }
  { name: 'Email__SmtpPassword', secretRef: 'smtppassword' }
  { name: 'Email__FromAddress', value: emailFromAddress }
  { name: 'Email__LinkBaseUrl', value: 'https://${webFqdn}' }
]

resource api 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: 'marketplace-api'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${apiIdentity.id}': {} } }
  properties: {
    managedEnvironmentId: appsEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
      secrets: [
        secretRef(vault.properties.vaultUri, 'JwtSigningKey', apiIdentity.id)
        secretRef(vault.properties.vaultUri, 'StripeSecretKey', apiIdentity.id)
        secretRef(vault.properties.vaultUri, 'StripeWebhookSecret', apiIdentity.id)
        secretRef(vault.properties.vaultUri, 'AiApiKey', apiIdentity.id)
        secretRef(vault.properties.vaultUri, 'SmtpPassword', apiIdentity.id)
        secretRef(vault.properties.vaultUri, 'SeedAdminPassword', apiIdentity.id)
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(sharedSettings, identitySettings(postgres.properties.fullyQualifiedDomainName, apiIdentity.name, apiIdentity.properties.clientId), [
            { name: 'ASPNETCORE_ENVIRONMENT', value: aspnetEnvironment }
            { name: 'Jwt__SigningKey', secretRef: 'jwtsigningkey' }
            { name: 'Cors__AllowedOrigins__0', value: 'https://${webFqdn}' }
            { name: 'Api__ExposeDocs', value: environmentName == 'prod' ? 'false' : 'true' }
            { name: 'Seed__Admin__Email', value: adminEmail }
            { name: 'Seed__Admin__Password', secretRef: 'seedadminpassword' }
          ])
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 10, failureThreshold: 3 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 5, failureThreshold: 2 }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [{ name: 'http', http: { metadata: { concurrentRequests: '50' } } }]
      }
    }
  }
  dependsOn: [apiBlobAccess, apiSecretsAccess, filesContainer, coversContainer, database]
}

resource worker 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: 'marketplace-worker'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${workerIdentity.id}': {} } }
  properties: {
    managedEnvironmentId: appsEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      secrets: [
        secretRef(vault.properties.vaultUri, 'StripeSecretKey', workerIdentity.id)
        secretRef(vault.properties.vaultUri, 'StripeWebhookSecret', workerIdentity.id)
        secretRef(vault.properties.vaultUri, 'AiApiKey', workerIdentity.id)
        secretRef(vault.properties.vaultUri, 'SmtpPassword', workerIdentity.id)
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: workerImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(sharedSettings, identitySettings(postgres.properties.fullyQualifiedDomainName, workerIdentity.name, workerIdentity.properties.clientId), [
            { name: 'DOTNET_ENVIRONMENT', value: aspnetEnvironment }
          ])
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8081 }, periodSeconds: 10, failureThreshold: 3 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8081 }, periodSeconds: 5, failureThreshold: 2 }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
        rules: [{ name: 'cpu', custom: { type: 'cpu', metadata: { type: 'Utilization', value: '70' } } }]
      }
    }
  }
  dependsOn: [workerBlobAccess, workerSecretsAccess, database]
}

// T-083: the web client is deployed alongside the API. It calls the API from its server only, so it needs no
// identity and no secrets.
resource web 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: 'marketplace-web'
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: appsEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      ingress: { external: true, targetPort: 3000, transport: 'auto', allowInsecure: false }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'API_BASE_URL', value: 'https://${apiFqdn}' }
            { name: 'COVER_IMAGE_BASE_URL', value: '${blobEndpoint}product-covers/' }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
        rules: [{ name: 'http', http: { metadata: { concurrentRequests: '50' } } }]
      }
    }
  }
}

// ---- Budget alert (section 15.5) ----

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = if (!empty(budgetContactEmail)) {
  name: 'budget-marketplace-${environmentName}'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      actualOver80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [budgetContactEmail]
      }
      forecastOver100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [budgetContactEmail]
      }
    }
  }
}

output keyVaultName string = vault.name
output postgresServer string = postgres.properties.fullyQualifiedDomainName
output postgresAdminIdentity string = migrateIdentity.name
output storageAccount string = storage.name
output apiUrl string = 'https://${apiFqdn}'
output webUrl string = 'https://${webFqdn}'
