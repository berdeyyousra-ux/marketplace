# 0002 - Technology stack and banned libraries

**Status:** Accepted
**Date:** 2026-11-01
**Deciders:** berdeyyousra-ux

## Context

The project needs one agreed stack so implementation does not drift between tasks. Library licensing also matters: this is a public project with no budget for commercial licenses, and several common .NET libraries have become commercial. Section 2 of the build spec records the decisions and is binding (R-10). This ADR summarizes them; section 2 stays authoritative for the detail.

## Decision

| Area | Choice | Spec IDs |
| --- | --- | --- |
| Runtime | .NET 10 LTS, C# 14, SDK pinned in `global.json` with `latestPatch` | D-01 |
| API | ASP.NET Core controllers, RFC 9457 ProblemDetails, built-in OpenAPI with Scalar | D-03, D-19, D-20 |
| Application code | Hand-written handlers through DI, manual mapping, `Result<T>` for expected failures, FluentValidation 12 | D-05, D-06, D-07, D-18 |
| Data | PostgreSQL 17, EF Core 10 with Npgsql and snake_case naming, raw SQL for reports | D-08, D-09, D-10 |
| Data rules | UUID v7 keys, `timestamptz` in UTC, `numeric(12,2)` money in EUR, `xmin` concurrency | D-11 to D-15 |
| Auth | ASP.NET Core Identity, HS256 JWT, rotating refresh tokens stored as hashes | D-16, D-17 |
| Reliability | Idempotency keys, webhook inbox, transactional outbox, `Microsoft.Extensions.Http.Resilience` | D-21 to D-24 |
| Integrations | Stripe behind `IPaymentProvider`, Azure Blob behind `IFileStorage`, MailKit over SMTP | D-25 to D-27 |
| Search and paging | PostgreSQL full-text search, keyset pagination | D-28, D-29 |
| Logging | Serilog, JSON to console, correlation IDs | D-30 |
| Testing | xUnit v3, Shouldly, NSubstitute, Bogus, Testcontainers, Verify, `WebApplicationFactory` | D-31 |
| Build | Central package management, warnings as errors, enforced formatting | D-32 |
| Delivery | Multi-stage Dockerfiles, GHCR, Azure Container Apps, Bicep, GitHub OIDC, trunk-based squash merges | D-33, D-34, D-35 |
| AI | LLM behind `IRefundAdvisor`, JSON-only validated output, human approval for every refund | D-36, D-37 |
| Frontend | Next.js with the App Router, thin and API-driven (phase 7) | D-40 |

**Banned:** MediatR, AutoMapper, FluentAssertions, Moq, Swashbuckle, Newtonsoft.Json, any ORM other than EF Core, and any message broker in v1.

Only libraries named in the spec may be used (R-11). Adding one requires a proposal in `docs/proposals/` and human approval (R-04).

## Consequences

- Everything runs locally through Docker Compose, and tests use real PostgreSQL and Azurite containers. This needs more test setup but catches database behavior that mocks would miss.
- Hand-written handlers and mapping mean more code, in exchange for no hidden behavior and no license risk.
- Azure and Stripe are the only real provider integrations in v1. Both sit behind interfaces, so tests and local development can run without them.
- Library licenses can still change. Verify already requires a declared license option, covered for now by its time-limited open source exemption, which applies only while the project generates no revenue.
- Changing a decision here requires a new ADR that supersedes this one, plus human approval (R-10).
