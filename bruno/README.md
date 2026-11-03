# Bruno collection (T-081)

The collection covers all 47 endpoints in the API's OpenAPI document. It also covers the health probes, the OpenAPI document itself, and the development-only fake payment completion (EP-99).

The requests are ordered to run end to end against the local stack:

1. Health checks.
2. Auth, signed in as the seeded buyer.
3. The catalogue.
4. A seller creates a new listing, uploads its file and cover, and publishes it.
5. The buyer orders the listing, pays for it, downloads it and asks for a refund.
6. An admin reviews and approves the refund, and reads the reports.
7. Cleanup: the listing is unpublished and removed, and the buyer signs out.

Every request has tests, and docs that name its endpoint (EP-xx).

## Run it in the Bruno app

1. Open the `bruno` folder as a collection.
2. Select the **Local** environment.
3. Run the collection, or run single requests.

Requests that need a token read it from the runtime variables set by the "Log in as …" requests, so run those first.

## Run it from the command line

```sh
cd bruno
npx @usebruno/cli@4.2.1 run --env Local -r
```

## What it expects

- **The local stack is running,** as described in the [main README](../README.md#run-it-locally):
  - the API on http://localhost:8080 in Development
  - the Worker
  - the demo seed
- **The fake payment provider** (`Payments:Provider` = `Fake`, the default). The "Complete fake payment" request uses EP-99, which answers 404 with any other provider.
- **The Worker processes the payment webhook.** "Wait for the order to be paid" polls for up to 30 seconds.
- **Wait a minute between full runs.** Register, login, forgot-password and reset-password share a limit of 10 requests per minute per IP. A run uses six, so a second run within the minute gets 429.

## Side effects

Each run leaves a small trail in the local database:
- one new account that never confirms its email
- one listing, unpublished and removed at the end
- one paid and refunded order
- one cancelled order

It never changes a seeded account's password, and it never deletes seeded data.

The environment file holds only the seeded local accounts and their shared password (`seed password 2026`, see [docs/seeding.md](../docs/seeding.md)). These are not real secrets.
