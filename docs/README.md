# 📊 Portfolio Analytics Backend — Detailed Guide

High-performance backend for stock analytics with **smart ingestion**, **local-first search**, and **clear APIs**.  
This document is the single source of truth for setup, configuration, operations, and development.

This document is the single source of truth for setup, configuration, operations, and development.
For a quick overview and badges, see the root [README.md](./README.md).

---

## Table of Contents
- [Overview](#overview)
- [Architecture](#architecture)
- [Data Sources](#data-sources)
- [Rate Limits & Error Semantics](#rate-limits--error-semantics)
- [Configuration](#configuration)
- [Setup & Run](#setup--run)
- [Database & Migrations](#database--migrations)
- [Ingestion Workflows](#ingestion-workflows)
- [Search & Discovery](#search--discovery)
- [API Surface](#api-surface)
- [Docs & Tooling](#docs--tooling)
- [Testing](#testing)
- [Operations](#operations)
- [Troubleshooting](#troubleshooting)
- [Security](#security)
- [Docker & Deployment](#docker--deployment)
- [Conventions](#conventions)
- [Roadmap](#roadmap)

---

## Overview
- **Purpose:** Serve financial statements, analytics (PE, ROE/ROA, FCF Yield, OE, ratios), and time-series data to the frontend.
- **Storage:** SQLite via EF Core with lean indices and VACUUM/PRAGMA tuning.
- **Resilience:** Graceful handling of **429** (rate limit) and **402** (free-tier) with fallbacks/caching.
- **Observability:** Structured logs + admin endpoints for quick health checks and housekeeping.

---

## Architecture
```
Frontend (Vite/React)
        │
        ▼
  Portfolio.Api (.NET 8)
        │
        ├─ Controllers (REST)
        │    ├─ Companies / Quotes / Fundamentals / Analytics / Data / Admin
        │
        ├─ Services
        │    ├─ FmpClient / AlphaVantageClient / Ingest Services
        │    └─ MaintenanceService (vacuum, backups, prune)
        │
        └─ Data (EF Core + SQLite)
             ├─ DbContext, Entities, Migrations
             └─ Indices for fast filters
```
> Optional diagrams: `docs/architecture.png`, `docs/erd.png`

**Key decisions**
- Keep write paths simple (ingest → upsert); analytics are calculated on read.
- Prefer **local data**; only hit external APIs when necessary.
- Make errors **classifiable** so the frontend can show simple categories.

---

## Data Sources
- **Primary:** Financial Modeling Prep (FMP)
- **Secondary:** Alpha Vantage (optional, fallback)
- **Offline bundle:** 200+ popular tickers for discovery/search

> Configure API keys via **user-secrets** or env vars. See [Configuration](#configuration).

---

## Rate Limits & Error Semantics
The backend strives to return errors that the frontend can map into **simple categories**:

| Situation                           | HTTP / Text markers                        | Frontend category |
|------------------------------------|--------------------------------------------|-------------------|
| Success                            | `200`                                      | ✔️ OK             |
| Not found                          | `404`                                      | ❌ Not found      |
| Bad request                        | `400`                                      | ⚠️ Bad request    |
| Free tier exceeded / subscription  | `402`, `subscription`, `payment required`  | ⛔ Free-tier limit|
| Rate limit                         | `429`, `too many requests`, `rate limit`   | ⏳ Rate limit     |
| Server issue                       | `5xx` without 402/429 hints                | ⚠️ Server error   |

**Notes**
- When upstream wraps `402/429` inside `5xx`, the frontend still detects the inner cause.
- `Retry-After` is propagated where available (used for e.g., `Rate limit (10s)`).

---

## Configuration
### `appsettings.json` (excerpt)
```json
{
  "ConnectionStrings": { "Default": "Data Source=portfolio.db" },
  "Fmp": { "ApiKey": "YOUR_KEY_HERE", "BaseUrl": "https://financialmodelingprep.com/" },
  "AlphaVantage": { "ApiKey": "YOUR_KEY_HERE" },
  "DemoMode": false,
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.EntityFrameworkCore": "Warning" }
  }
}
```

### Secrets (dev)
```bash
dotnet user-secrets init
dotnet user-secrets set "Jwt:Secret" "YOUR_LOCAL_JWT_SECRET"
dotnet user-secrets set "Fmp:ApiKey" "YOUR_FMP_API_KEY"
dotnet user-secrets set "AlphaVantage:ApiKey" "YOUR_AV_API_KEY"   # optional
```

### Environment variables (prod)
```bash
export ConnectionStrings__Default="Data Source=/data/portfolio.db"
export Fmp__ApiKey="your-prod-key"
export AlphaVantage__ApiKey="your-prod-key"   # optional
export ASPNETCORE_URLS="http://0.0.0.0:5046"
export ASPNETCORE_ENVIRONMENT="Production"
```

---

## Setup & Run
```bash
# restore, migrate, run
dotnet restore
dotnet ef database update
dotnet run

# Swagger UI
# → http://localhost:5046/swagger
```

**Dev helpers**
```bash
# migrations
dotnet ef migrations add Init
dotnet ef database update

# reset db (be careful)
rm -f portfolio.db && dotnet ef database update
```

---

## Database & Migrations
- SQLite DB: `portfolio.db`
- Indices (examples):
```sql
CREATE UNIQUE INDEX IX_Tickers_Symbol ON Tickers(Symbol);
CREATE INDEX IX_IncomeStatements_Symbol ON IncomeStatements(Symbol);
CREATE INDEX IX_Prices_TickerId_Date ON Prices(TickerId, Date);
```
- Vacuum/reindex available via Admin endpoints. See [Operations](#operations).

---

## Ingestion Workflows
Endpoints (annual/quarter supported with `period` + `limit`):
```http
GET  /api/ingest/income/{symbol}?period=annual&limit=5
GET  /api/ingest/balance/{symbol}?period=annual&limit=5
GET  /api/ingest/cash/{symbol}?period=annual&limit=5
```
**Bulk/admin helpers**
```http
POST /api/admin/ingest/fmp-annual?symbol=AAPL&limit=5
```
**Behavior**
- Upserts based on `(symbol, period, date)`.
- Pacing (≈350 ms) when batching to avoid 429.
- Clear error signaling (see categories above).

---

## Search & Discovery
```http
GET /api/companies                     # list companies in DB
GET /api/companies/search?q=AAPL&limit=10
POST /api/companies/add                # { "symbol": "AAPL" }
POST /api/companies/add-popular        # { "category":"megacap","limit":10 }
DELETE /api/companies/{symbol}
POST /api/companies/{symbol}/refresh-profile
```

---

## API Surface
### Analytics
```http
GET /api/analytics/pe?symbol=AAPL
GET /api/analytics/pb?symbol=AAPL
GET /api/analytics/roe?symbol=AAPL
GET /api/analytics/roa?symbol=AAPL
GET /api/analytics/fcf-yield?symbol=AAPL
GET /api/analytics/owner-earnings?symbol=AAPL
GET /api/analytics/owner-earnings-yield?symbol=AAPL
GET /api/analytics/equity-cagr?symbol=AAPL
GET /api/analytics/asset-turnover?symbol=AAPL
GET /api/analytics/debt-to-equity?symbol=AAPL
GET /api/analytics/debt-to-assets?symbol=AAPL
GET /api/analytics/equity-ratio?symbol=AAPL
GET /api/analytics/eps?symbol=AAPL
GET /api/analytics/bvps?symbol=AAPL
GET /api/analytics/oeps?symbol=AAPL
GET /api/analytics/p-to-oe?symbol=AAPL
GET /api/analytics/fcf-margin?symbol=AAPL
```

### Time Series & TTM
```http
GET /api/quotes/latest?symbol=AAPL&take=1
GET /api/quotes/timeseries?symbol=AAPL&from=YYYY-MM-DD&to=YYYY-MM-DD
GET /api/data/ttm/{symbol}
GET /api/data/ttm/{symbol}/ratios
```

### Data Export
```http
GET /api/data/income/{symbol}?period=quarter&limit=10
GET /api/data/balance/{symbol}?period=quarter&limit=10
GET /api/data/cash/{symbol}?period=quarter&limit=10
```

### Admin & Maintenance
```http
GET  /api/admin/info
POST /api/admin/vacuum
POST /api/admin/backup
POST /api/admin/prune
```

---

## Docs & Tooling
- **OpenAPI / Swagger UI:** http://localhost:5046/swagger
- **Analytics endpoints (overview):** `docs/analytics-endpoints.md`
- **Postman collection:** `docs/portfolio_analytics_postman_collection.json`
- **Architecture diagram:** `docs/architecture.png` (optional)
- **ERD:** `docs/erd.png` (optional)

---

## Testing
```bash
dotnet test
# Coverage:
dotnet test /p:CollectCoverage=true
# Focused:
dotnet test --filter Category=Integration
```

---

## Operations
- **Backups:** `POST /api/admin/backup`
- **Vacuum:** `POST /api/admin/vacuum`
- **Prune old data:** `POST /api/admin/prune`
- **Health & stats:** `GET /api/admin/info`

**Tips**
- Run `VACUUM` after large batch ingests.
- Keep `portfolio.db` out of version control; back it up regularly in prod.

---

## Troubleshooting
- **404 No data:** Ensure company exists (`POST /api/companies/add`), then run ingestion.
- **429 Rate limit:** Reduce pace or retry later; prefer bulk admin endpoints.
- **402 Free tier:** Requires paid plan for the upstream provider.
- **DB locked:** Check long-running processes; try `vacuum` and ensure single-writer pattern.

---

## Security
- API keys via user-secrets or env vars (never commit)
- Validate inputs on all endpoints
- EF Core parameterization prevents SQL injection
- Restrict CORS to frontend origin
- Consider simple rate limiting for public endpoints

---

## Docker & Deployment
**Dockerfile example**
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore && dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:5179
ENTRYPOINT ["dotnet", "Portfolio.Api.dll"]
```
**Environment variables**
```bash
export ConnectionStrings__Default="Data Source=/data/portfolio.db"
export Fmp__ApiKey="your-production-key"
export AlphaVantage__ApiKey="your-production-key"
export ASPNETCORE_ENVIRONMENT="Production"
```

---

## Conventions
- **Commit messages:** Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, …)
- **Branching:** feature branches → PR
- **Docs:** Keep Swagger and `docs/*.md` up to date with changes

---

## Roadmap
- Optional provider abstractions (plug additional sources)
- Background jobs for scheduled ingestion
- More analytics (quality of earnings, accruals, Piotroski F-score)
- Export endpoints (CSV/Parquet) for more resources
