# Azure infrastructure (T-061)

`main.bicep` creates one environment in an existing resource group:

| Environment | Resource group | Parameters |
| --- | --- | --- |
| staging | `marketplace-staging-rg` | `parameters.staging.json` |
| production | `marketplace-prod-rg` | `parameters.prod.json` |

What it creates:

| Resource | Notes |
| --- | --- |
| Virtual network | One subnet for Container Apps, one delegated to PostgreSQL |
| PostgreSQL 17 Flexible Server | Burstable B1ms (staging) or B2s (production). Private network only, Microsoft Entra authentication only (no passwords), database `marketplace` |
| Private DNS zone | Resolves the database inside the network |
| Storage account | `product-files` (private) and `product-covers` (public read, T-085). Shared-key access is disabled |
| Key Vault | RBAC. Holds the six secrets below |
| Managed identities | `id-marketplace-api-*` and `id-marketplace-worker-*` (blob data and Key Vault secrets access), and `id-marketplace-migrate-*`, the database's Entra administrator, used by the migration job |
| Container Apps environment | Inside the network, logging to a Log Analytics workspace |
| Container apps, when `deployApps=true` | `marketplace-api`, `marketplace-worker` and `marketplace-web` |
| Budget | 20 per month in the billing currency, with alerts at 80 percent actual and 100 percent forecast. Created only when the `BUDGET_ALERT_EMAIL` GitHub environment variable is set |

## How it is deployed

The `Infrastructure` workflow (`.github/workflows/infra.yml`) signs in through GitHub OIDC, using the federated credentials on the app registration. It never uses a stored secret.

- **Pull requests:** a pull request that touches `deploy/bicep` gets a what-if for both environments in the job summary. Nothing is applied.
- **Applying:** a manual run with `apply: true` applies one environment. Production runs wait for the `production` environment's required reviewer.

Deployment happens in two stages:

1. **Infrastructure,** with `deployApps=false` (the default). Container Apps won't start while a Key Vault reference points at a missing secret, so the apps come after the secrets exist.
2. **Secrets, then apps.** Set the secrets (below), then run the CD pipeline (T-062). It publishes the images, runs the migration job, and deploys with `deployApps=true`.

## Key Vault secrets

These are set once per environment by someone with the Key Vault Secrets Officer role on the vault. The vault name is in the deployment output `keyVaultName`.

```sh
VAULT=<keyVaultName>
az keyvault secret set --vault-name $VAULT --name JwtSigningKey       --value "$(openssl rand -base64 48)"
az keyvault secret set --vault-name $VAULT --name StripeSecretKey     --value "sk_test_..."
az keyvault secret set --vault-name $VAULT --name StripeWebhookSecret --value "whsec_..."
az keyvault secret set --vault-name $VAULT --name AiApiKey            --value "..."
az keyvault secret set --vault-name $VAULT --name SmtpPassword        --value "..."
az keyvault secret set --vault-name $VAULT --name SeedAdminPassword   --value "$(openssl rand -base64 24)"
```

Notes:
- **Stripe webhook secret:** in Azure this comes from a webhook endpoint registered in the Stripe dashboard, pointing at `https://<apiUrl>/api/v1/webhooks/payments`. The Stripe CLI's local value only works for local testing.
- **No SMTP provider yet:** if there isn't one, set `SmtpPassword` to any placeholder so the reference resolves. Emails will then fail and wait in the outbox's dead letters until a provider is configured.

## Monthly cost

These are estimates from West Europe list prices. They are not quotes.

| | Staging | Production |
| --- | --- | --- |
| PostgreSQL compute and 32 GB storage | about $16 (B1ms) | about $54 (B2s) |
| Worker, always on (0.25 vCPU, 0.5 GiB), after the Container Apps free grant | about $14 | about $14 |
| Api and web | close to $0 at demo traffic (both scale to zero) | close to $0 at demo traffic |
| Log Analytics, Key Vault, storage, private DNS | about $1 to $4 | about $1 to $4 |
| **Total** | **about $31 to $35** | **about $70** |

Both totals are above the 20-per-month budget in section 15.5, so the budget alert will fire in a normal month.
