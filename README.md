# swiftbets-wallet

The SwiftBets wallet: the double-entry ledger, punter balances, reservations for bets, and the daily reconciliation.

| Part | What it does |
|---|---|
| `SwiftBets.Wallet.Api` | gRPC for reserve, commit, release, credit and debit; HTTP for balances and reconciliation runs |
| `SwiftBets.Wallet.Application` | Posting rules: every posting balances to zero, punters never go negative, idempotency keys make retries safe |
| `SwiftBets.Wallet.Infrastructure` | Dapper over SQL Server (`SbWallet`), outbox to Kafka, the reconciliation worker and its metrics |
| `SwiftBets.Wallet.Migrator` | DbUp migrations with rollbacks; `Migrator:SeedDemo=true` funds the demo punters |

## Build and test

```bash
scripts/fetch-shared-packages.sh .   # the shared packages, from public releases
dotnet test
```

Integration tests start SQL Server and Redpanda in Docker (Testcontainers).

## Part of SwiftBets

SwiftBets is a sportsbook and casino platform built as independent .NET services. It runs live at [swiftbets.remonenaidoo.com](https://swiftbets.remonenaidoo.com). These repositories show a selection of it: [Settlement](https://github.com/remonenaidoo/swiftbets-settlement-showcase) · [Originals](https://github.com/remonenaidoo/swiftbets-originals-showcase) · [Steward](https://github.com/remonenaidoo/swiftbets-steward-showcase) · [Building blocks](https://github.com/remonenaidoo/swiftbets-building-blocks-showcase) · [Contracts](https://github.com/remonenaidoo/swiftbets-contracts-showcase).
