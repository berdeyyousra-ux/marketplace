# Seeding (T-050)

`Marketplace.Tools` fills an empty, migrated database. Both commands refuse to run if the database already has orders or seeded users. Everything is written in one transaction, so a failed run leaves nothing behind.

Seeded users sign in with the password `seed password 2026`. Emails end in `@seed.test`: `admin@seed.test`, `seller1@seed.test`, `buyer1@seed.test`, and so on.

## Demo data

A small, browsable catalogue (6 sellers, 60 products, 30 buyers, 400 orders). The product files are uploaded to blob storage, so downloads work.

```sh
export ConnectionStrings__Database="Host=localhost;Port=5432;Database=marketplace;Username=marketplace;Password=..."
export ConnectionStrings__BlobStorage="UseDevelopmentStorage=true"
dotnet run --project src/Marketplace.Tools -- seed-demo
```

## Load data (section 13.4)

```sh
dotnet run -c Release --project src/Marketplace.Tools -- seed-load \
  --orders 1000000 --sellers 500 --buyers 20000 --products 10000 --seed 42
```

The data is deterministic for a given `--seed` and `--now`. `--now` defaults to the current time, and the two years of history end at it. The seed writes no blobs.

Shape of the data:

- Order volume grows over the two years.
- Popularity is skewed: a few products and buyers account for many orders.
- About 86 percent of orders are paid, 8 percent expired and 6 percent cancelled.
- About 2.5 percent of paid items are refunded, and 0.5 percent have a rejected refund request.
- About one order in eight has a declined payment attempt.

Reference run on an M-series laptop against PostgreSQL 17 in Docker: 1,000,000 orders, 1,500,246 items, 952,323 payments, 1,290,432 download grants, 2,644,132 ledger entries and 37,781 refund requests, in 55 seconds with a peak of 630 MB of memory.

The ledger has about 2.6 million entries rather than the "about 3 million" in section 13.4, because only paid orders create ledger entries.
