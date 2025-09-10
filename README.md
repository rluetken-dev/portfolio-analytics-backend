# Portfolio Analytics Backend (.NET 8 + EF Core + SQLite)

![CI](https://github.com/rluetken-dev/portfolio-analytics-backend/actions/workflows/ci.yml/badge.svg?branch=main)
![.NET](https://img.shields.io/badge/.NET-8.0-blue)
![License](https://img.shields.io/badge/License-MIT-green)

Backend service for fetching, ingesting, and analyzing stock **fundamentals**.  
**Now using Financial Modeling Prep (FMP) `/stable`** endpoints for Income Statement, Balance Sheet, and Cash Flow data.  
Computed **TTM (Trailing Twelve Months)** metrics are derived **from your stored quarterly data** — no external call needed once ingested.

---

## ✨ Highlights (2025-09-10)
- ✅ Migrated fundamentals to **FMP `/stable`**
- ✅ Added **ingest services** (Income, Balance, Cash) + EF entities & migrations
- ✅ Added **read endpoints** (SQL → JSON) for each statement
- ✅ Added **TTM** endpoints (sums & ratios) computed from stored quarterlies
- ✅ Added **snapshot** route (Income/Balance/Cash + TTM metrics) with `period` passthrough
- ✅ Added **plan-safe caps** on quarterly requests to respect FMP plan limits
- ✅ Swagger UI for quick exploration

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download)
- FMP API key ([financialmodelingprep.com](https://financialmodelingprep.com))
- *(Optional)* Alpha Vantage API key (used only for a legacy revenue fallback path)

### Setup & Run
```bash
# Clone
git clone https://github.com/rluetken-dev/portfolio-analytics-backend.git
cd portfolio-analytics-backend/Portfolio.Api

# Restore
dotnet restore

# Configure secrets
dotnet user-secrets init
dotnet user-secrets set "Fmp:ApiKey" "YOUR_FMP_API_KEY"
# Optional (only needed for legacy revenue fallback):
dotnet user-secrets set "AlphaVantage:ApiKey" "YOUR_ALPHA_VANTAGE_API_KEY"

# Apply database migrations (creates/updates SQLite DB)
# (Install dotnet-ef once: dotnet tool install --global dotnet-ef)
dotnet ef database update --project Portfolio.Api

# Run the API
dotnet run
```
> Swagger: `http://localhost:5046/swagger` (port may differ)

---

## 🔐 Configuration

`appsettings.json` keys (can be overridden by user-secrets / env vars):

```json
{
  "Fmp": {
    "ApiKey": "override-with-user-secrets"
  },
  "ConnectionStrings": {
    "Default": "Data Source=portfolio.db"
  }
}
```

Notes:
- **Fmp:ApiKey** – required, used by the typed `HttpClient`.
- Throttling/caps for quarterly endpoints are enforced in ingest services to avoid `402 Payment Required` on basic plans (limit ≤ 5 per call).

---

## 📚 API Overview (current)

### Ingest (writes to SQL)
Ingest specific statements from FMP `/stable` and upsert into SQLite.

```http
GET /api/ingest/income/{symbol}?period=annual|quarter&limit=10
GET /api/ingest/balance/{symbol}?period=annual|quarter&limit=5
GET /api/ingest/cash/{symbol}?period=annual|quarter&limit=5
```
- `period=quarter` requests are **plan-safe capped** at `limit ≤ 5` to avoid 402s.
- Upsert key: `(Symbol, Date, Frequency)`; newest first is recommended for reads.

### Read (from SQL; no external calls)
Return persisted rows for debugging, verification, or UI.

```http
GET /api/data/income/{symbol}?period=annual|quarter&limit=10
GET /api/data/balance/{symbol}?period=annual|quarter&limit=10
GET /api/data/cash/{symbol}?period=annual|quarter&limit=10
``

### TTM (from SQL; requires 4 stored quarters)
Compute TTM sums and ratios using the last 4 quarterly rows from your DB.

```http
GET /api/data/ttm/{symbol}
GET /api/data/ttm/{symbol}/ratios
```
- Sums: `RevenueTtm`, `NetIncomeTtm`, `FreeCashFlowTtm`
- Ratios: `NetMarginTtm`, `FcfMarginTtm` (computed as sum ÷ RevenueTtm)

### Live Fundamentals (direct FMP `/stable` calls; read-only)
Useful when you don’t need persistence or before ingesting.

```http
GET /api/fundamentals/{symbol}/income-statement/stable?period=annual|quarter&limit=5
GET /api/fundamentals/{symbol}/balance-sheet/stable?period=annual|quarter&limit=3
GET /api/fundamentals/{symbol}/cash-flow/stable?period=annual|quarter&limit=3
GET /api/fundamentals/{symbol}/metrics/ttm
GET /api/fundamentals/{symbol}/snapshot/stable?period=annual|quarter&limit=3
```
- `snapshot` returns `{ Income, Balance, Cash, Metrics }`, each fetched independently.
- `metrics/ttm` is period-agnostic (TTM by definition).

### Legacy Revenue (helper)
Simple aggregate returning (by default) quarterly revenue via FMP, falling back to annual and finally Alpha Vantage if needed.

```http
GET /api/fundamentals/revenue?symbol=AAPL&limit=8
```
> For `period=quarter` on basic FMP plans, use `limit ≤ 5` to avoid 402s.

> **Other controllers present:** `AdminController` (ops/maintenance), `QuotesController` (quotes/price-related). These are outside the fundamentals scope and may vary.

---

## 🧱 Data Model (EF Core)
**Entities (SQLite tables):**
- `income_statements` → `IncomeStatementEntity`  
  Fields: `Id`, `Symbol`, `Date`, `Frequency` (`annual|quarter`), `ReportedCurrency`, `Revenue`, `NetIncome`, `Eps`, `EpsDiluted`, `WeightedAverageShsOut`, `WeightedAverageShsOutDil`
- `balance_sheets` → `BalanceSheetEntity`  
  Fields: `Id`, `Symbol`, `Date`, `Frequency`, `ReportedCurrency`, `TotalAssets`, `TotalLiabilities`, `TotalStockholdersEquity`, `CashAndCashEquivalents`
- `cash_flows` → `CashFlowEntity`  
  Fields: `Id`, `Symbol`, `Date`, `Frequency`, `ReportedCurrency`, `OperatingCashFlow`, `CapitalExpenditure`, `FreeCashFlow`, `NetIncome`, `DepreciationAndAmortization`

**Constraints & Conventions:**
- Unique index on `(Symbol, Date, Frequency)` for each table (prevents duplicates)
- `DateOnly` converted to UTC `DateTime` for SQLite storage
- Monetary values stored as integers (raw values from the API), decimals used where appropriate on price tables

**Migrations:** included in-repo (apply via `dotnet ef database update`).

---

## 🧭 Design Notes
- **Separation of concerns:** ingest (write) vs. read (SQL) vs. live (FMP) endpoints
- **Plan-aware limits:** quarterly calls are capped to avoid FMP 402s
- **Lightweight DTOs:** we map only the fields we actually use (faster & safer)
- **Defensive parsing & logging:** invalid rows are skipped; upstream issues don’t crash reads
- **Period passthrough:** `period=annual|quarter` is consistently honored in live & snapshot routes

---

## 📂 Project Structure (current)
This reflects the tree in the repository (controllers + data + services + program):

```
Portfolio.Api/
├─ Controllers/
│  ├─ AdminController.cs
│  ├─ DataController.cs            # read from SQL (income/balance/cash + TTM)
│  ├─ FundamentalsController.cs    # live FMP /stable + revenue helper + snapshot
│  ├─ IngestController.cs          # ingest income/balance/cash into SQL
│  └─ QuotesController.cs
├─ Data/
│  ├─ Entities/
│  │  ├─ BalanceSheetEntity.cs
│  │  ├─ CashFlowEntity.cs
│  │  └─ IncomeStatementEntity.cs
│  ├─ AppDbContext.cs
│  └─ Migrations/                  # EF Core migrations (checked in)
├─ Models/
│  ├─ Ticker.cs
│  └─ Price.cs
├─ Services/
│  ├─ AlphaVantageClient.cs        # optional fallback (legacy revenue only)
│  ├─ BalanceSheetIngestService.cs
│  ├─ CashFlowIngestService.cs
│  ├─ FmpClient.cs                 # thin /stable client; reads Fmp:ApiKey
│  ├─ IncomeIngestService.cs
│  └─ MaintenanceService.cs
├─ Program.cs
├─ appsettings.json
├─ appsettings.Development.json
└─ README.md
```

> Note: You may also see `obj/` and `Properties/` directories generated by the SDK.

---

## 🧪 Testing
```bash
dotnet test
```
Planned unit tests for: mappings, TTM computation, and controller contracts.

---

## 🗺️ Roadmap
- [ ] Additional derived ratios (ROIC, leverage, cash conversion)
- [ ] Portfolio rollups (positions, weights, P&L)
- [ ] Dockerfile + compose (app + DB)
- [ ] Auth for write routes (API key / token)
- [ ] Caching headers & ETags on read endpoints

---

## 📜 License
MIT
