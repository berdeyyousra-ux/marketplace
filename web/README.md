# Marketplace web client (T-083)

A thin Next.js (App Router, TypeScript) client for the Marketplace API. It has these pages:

- create an account, confirm the email, sign in, and reset a forgotten password
- browse and search the catalogue, and view a product
- buy a product and pay for it (checkout)
- the order list
- the library, with downloads and refund requests
- the seller dashboard
- the admin refund queue

## How it talks to the API

The browser never calls the API and never sees a token.

- **Sign-in:** this app's server makes the call and stores the access and refresh tokens in httpOnly cookies (section 11.2).
- **Pages:** they render on the server with the access token.
- **Browser actions:** they go to `/api/proxy/...`, which adds the token and forwards the call to the API's `/api/v1/...`.
- **Expired tokens:** only the browser renews a session, through `lib/refresh.ts`. The API has no grace period for a reused refresh token, so two refreshes sent with the same token would sign the user out everywhere.
  - Refreshes are single-flight: one shared promise within a tab, and a Web Lock across tabs, so each refresh sends the newest token.
  - The API proxy passes a 401 back. `call()` refreshes and retries the request once.
  - A signed-in page without a usable token goes to `/session/refresh`, which renews the session and returns.
  - A public page shows a neutral header and restores the session in the background, so a signed-in user never appears signed out.
  - If the refresh fails, the cookies are cleared and the user is signed out.

Because every API call comes from the server, the API needs no CORS entry for the client.

## Run it locally

1. Start the stack's dependencies with `docker compose up -d` from the repository root.
2. Apply the migrations, then load demo data (see [docs/seeding.md](../docs/seeding.md)):
   ```sh
   dotnet run --project src/Marketplace.Tools -- seed-demo
   ```
   Seeded accounts use the password `seed password 2026`:
   - `buyer1@seed.test`
   - `seller1@seed.test`
   - `admin@seed.test`
3. Run the API and the Worker. Both start in Development through their launch profiles:
   ```sh
   dotnet run --project src/Marketplace.Api
   dotnet run --project src/Marketplace.Worker
   ```
   The Worker processes payments, emails, AI analysis and refunds, so orders stay Pending without it.
4. Run the client:
   ```sh
   cd web
   cp .env.example .env.local   # API_BASE_URL=http://localhost:8080
   npm install
   npm run dev                  # http://localhost:3000
   ```

## Development details

- **Payments.** With the fake payment provider, checkout offers "Complete test payment". It calls the API's development-only EP-99, which sends the signed webhook a real provider would. EP-99 answers 404 unless the API runs in Development with the fake provider. Stripe's payment form comes with T-064.
- **Email.** Confirmation emails go to Mailpit at http://localhost:8025.
- **AI suggestions.** These come from whichever `Ai:Provider` the Worker is configured with. That can be `Fake`, `Anthropic` or `OpenAi`; see `docs/spec-changelog.md`.

`npm run build` type-checks and builds, and CI runs it.
