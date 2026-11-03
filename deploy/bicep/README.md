# Azure infrastructure (T-061)

`main.bicep` creates one environment in an existing resource group. Only **staging** is deployed, into `marketplace-staging-rg` with `parameters.staging.json`.

The resources are in **North Europe**: West Europe, the resource group's region, isn't accepting new customers for this subscription (https://aka.ms/locationineligible). A resource group's region is only where its metadata lives.

Production is deferred to keep the cost down; one live environment is enough for now (T-063, see `docs/spec-changelog.md`). The template still supports it: when it's wanted, add a `parameters.prod.json` (`postgresSku` `Standard_B2s`, `keyVaultPurgeProtection` `true`) and a production job to the workflow. Its resource group, role grants and federated credential already exist.

What it creates:

| Resource | Notes |
| --- | --- |
| Virtual network | One subnet for Container Apps, one delegated to PostgreSQL |
| PostgreSQL 17 Flexible Server | Burstable B1ms, the smallest tier. Private network only, Microsoft Entra authentication only (no passwords), database `marketplace` |
| Private DNS zone | Resolves the database inside the network |
| Storage account | `product-files` (private) and `product-covers` (public read, T-085). Shared-key access is disabled |
| Key Vault | RBAC. Holds the six secrets below |
| Managed identities | `id-marketplace-api-*` and `id-marketplace-worker-*` (blob data and Key Vault secrets access), and `id-marketplace-migrate-*`, the database's Entra administrator, used by the migration job |
| Container Apps environment | Inside the network, logging to a Log Analytics workspace |
| Container apps, when `deployApps=true` | `marketplace-api`, `marketplace-worker` and `marketplace-web` |
| Budget | 20 per month in the billing currency, with alerts at 80 percent actual and 100 percent forecast. Created only when the `BUDGET_ALERT_EMAIL` GitHub environment variable is set |

## How it is deployed

The `Infrastructure` workflow (`.github/workflows/infra.yml`) signs in through GitHub OIDC, using the federated credentials on the app registration. It never uses a stored secret.

- **Pull requests:** a pull request that touches `deploy/bicep` gets a staging what-if in the job summary. Nothing is applied.
- **Applying:** a manual run with `apply: true` applies staging.

Deployment happens in two stages:

1. **Infrastructure,** with `deployApps=false` (the default). Container Apps won't start while a Key Vault reference points at a missing secret, so the apps come after the secrets exist.
2. **Secrets, then apps.** Set the secrets (below), then run the CD pipeline (T-062). It publishes the images, runs the migration job, and deploys with `deployApps=true`.

## Key Vault secrets

These are set once by someone with the Key Vault Secrets Officer role on the vault. The vault name is in the deployment output `keyVaultName`.

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

These are estimates for staging from West Europe list prices. They are not quotes.

| | Running | Database stopped |
| --- | --- | --- |
| PostgreSQL B1ms compute | about $12 to $13 | $0 |
| PostgreSQL storage, 32 GB (the minimum) | about $4 | about $4 |
| Worker, always on (0.25 vCPU, 0.5 GiB) | about $3 to $5 if billed at the idle rate, up to about $14 if not | the same |
| Api and web | close to $0 at demo traffic (both scale to zero) | $0 |
| Log Analytics, Key Vault, storage, private DNS | about $1 to $3 | about $1 to $3 |
| **Total** | **about $20 to $24** | **about $8 to $10** |

About the Worker:
- **It can't scale to zero.** It turns payment webhooks into paid orders, sends email, executes refunds and expires orders. Nothing could wake it without delaying a payment confirmation by minutes.
- **Idle rate:** Container Apps bills a replica at a lower rate while it uses under 0.01 vCPU and under 1,000 bytes a second of network traffic. The Worker backs off its polling when the outbox is empty, to stay under that line. The first bill's meter data will show which rate applies.

## Stopping the database between demos

A stopped Flexible Server bills storage only. Azure starts it again automatically after 7 days, so stop it again if it should stay off.

```sh
SERVER=$(az postgres flexible-server list --resource-group marketplace-staging-rg --query "[0].name" --output tsv)
az postgres flexible-server stop  --resource-group marketplace-staging-rg --name "$SERVER"
az postgres flexible-server start --resource-group marketplace-staging-rg --name "$SERVER"   # takes a few minutes
az postgres flexible-server show  --resource-group marketplace-staging-rg --name "$SERVER" --query state
```

While the database is stopped:
- the Api's readiness check fails, so the site answers 503
- the Worker's readiness check fails, and it retries until the database is back

Start the database about five minutes before a demo. The apps recover on their own once it's back.
