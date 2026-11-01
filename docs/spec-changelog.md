# Spec changelog

Changes to, and resolved contradictions in, the build specification.

## 2026-11-01

### T-004: `docker-compose.override.yml` is committed

**Resolves:** the contradiction between section 15.1, which calls the file git-ignored and created from `.env.example`, and section 5.1 and T-004, which treat it as part of the repository.

**Decision (repo owner):** commit `docker-compose.override.yml` with development-only values, per section 7.6, which places the development admin defaults in this file.

**Effect:**

- Section 15.1's "git-ignored, created from `.env.example`" no longer applies to this file.
- Every value in the file works only against local containers. Staging and production supply their own secrets (section 11.3), and startup validation rejects the development signing key there (section 16).
- The Api and Worker services use the Compose profile `app`, because their Dockerfiles do not exist until T-060. `docker compose up -d` starts only PostgreSQL, Azurite and Mailpit, as T-004 requires. From T-060 onward, `docker compose --profile app up -d` also starts the Api and Worker.
