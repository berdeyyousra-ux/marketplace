-- Database roles for the api and worker managed identities, named after the identities (section 15.5). The apps sign
-- in as these roles with Microsoft Entra tokens (T-061a). Run in the postgres database; skipped when a role exists.
SELECT pgaadauth_create_principal(:'api', false, false)
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'api');

SELECT pgaadauth_create_principal(:'worker', false, false)
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'worker');
