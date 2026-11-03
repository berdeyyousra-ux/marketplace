#!/bin/sh
# Runs inside the marketplace-migrate Container Apps job (deploy/bicep/main.bicep) as the database's Microsoft Entra
# administrator. Every step is idempotent, so the job can run on every deployment.
#   1. Fetch an access token for Azure Database for PostgreSQL from the job's managed identity.
#   2. Apply the EF Core migration bundle (section 15.6). On failure it rolls back, the job fails, and CD stops.
#   3. Create the api and worker database roles, tied to their managed identities, if they don't exist yet.
#   4. Grant those roles what the running apps need, and nothing more (grant-roles.sql).
set -eu

: "${PGHOST:?}" "${PGDATABASE:?}" "${PGUSER:?}" "${AZURE_CLIENT_ID:?}" "${API_ROLE:?}" "${WORKER_ROLE:?}"
: "${IDENTITY_ENDPOINT:?not running with a managed identity}" "${IDENTITY_HEADER:?}"

echo "Requesting a database token for ${PGUSER}"
token=$(curl --fail --silent --show-error \
  --header "X-IDENTITY-HEADER: ${IDENTITY_HEADER}" \
  "${IDENTITY_ENDPOINT}?api-version=2019-08-01&resource=https%3A%2F%2Fossrdbms-aad.database.windows.net&client_id=${AZURE_CLIENT_ID}" \
  | jq -r .access_token)
[ -n "$token" ] && [ "$token" != "null" ] || { echo "No token returned" >&2; exit 1; }
export PGPASSWORD="$token" PGSSLMODE=require

echo "Applying migrations to ${PGDATABASE} on ${PGHOST}"
./efbundle --connection "Host=${PGHOST};Database=${PGDATABASE};Username=${PGUSER};Password=${token};SSL Mode=Require" --no-color

# Entra roles are created through the pgaadauth extension, which lives in the postgres database.
echo "Ensuring the database roles ${API_ROLE} and ${WORKER_ROLE}"
psql --dbname postgres --set ON_ERROR_STOP=1 --quiet --set api="${API_ROLE}" --set worker="${WORKER_ROLE}" --file create-roles.sql

echo "Granting privileges in ${PGDATABASE}"
psql --dbname "${PGDATABASE}" --set ON_ERROR_STOP=1 --quiet --set api="${API_ROLE}" --set worker="${WORKER_ROLE}" --file grant-roles.sql

echo "Migration job finished"
