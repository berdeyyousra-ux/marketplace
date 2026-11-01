# 0001 - Modular monolith

**Status:** Accepted
**Date:** 2026-11-01
**Deciders:** berdeyyousra-ux

## Context

The marketplace needs production-shaped behavior: background work, reliable side effects, payment webhooks and clear module boundaries. It is built by one developer with an AI agent, on a small cloud budget. Microservices would add network failure modes, distributed transactions and more deployment work without a matching benefit at this size.

## Decision

Build a modular monolith from one codebase, deployed as two processes (D-02):

- `Marketplace.Api` handles HTTP, authentication, validation and database transactions. It writes outbox rows and only verifies and stores webhooks.
- `Marketplace.Worker` runs outbox processing, order expiry, reconciliation, AI analysis and cleanup.

The code is layered into Domain, Application, Infrastructure, Api and Worker (D-04). Inside Application, the modules are Identity, Catalog, Orders, Payments, Fulfillment, Refunds, Ledger, Reporting and Ops. A module may use another only through an interface in the callee's `Abstractions/` folder, following the dependency matrix in section 4.4 of the build spec.

Side effects go through a transactional outbox polled by the Worker (D-23), so no message broker is needed in v1. Architecture tests enforce the layer and module rules (section 4.3).

## Consequences

- One PostgreSQL database gives ordinary ACID transactions across modules, which keeps orders, payments and the ledger consistent.
- Deployment is two container apps from one repository, with one set of migrations.
- Slow or failing dependencies do not hold up user requests, except payment creation, which is the one synchronous provider call.
- Boundaries rely on architecture tests rather than the network, so those tests must stay in CI.
- The Api and Worker scale separately, but they share one database and its limits. The Worker is capped at 2 replicas.
- Extracting a service later is possible through the module interfaces and outbox, but it would still be real migration work.

Rejected: microservices (cost and operational overhead) and a single-project application (business rules would be harder to test in isolation).
