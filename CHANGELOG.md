# Changelog

Notable changes to this project are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Deviations from the build specification, and the reasons
for them, are recorded separately in the [spec changelog](docs/spec-changelog.md).

## [Unreleased]

### Added

- Stripe's Payment Element in the web client's checkout, where Stripe is the configured provider (T-064). Card
  details go straight to Stripe and never reach the app's own servers, and the order is only ever confirmed by the
  provider's webhook, not by the browser.
- `publishableKey` on the EP-33 payment response, so the client gets the key for the provider the API actually uses
  (see the [spec changelog](docs/spec-changelog.md)).

### Changed

- `Payments:Stripe:PublishableKey` is now required at startup wherever Stripe is the provider, so a deployment that
  could not take a card payment fails before it goes live. In Azure it comes from the `STRIPE_PUBLISHABLE_KEY`
  GitHub environment variable.

## [1.0.0] - 2026-11-09

First release: a complete marketplace for downloadable digital products, from registration through payment,
download, refund and reporting, with a web client and a deployment pipeline.

### Added

**Accounts and sessions**
- Registration with email confirmation, password reset, change password and account lockout.
- JWT access tokens with rotating refresh tokens, reuse detection and whole-session revocation.
- Role policies for buyers, sellers and admins. The Admin role is never self-assignable: it is granted only by the
  bootstrap seed.

**Catalogue**
- Product aggregate with its invariants, draft and publish flow, and optimistic concurrency over ETags.
- Full-text search, category and price filters, sorting, and keyset pagination.
- Streamed file upload checked by magic bytes, and cover images stripped of metadata and served from a separate
  public-read container.

**Orders, payments and downloads**
- Idempotent order creation with price snapshots, an `Idempotency-Key` filter with replay and lock takeover, and
  order list, detail and cancel endpoints.
- A payment provider abstraction with a Stripe implementation and a fake provider for development, signed webhook
  verification through an inbox, and reconciliation against the provider.
- Order receipts by email, and downloads through five-minute signed URLs backed by a grant that stays usable until
  it expires or a refund revokes it.

**Refunds**
- Buyer refund requests with a computed policy verdict, admin review, decision and execution against the provider,
  with retries for failed executions.
- An LLM refund advisor with a strict output schema, prompt-injection handling and flag merging. It only ever
  advises; an admin decides. Anthropic, OpenAI and a deterministic fake client are selectable with `Ai:Provider`.
- An eval case set, a replay gate in CI and a live eval runner, reported in [docs/ai-eval-report.md](docs/ai-eval-report.md).

**Reporting**
- Report queries R-01 to R-09 as hand-written SQL, with seller dashboard and admin endpoints.
- Seller monthly revenue served from a materialized view, and covering indexes with their query plans recorded in
  [docs/sql](docs/sql) and [ADR 0003](docs/adr/0003-reporting-indexes.md).

**Platform**
- A transactional outbox written in the same transaction as every side effect, drained by the Worker.
- Worker jobs for order expiry, payment reconciliation, cleanup and the revenue view refresh, with Polly timeout,
  retry and circuit-breaker pipelines and dead-lettering.
- `ProblemDetails` for every error, request rate limiting, OpenAPI with a Scalar UI, structured logging with masked
  sensitive properties, and liveness and readiness endpoints.
- Fail-fast configuration validation: every configuration section is checked as the process starts, and an unsafe or
  missing value is logged at Fatal and stops the process instead of failing on a later request. Outside Development
  this refuses the published development signing key, a wildcard CORS origin, and the fake payment provider in
  Production.

**Web client**
- A Next.js client for buyers, sellers and admins: catalogue, product pages, checkout, library, orders, account
  screens, the seller dashboard with a revenue chart and listing editor, and the admin refund queue.
- Backend-for-frontend authentication that keeps tokens in httpOnly cookies and never exposes them to browser code,
  with single-flight session refresh.
- A design system of contrast-checked tokens, dark by default and light where the operating system asks for it,
  documented in [docs/frontend-design-plan.md](docs/frontend-design-plan.md).

**Build, test and deployment**
- Architecture tests for the layer rules, unit tests, integration tests against real PostgreSQL and Azurite through
  Testcontainers, and AI evals.
- Contract tests: an OpenAPI snapshot, a database schema contract, an authorization matrix over every endpoint and
  role, and the failure modes from the specification.
- A [Bruno collection](bruno/README.md) covering every endpoint end to end.
- CI with formatting, migration-bundle idempotency, Trivy image scanning, vulnerable-package checks, gitleaks and
  CodeQL; Dependabot for updates.
- Production Dockerfiles, Bicep infrastructure for Azure with GitHub OIDC sign-in, and a staging deployment pipeline
  that runs the migration job, deploys, smoke tests and rolls back on failure.

### Known limitations

Recorded in full in the [README](README.md#limitations). In short: no live deployment is running (staging was torn
down to stop the charges and production is deferred), the web client has no Stripe payment form, payouts are recorded
but not paid out, refunds are whole-item only, and observability is limited to structured logs and health checks.

[Unreleased]: https://github.com/berdeyyousra-ux/marketplace/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/berdeyyousra-ux/marketplace/releases/tag/v1.0.0
