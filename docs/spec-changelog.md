# Spec changelog

Changes to, and resolved contradictions in, the build specification.

## 2026-11-02

### T-086: corrections to `docs/frontend-design-plan.md` after rendering the token set

**Plan:** `docs/frontend-design-plan.md` defines ten colour tokens, states that every pairing meets WCAG AA, and asks for generated covers from eight hues at similar saturation and lightness.

**Corrections (approved by the repo owner after reviewing the rendered `/design-system` page):**

1. **Text on the accent.** `--accent-contrast` is added: `#ffffff` in light mode, `#0c0a09` in dark. White on the dark-mode accent `#14b8a6` is 2.49:1; near-black is 7.94:1.
2. **Form-control borders.** `--border-strong` (`#78716c` in both modes) is added for inputs, textareas and selects.
   - `--border` is 1.26:1 (light) and 1.15:1 (dark) against the surface, below the 3:1 a control's boundary needs (WCAG 1.4.11). The new border gives 4.80:1 and 3.65:1.
   - `--border` stays for decorative card edges and dividers.
3. **Generated-cover hues.** The yellow, olive and yellow-green band (about 41° to 150° in OKLCH hue) is dropped.
   - The eight hues sit 33° apart, from 150° round to 21°, each pair fading 20°, at the same lightness and chroma.
   - Yellows look lighter than other hues at equal chroma, so at this lightness that band renders muddy and cannot be made clean. Nudging only the two muddy pairs would have left the spacing uneven.
4. **Dark-mode status colours.** Success becomes `#5db384` (was `#4ade80`) and warning `#d4a24c` (was `#fbbf24`). Danger stays `#f87171`.
   - Green and amber are informational, so they are toned down. Red must still pull the eye, so the three are not flattened equally.
   - Saturation: success 0.69 → 0.36, warning 0.96 → 0.61, danger stays at 0.91.
   - Contrast on the dark surface: success 6.87:1, warning 7.55:1, danger 6.32:1.

CI fails if a raw colour value appears outside `web/app/globals.css`, so these values change in one place.

### Section 12.5: prompt `refund-advisor.v2`, and strict structured output for OpenAI (T-075)

**Spec:** section 12.5 gives the text of `refund-advisor.v1`.

**What v1 measured** (`gpt-4o-mini`, 40 cases):
- Schema validity 97.5%. One case named the field `reason` instead of `reasons` on both attempts.
- Action agreement 72.5%. The model leaned to reject: "needs more info" was almost never chosen, and a download was treated as grounds to reject even when the file was reported broken.
- Six ordinary requests were flagged as prompt injection.

**Changes:**
- **`refund-advisor.v2`** keeps rules 1 to 4 of v1 word for word, apart from asking for the property names exactly as written. It adds three things:
  - guidance for each `recommendedAction`: a download does not weaken a concrete defect claim, and "needs more info" beats reject for vague messages
  - definitions for each flag, with examples of injection. `policy_conflict` is only for approving an `IneligibleOutsideWindow` request, which matches the code-computed flag.
  - an example object
- **OpenAI requests now send the answer's JSON schema in strict mode**, built from the same contract lists as the validator. The validator still checks every answer, including lengths and ranges.
- **The eval now also reports false injection flags on ordinary requests.**
- `refund-advisor.v1` stays embedded for reference; the advisor uses v2.

**Result with v2:**

| Metric | v2 result | Target |
| --- | --- | --- |
| Schema validity | 100% | 100% |
| Action agreement | 95% | 80% |
| Injection recall | 6 of 6 | 5 of 6 |
| Category accuracy | 82.5% | reported only |
| False injection flags | 7 of 34 | reported only |

The false flags are ordinary requests containing a plea such as "please cancel" or "refund please". The flag is advisory and changes no state, so this is accepted for now. A larger model is the next lever if admins find it noisy.

### Section 19 Phase 7: T-085 to T-089 added for the UI redesign and product cover images

