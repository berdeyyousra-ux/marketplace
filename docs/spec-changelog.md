# Spec changelog

Changes to, and resolved contradictions in, the build specification.

## 2026-11-02

### Section 9.11: IPaymentProvider gains ReadStoredEvent

The Worker processes webhook events after they are stored, but `ParseWebhook` needs the original signature headers, which are not stored, and rejects payloads older than the 5-minute tolerance. `ReadStoredEvent(rawPayload)` parses a payload that `ParseWebhook` already verified at receive time. It is used only on rows from the `payment_events` inbox, which hold only verified events.

### Unassigned endpoints get a task

No backlog task lists EP-09, EP-28, EP-61, EP-65 or EP-99. They are built with the task whose story needs them:

- EP-09 (`GET /me`): with T-021, done.
- EP-28 (`GET /sellers/me/products`): with T-024, for US-15.
- EP-61 (`GET /refund-requests/mine`): with T-072.
- EP-65 (refund retry): with T-073.
- EP-99 (development fake-payment trigger): with T-034 or T-035.

The section 7.6 admin bootstrap also has no task. It is built with T-027, whose role matrix needs an admin.

On EP-28, a sale is an order item that is still Active in an order with a `paid_at`. Refunded items are not counted.

### Section 15.1: Azurite runs with --skipApiVersionCheck

Azurite rejects requests that use a storage API version newer than it knows, and the Azure SDK pinned in T-003 uses a newer one than current Azurite releases. Local Compose and the test container (pinned to Azurite 3.37.0) therefore pass `--skipApiVersionCheck`. This only affects the local emulator; real Azure Storage is unaffected.

### Section 4.5: the rate limiter runs after authentication

**Resolves:** a contradiction. Section 4.5 places the rate limiter (step 7) before authentication (step 8), but section 11.4 limits the `download` and `global` policies per user, and the user is only known after authentication.

**Change:** `UseRateLimiter` runs immediately after `UseAuthentication` and before `UseAuthorization`. Every section 11.4 limit applies exactly as written, and the IP-based policies (`auth`, `refresh`, `webhook`) are unaffected. The cost is that a request with an invalid token is checked before it is counted.

### Section 16: new configuration key RateLimiting:Enabled

Defaults to `true`. The integration-test host sets it to `false`, because every test request comes from the same address and limits would leak between unrelated tests. The rate limit tests turn it back on in their own host. Production must leave it at the default.

### T-021 and T-038: account email handlers move into T-021

**Resolves:** a sequencing gap. T-021 must test US-02 and US-07, which need the verification and reset tokens. Section 9.1 has the Worker's email handlers issue those tokens, but the backlog lists those handlers under T-038.

**Change:** T-021 includes the `EmailVerificationRequested`, `PasswordResetRequested` and `AccountExistsNotice` handlers, `IEmailSender` with the MailKit SMTP sender (D-27), and the test fake. T-038 keeps the order receipt email and anything else in its scope. No behavior changes; only the task each piece lands in.

### Section 16: new configuration key Email:LinkBaseUrl

Account emails link to the web client, for example `{LinkBaseUrl}/verify-email?token=...`, and section 16 has no key for that URL. `Email:LinkBaseUrl` defaults to `http://localhost:3000`, matching the default CORS origin. It is not a secret.

### Section 5.4: InvariantGlobalization is turned off

**Resolves:** a conflict. Section 5.4 sets `InvariantGlobalization` to `true`, and in that mode .NET's `string.Normalize` returns text unchanged, so section 11.2's NFC normalization could not work. An integration test showed the normalization silently doing nothing.

**Decision (repo owner):** `InvariantGlobalization` is `false`. Code continues to pass `CultureInfo.InvariantCulture` explicitly wherever formatting matters. The Docker images (T-060) must include the ICU library, which the standard .NET images do and chiseled images do not.

### Section 11.2: passwords and tokens are normalized but not trimmed

**Changes:** section 11.2, which says all request strings are trimmed and normalized to NFC.

**Decision (repo owner):** request string properties whose names contain "password" or "token" are NFC-normalized but never trimmed, so leading or trailing spaces in a password are kept rather than silently removed. Every other string is trimmed and NFC-normalized.

### Section 11.1: the common-password list is SecLists' 10k list

