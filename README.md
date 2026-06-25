# ReadModelDemo

Demo project to showcase a Cosmos DB read-model strategy for multi-tenant directory-like data.

## Purpose

This repository demonstrates how to:

- Model a tenant-partitioned dataset in Cosmos DB with three containers:
  - identity
  - relationship
  - orgUnit
- Expose a single flattened read-model endpoint for consumers.
- Compare two read strategies under benchmark (latency + RU).
- Provide a simple web UI that fetches once and applies local filters client-side.

## Architecture

Projects:

- `ReadModelDemo.AppHost`: .NET Aspire host (orchestration + Cosmos emulator preview + Data Explorer)
- `ReadModelDemo.ApiService`: API with read-model and benchmark endpoints
- `ReadModelDemo.Web`: Blazor UI for benchmark and read-model visualization
- `ReadModelDemo.ServiceDefaults`: shared Aspire defaults (health, telemetry, resilience)

## Data Model

Partition key on all containers: `/tenantId`.

- `identity`: `id`, `tenantId`, `displayName`
- `relationship`: `id`, `tenantId`, `displayName`, `identityId`, `orgUnitId`
- `orgUnit`: `id`, `tenantId`, `displayName`, `managerId`

`relationship` links identities and orgUnits (N:N). `managerId` references an identity.

## Endpoints

API service exposes:

- `GET /readmodel`
  - Main consumer endpoint.
  - Params: `tenantId`, `page`, `pageSize`, `strategy`
  - `strategy`: `fanout` or `two-phase`
- `GET /benchmark`
  - Runs both strategies and returns min/avg/p95 for latency and RU.

## Read Strategies

1. `fanout`
- Reads all 3 containers for a tenant in parallel.
- Joins in memory.
- Produces linked and orphan rows.

2. `two-phase`
- Reads paged relationships first.
- Batch-resolves referenced identities and orgUnits.
- Optimized for page-focused retrieval.

## RU Calculation

RU is not estimated by formula in this sample; it is measured directly from Cosmos SDK responses.

- For each query page, Cosmos returns `RequestCharge`.
- The service accumulates `RequestCharge` across all pages and operations involved in a request.

Per strategy:

1. `fanout`
- Total RU = RU(identity query) + RU(relationship query) + RU(orgUnit query).

2. `two-phase`
- Total RU = RU(relationship count + page query)
  + RU(identity batch query)
  + RU(orgUnit batch query)
  + RU(manager identity batch query when needed).

In `/benchmark`, each strategy runs for N iterations (`iterations` parameter), and the API returns:

- `MinRequestUnits`
- `AvgRequestUnits`
- `P95RequestUnits`

These values are computed from the measured RU totals of each iteration, so benchmark output reflects real RU consumption for the selected input (`tenantId`, `page`, `pageSize`).

## Seed Behavior

On development startup, seed creates deterministic data for tenant `tenant-demo`:

- 5,000 identities
- 5,000 relationships
- 5,000 orgUnits

Seed is idempotent by count threshold check + stable IDs + upsert.

## Web UI

- `/benchmark`: compares both strategies
- `/readmodel`: runs read-model query and applies local filters on response

## Running Locally

Prerequisites:

- .NET SDK 10
- Docker Desktop running (required by Cosmos emulator preview)

Commands:

```bash
dotnet build ReadModelDemo.AppHost/ReadModelDemo.AppHost.csproj
dotnet run --project ReadModelDemo.AppHost/ReadModelDemo.AppHost.csproj
```

Open Aspire dashboard from console output.

## Demo Flow (suggested)

1. Start AppHost and open dashboard.
2. Show Cosmos Data Explorer in emulator preview.
3. Call `/readmodel` for `tenant-demo`.
4. Open benchmark page and compare `fanout` vs `two-phase`.
5. Explain RU and latency trade-offs based on returned metrics.

## Notes

- `WithOpenApi` currently emits deprecation warnings in this SDK version.