**Spec:** the v1 scope lists product images as a non-goal. T-083 is the only frontend task, and section 2 names no CSS framework (R-11).

**Addition (repo owner's decision, `docs/frontend-design-plan.md`):** five Phase 7 tasks are added to the backlog in section 19.

| Task | Scope |
| --- | --- |
| T-085 | Product cover images, backend: `cover_image_key` and `cover_image_updated_at` on `products`; a public-read blob container separate from the private one holding downloadable files; `PUT /products/{id}/cover` with a magic-byte check, a 5 MB limit and EXIF stripping; `coverImageUrl` on product responses |
| T-086 | Design foundation: tokens, Tailwind theme extension, Inter through `next/font`, and the component library |
| T-087 | Catalogue and product detail redesign |
| T-088 | Library, orders, checkout and auth screen redesign |
| T-089 | Seller dashboard and admin refund queue redesign |

**This reverses the product-images non-goal.** It also adds Tailwind CSS to the frontend stack, which the plan specifies. EXIF stripping adds no library: metadata blocks are removed from the JPEG, PNG and WebP bytes directly.

**Why:** the client works end to end but carries no visual weight, and a catalogue without product imagery cannot be browsed (see the plan's "What's wrong now").

### Section 19 backlog order: the frontend (T-083) moves ahead of the Azure, deploy and polish tasks

**Spec:** T-083 (thin Next.js frontend, an optional phase 7 stretch) comes after T-064, and its acceptance is "deployed alongside the API".

**Change (repo owner's decision):**
- T-083 is built now, against the local API, before T-061 to T-066 and before phase 7's deploy and polish tasks.
- Its scope is unchanged: register, login, browse, product detail, checkout, library, seller dashboard and admin refund queue.
- Deploying it alongside the API waits for the Azure work (T-061 to T-064).
- T-073 (admin refund review and decision) is built first, because the admin refund queue screen needs its endpoints.

**Why:** the owner wants a working client against the local stack before the cloud tasks, which need an Azure subscription and Stripe keys.

### Sections 12.3 and 16: OpenAI as a second LLM provider

**Spec:** v1 ships `AnthropicLlmClient` and `FakeLlmClient`, and `Ai:Provider` selects between them.

**Change (repo owner's request):** `OpenAiLlmClient` is added, selected with `Ai:Provider=OpenAi`. The repo owner has an OpenAI key rather than an Anthropic one. The AI refund feature can now run against either provider. `Ai:Provider` accepts `Fake` (the default), `Anthropic` or `OpenAi`.

How the OpenAI client works:
- It calls the Chat Completions API (`POST /v1/chat/completions`) over `HttpClient`.
- The prompt goes in a `system` message and the facts in a `user` message, with `max_completion_tokens` set from `Ai:MaxOutputTokens`.
- It asks for `response_format: json_object`. The answer still goes through the same strict section 12.4 parser, the one retry and the code-computed flags as the Anthropic client, so the format setting only makes a valid answer more likely and is not trusted on its own.
- It sends no `temperature`, because reasoning models accept only the default.
- `Ai:OpenAiReasoningEffort` is sent as `reasoning_effort` only when set, because models that do not reason reject it. `Ai:Effort` remains Anthropic-only.
- It uses the same section 10.2 LLM resilience profile (`Resilience:Llm`): timeouts, network errors, 429 and 5xx are retried once, other 4xx are not, and the circuit breaker is shared in behaviour.
- A refusal comes back as an empty answer, so it ends as `InvalidOutput`.
- The model name comes only from `Ai:Model`, and is never hard-coded.

Configuration:
- Both real providers need `Ai:Model` and `Ai:ApiKey`; startup validation refuses to start without them.
- `Ai:BaseUrl` optionally overrides either provider's address.
- For local development, the Api and the Worker share one user-secrets store (the same `UserSecretsId`), because the analysis runs in the Worker.

**Why:** a provider choice made through `ILlmClient` keeps D-36's boundary (`IRefundAdvisor` decides nothing, and a human approves every refund) and lets the project run with the key that is available.

### Sections 12.3 and 12.5: no temperature, and a higher output-token default for the LLM call (T-070)

**Spec:** temperature 0, and `Ai:MaxOutputTokens` default 500.

**Changes:**
- `AnthropicLlmClient` sends `anthropic-version: 2026-10-21` and no `temperature`. On that API version, a Messages request that sends `temperature`, `top_p` or `top_k` returns a 400 on every model, and current models accept only the default sampling anyway. Consistency comes from the strict validator and the single retry (section 12.4), not from sampling.
- `Ai:MaxOutputTokens` defaults to 2000. Current models think before answering, and thinking counts toward the limit, so 500 could cut the JSON off.
- `Ai:Effort` (default `low`) is sent as `output_config.effort` to keep the classification fast and cheap.
- The model still comes only from `Ai:Model`.

**Why:** the spec's values predate the current API. Both changes keep its intent: a short, deterministic-as-possible JSON answer.

### Section 15.2 Dockerfiles and section 15.7 Worker health (T-060)

**Spec:** the section 15.2 Dockerfile template creates its user with `addgroup` and `adduser`, and checks health with `curl`. The Worker serves health on port 8081 (section 15.7).

**Changes:**
- The `aspnet:10.0` image (Ubuntu 24.04) has neither `adduser` nor `curl`. The runtime stage creates the user with `groupadd` and `useradd`, and installs `curl` with `--no-install-recommends` for the `HEALTHCHECK`.
- The build stage also copies `.editorconfig`, which holds the analyzer settings (for example, generated migrations are exempt), and `docs/sql`, which Infrastructure embeds.
- The Dockerfiles live in `deploy/docker/`, where `docker-compose.override.yml` already points.
- The Worker now runs on `WebApplication.CreateSlimBuilder`. Kestrel listens only on `Worker:HealthPort` (default 8081) and maps only `/health/live` and `/health/ready`.
- The Worker registers only background services through the new `AddApplicationBackground()`. Request handlers need the Api's caller and token issuer, so the Worker leaves them out, and Development-mode service validation passes.

**Why:** the template as written does not build or pass its health check on the current base image.

### Section 13.2 R-10: EP-71 reads completed months from the view and later months live (T-054)

**Spec:** R-10 is `mv_seller_monthly_revenue`, refreshed every 15 minutes, and serves EP-71 when it beats R-01.

**Change:** R-10 is used, because it measured about 55 times faster than R-01 for the busiest seller in the 1M-order dataset (`docs/sql/R10-seller-monthly-revenue.md`). A new table, `report_refreshes (view_name, refreshed_at)`, records when each refresh started. EP-71 reads months that began before that time, less a 10-minute margin, from the view, and later months live from the ledger. With no refresh recorded, every month is read live, which is R-01.

**Why:** ledger entries are only ever written at the current time, so this split gives R-01's exact result. Serving straight from a view that can be 15 minutes stale would hide a seller's newest sales.

### Section 10.2: payment provider policies wrap the provider, not only its HttpClient (T-040)

**Spec:** resilience is implemented "with `Microsoft.Extensions.Http.Resilience` handlers per named `HttpClient`".

**Change:** the payment provider's timeout, retry and circuit-breaker policies are a Polly pipeline in `ResilientPaymentProvider`, which wraps whichever `IPaymentProvider` is configured. The values are the section 10.2 defaults, bound from `Resilience:PaymentsApi` (create, get) and `Resilience:PaymentsWorker` (cancel, refund). The Stripe SDK's own network retries are turned off, so calls are not retried twice.

**Why:** the fake provider makes no HTTP calls, so HttpClient handlers would leave FM-09 and FM-10 untestable as T-040 requires. The policies, values and retry conditions are unchanged. The LLM client (Phase 4) will use the same settings type.

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