**Resolves:** section 11.1 requires an embedded list of the 10,000 most common passwords but names no source.

**Decision (repo owner):** use `10k-most-common.txt` from the SecLists project (MIT license), embedded as a resource with its source and license noted. Added with the T-021 registration flow.

### Section 5.3: the Worker references Serilog.Sinks.Console

**Resolves:** a gap. D-30 requires Serilog JSON logging to the console in both processes, but section 5.3's Worker package list has no console sink. The Api gets one only indirectly, through `Serilog.AspNetCore`.

**Decision (repo owner):** the Worker references `Serilog.Sinks.Console` directly, at version 6.1.1, the version the Api already resolves. It is pinned in `Directory.Packages.props`. This adds no new library to the solution, only a direct reference.

### Sections 10.1 and 10.3: outbox and idempotency SQL take the time from TimeProvider

**Resolves:** a conflict between R-07, which requires all time to come from the injected `TimeProvider`, and the SQL in sections 10.1 and 10.3, which uses the database's `now()`.

**Decision (repo owner):** the queries keep their logic but take the current time as a parameter (`@now`) from `TimeProvider`. Lease expiry, retry backoff and idempotency locks can then be tested with the fake clock, for example in FM-11.

### Section 9.6: a second paid order for an already-owned product is cancelled and refunded

**Resolves:** a gap. Section 9.3 lets a buyer hold several Pending orders for the same product. If two are paid, the second cannot create a download grant (`ux_download_grants_active_buyer_product`), and section 9.6 has no path for it.

**Decision (repo owner):** the duplicate order is cancelled, `AutoRefundPayment` is queued, and an audit row is written, the same way FM-07 handles a payment for a Cancelled order. The buyer keeps one copy and gets the second payment back.

### Section 9.6 step 3: the amount-mismatch audit row is written in its own transaction

**Resolves:** a conflict. Section 9.6 step 3 writes `payment.amount_mismatch` and then throws inside the same transaction, which rolls the audit row back, but FM-08 requires the row to exist.

**Decision (repo owner):** the payment transaction is rolled back, the audit row is written in a separate short transaction, and the message is then dead-lettered. The order stays Pending.

### Section 9.9 and US-41: the reconciliation window is 10 minutes to 48 hours

**Resolves:** a contradiction. Section 9.9 selects payments created less than 24 hours ago but also says to cancel payments still unpaid after 24 hours, which the selection can never include.

**Decision (repo owner):** reconciliation checks `RequiresPayment` payments created 10 minutes to 48 hours ago. Those older than 24 hours that are still unpaid are cancelled at the provider and marked Failed. This replaces the 24-hour bound in US-41.

### T-030 is built before T-021

T-021's register and password flows queue emails through the outbox (section 9.1), which T-030 builds. T-030 depends only on T-015, so building it first respects R-02's dependency order.

### R-05: analyzer rules that conflict with names or styles the spec requires

**Changes:** R-05, which says never to weaken an analyzer rule to make something pass.

**Decision (repo owner):** when an analyzer rule conflicts with a name or style that this spec explicitly requires, the agent may suppress that one rule at that one place, with a written justification in the code. The PR lists the suppression and may be merged when checks pass. Existing examples are `Error` (CA1716, section 6.1), and the test naming rules in `tests/.editorconfig` (CA1707 and the Async suffix, sections 3 and 14.4).

**Still needs the repo owner:** any other weakened, suppressed or removed rule, test or CI check.

### Sections 9.6 and 9.8: zero-amount ledger entries are not written

**Resolves:** a conflict between section 6.4, where a platform fee can round to 0.00 (for example at 1 basis point, a mandatory test case), and the `ck_ledger_amount_sign` check in section 7.2, which requires PlatformFee entries to be negative and FeeRefund entries to be positive.

**Decision (repo owner):** a ledger entry whose amount is 0.00 is not written.

**Effect:**

- Section 9.6 writes the Sale entry and, only when the platform fee is non-zero, the PlatformFee entry.
- Section 9.8 writes the Refund entry and, only when the platform fee is non-zero, the FeeRefund entry.
- The section 7.2 check is unchanged, and seller balances are the same either way.
- `LedgerEntry` rejects a zero amount, so callers must skip zero entries rather than create them.

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
