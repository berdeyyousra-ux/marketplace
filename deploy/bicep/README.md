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

## Before the first deployment

A new subscription has most resource providers unregistered, and deployments then fail with `MissingSubscriptionRegistration`. Neither validate nor what-if checks registration. The deployment identity can't register providers, because it only holds rights on the resource group. Someone with subscription rights registers them once:

```sh
for ns in Microsoft.Network Microsoft.KeyVault Microsoft.ManagedIdentity Microsoft.OperationalInsights \
          Microsoft.Storage Microsoft.DBforPostgreSQL Microsoft.App; do
  az provider register --namespace $ns
done
```

`Microsoft.Consumption`, used for the budget, is registered by default.

## How it is deployed

The `Infrastructure` workflow (`.github/workflows/infra.yml`) signs in through GitHub OIDC, using the federated credentials on the app registration. It never uses a stored secret.

- **Pull requests:** a pull request that touches `deploy/bicep` gets a staging what-if in the job summary. Nothing is applied.
- **Applying:** a manual run with `apply: true` applies staging.

Deployment happens in two stages:

1. **Infrastructure,** with `deployApps=false` (the default). Container Apps won't start while a Key Vault reference points at a missing secret, so the apps come after the secrets exist.
2. **Secrets, then apps.** Set the secrets (below), then run the CD pipeline (T-062). It publishes the images, runs the migration job, and deploys with `deployApps=true`.

## Continuous deployment (T-062)

`.github/workflows/cd-staging.yml` deploys `main` to staging after CI passes, and can also be started by hand. In order, it:
1. builds the api, worker, migration and web images and pushes them to GHCR, tagged with the commit
2. updates the infrastructure and the `marketplace-migrate` job
3. runs the job, which applies the migrations and grants the app database roles
4. deploys the apps (`deployApps=true`)
5. smoke-tests the API and the web client
6. puts the previous images back if the deployment or the smoke test fails

**Image visibility:** Container Apps pulls from GHCR without credentials, so the packages must be public. Packages pushed from this public repository with its source label inherit public visibility, and the workflow checks it. If one was made private, open it from the Packages tab on the GitHub profile and change its visibility in Package settings.

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
- **Stripe publishable key:** not here. It is public by design (the browser needs it to mount the payment form, T-064), so it is the `STRIPE_PUBLISHABLE_KEY` GitHub environment variable, passed as the `stripePublishableKey` parameter. Both .NET processes refuse to start without it when the provider is Stripe, so set it before the first apply.
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

## Pausing and resuming staging

Stopping the database (below) only pauses its compute; the Worker and storage still bill. To stop all charges, delete the resource group: `az group delete --name marketplace-staging-rg --yes`. Staging was paused this way on 2026-11-03 (see the spec changelog).

Deleting the group also removes the role assignments scoped to it, and the Key Vault is only soft-deleted. To resume, in this order:

1. **Re-create the resource group,** in the same region as before. Its region only holds metadata; the resources still go to North Europe.
   ```sh
   az group create --name marketplace-staging-rg --location westeurope
   ```
2. **Grant the deployment identity its roles again.** Role assignments scoped to a resource group are deleted with it.
   ```sh
   RG_ID=$(az group show --name marketplace-staging-rg --query id --output tsv)
   az role assignment create --assignee 4cac444d-f6ce-491e-b5d1-2ba346592a37 --role Contributor --scope "$RG_ID"
   az role assignment create --assignee 4cac444d-f6ce-491e-b5d1-2ba346592a37 --role "User Access Administrator" --scope "$RG_ID"
   ```
3. **Recover the Key Vault, before 2027-02-01.** It is soft-deleted, not gone. Recovering it brings back all six secrets. The template uses the same vault name, so applying while the name is still held in soft delete would fail. Then grant yourself Key Vault Secrets Officer on the vault again.
   ```sh
   az keyvault recover --name kv-mkt-staging-fndsuo
   az role assignment create --assignee "$(az ad signed-in-user show --query id --output tsv)" --role "Key Vault Secrets Officer" \
     --scope "$(az keyvault show --name kv-mkt-staging-fndsuo --query id --output tsv)"
   ```
   If the vault can no longer be recovered (after 2027-02-01, or purged), skip this step. The apply then creates an empty vault, and all six secrets are set again (commands under "Key Vault secrets").
4. **Apply the infrastructure:** run the Infrastructure workflow with `apply: true`.
   - The managed identities are new, so the template creates fresh vault and storage role assignments for them.
   - If the apply reports `RoleAssignmentUpdateNotPermitted`, the recovered vault kept an assignment for the deleted identity. List the vault's assignments with `az role assignment list --scope <vault id>`, delete the one whose principal no longer exists, then re-run.
5. **Deploy the apps:** re-enable CD if it was disabled (`gh workflow enable "CD staging"`), then start it by hand or merge to `main`.
   - The database is new, so the migration job recreates the schema and the app roles.
   - The API seeds the admin account from `SeedAdminPassword` on its first start.
6. **Stripe webhook.** The Container Apps environment is new, so the app URLs change. Create a webhook endpoint in the Stripe dashboard at the new API URL plus `/api/v1/webhooks/payments`, set its signing secret as `StripeWebhookSecret`, and delete the old endpoint.

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
