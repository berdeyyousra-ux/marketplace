# Spec changelog

Changes to, and resolved contradictions in, the build specification.

## 2026-11-01

### Section 18: gitleaks replaces the secret grep

**Changes:** the section 18 Definition of Done item that requires `git diff --cached | grep -iE 'password|secret|key|token'` to be clean, and the T-006 scope.

**Decision (repo owner):** gitleaks in CI, run as the `secret-scan` job at T-006, is the real secret check and replaces the grep. The new checklist item is "No secrets in the diff (the `secret-scan` CI check passes)".

**Why the grep is replaced:** it matches ordinary code and text, such as every test that uses a `CancellationToken`, so it can never come back clean. A check that always fails cannot serve as a merge gate.

**R-04 justification:** gitleaks is not named in the spec, so adding it needs approval under R-04, which the repo owner gave.

- It is needed because no other automated secret check exists before T-065, and agent merges on passing checks depend on one.
- It runs on every PR, across the full history, as a required check. T-065 still adds GitHub secret scanning (section 11.7).
- It is a CI-only binary. It adds no NuGet package or application dependency, so R-11 and section 2 are unaffected. The workflow pins its version and verifies its SHA-256 checksum.

**Allowed values:** `.gitleaks.toml` allows only the development JWT signing key and the published Azurite emulator key, and only in `docker-compose.override.yml`. Those values in another file still fail the check, and the normal rules still apply to the rest of that file.

**Known limit:** gitleaks detects known token formats and some generic keys, not every secret. In testing it caught Stripe, AWS and GitHub tokens, but not a random base64 string assigned to `Jwt__SigningKey`. Passing this check does not prove a change has no secrets, so R-06 still applies.

### T-006: migration bundle step moved to T-013

**Resolves:** a sequencing gap. T-006 requires a migration bundle, but no migrations exist until T-013.

**Decision (repo owner):** T-006 leaves out the migration bundle step, and T-013 adds it.

### T-006: empty test suites

T-006 requires an empty test suite to pass, but Microsoft.Testing.Platform treats "no tests ran" as failure (exit code 8). CI allows that exit code only for the two projects with no tests: `Marketplace.UnitTests` until T-010 and `Marketplace.AiEvals` until T-074. The architecture and integration test projects must run tests.

### T-005: minimal `AppDbContext` added ahead of T-012

**Resolves:** a sequencing gap. T-005 needs `AddDbContext` and a database readiness check, but the backlog does not create `AppDbContext` until T-012.

**Decision (repo owner):** T-005 adds a minimal `AppDbContext` with no tables. T-012 builds on it.

### Section 21.6: the implementing agent may merge its own PRs after T-006

**Changes:** section 21.6, which says the agent "never merges its own PR" and that the human "reviews and merges PRs".

**Decision (repo owner):** once T-006 adds CI, the agent may merge its own PRs when checks pass.

**Effect:**

- Until T-006's CI is running, the human merges every PR.
- A passing check is not enough on its own. The agent still stops and asks about anything that is a real decision, and the section 0 stop-and-ask triggers still apply.
- Unchanged: production deploys need human approval (sections 15.4 and 21.6), and the agent never deploys to production unattended.

### T-004: `docker-compose.override.yml` is committed

**Resolves:** the contradiction between section 15.1, which calls the file git-ignored and created from `.env.example`, and section 5.1 and T-004, which treat it as part of the repository.

**Decision (repo owner):** commit `docker-compose.override.yml` with development-only values, per section 7.6, which places the development admin defaults in this file.

**Effect:**

- Section 15.1's "git-ignored, created from `.env.example`" no longer applies to this file.
- Every value in the file works only against local containers. Staging and production supply their own secrets (section 11.3), and startup validation rejects the development signing key there (section 16).
- The Api and Worker services use the Compose profile `app`, because their Dockerfiles do not exist until T-060. `docker compose up -d` starts only PostgreSQL, Azurite and Mailpit, as T-004 requires. From T-060 onward, `docker compose --profile app up -d` also starts the Api and Worker.
