# Portfolio Analytics Backend (.NET 8 + EF Core + SQLite)

![CI](https://github.com/rluetken-dev/portfolio-analytics-backend/actions/workflows/ci.yml/badge.svg?branch=main)
![.NET](https://img.shields.io/badge/.NET-8.0-blue)
![License](https://img.shields.io/badge/License-MIT-green)

Backend service for fetching, ingesting, and analyzing stock **fundamentals** and computing **Buffett-style analytics**.  
Data is persisted in a local SQLite database (`portfolio.db`) and enriched via **Financial Modeling Prep (FMP) `/stable`** endpoints.

---

## ✨ Highlights (2025-09-11)
- ✅ Migrated fundamentals to **FMP `/stable`**
- ✅ Added **ingest services** (Income, Balance, Cash) + EF entities & migrations
- ✅ Added **read endpoints** (SQL → JSON) for each statement
- ✅ Added **TTM** endpoints (sums & ratios) computed from stored quarterlies
- ✅ Added **Buffett metrics** (ROE, ROA, P/E, P/B, FCF Yield, Owner Earnings, Equity CAGR, etc.)
- ✅ Added **admin ops** (vacuum, prune, truncate) + **demo seeding** (guarded by `DemoMode`)
- ✅ Swagger UI for quick exploration

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download)
- FMP API key ([financialmodelingprep.com](https://financialmodelingprep.com))
- *(Optional)* Alpha Vantage API key (legacy fallback for revenue)

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

# Optional (legacy revenue fallback):
dotnet user-secrets set "AlphaVantage:ApiKey" "YOUR_ALPHA_VANTAGE_API_KEY"

# Apply database migrations
dotnet tool install --global dotnet-ef   # once
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
  "ConnectionStrings": { "Default": "Data Source=portfolio.db" },
  "Fmp": { "ApiKey": "override-with-user-secrets" },
  "DemoMode": true
}
```

- **Fmp:ApiKey** – required for ingest/live endpoints.  
- **DemoMode** – enables `/api/admin/seed/*` and destructive ops (should be `false` in production).  
- Free-tier FMP plans require `limit ≤ 5` on many calls; ingestion services enforce this.

---

## 📚 API Overview

### Ingest (writes to SQL)
Ingest data from FMP `/stable` and upsert into SQLite.

```http
POST /api/admin/ingest/fmp-annual?symbol=AAPL&limit=5
POST /api/admin/ingest/fmp-cashflow-annual?symbol=AAPL&limit=5
GET  /api/ingest/income/{symbol}?period=annual|quarter&limit=5
GET  /api/ingest/balance/{symbol}?period=annual|quarter&limit=5
GET  /api/ingest/cash/{symbol}?period=annual|quarter&limit=5
```

### Read (from SQL)
```http
GET /api/data/income/{symbol}?period=annual|quarter&limit=10
GET /api/data/balance/{symbol}?period=annual|quarter&limit=10
GET /api/data/cash/{symbol}?period=annual|quarter&limit=10
```

### TTM (Trailing Twelve Months)
```http
GET /api/data/ttm/{symbol}
GET /api/data/ttm/{symbol}/ratios
```
- Sums: `RevenueTtm`, `NetIncomeTtm`, `FreeCashFlowTtm`  
- Ratios: `NetMarginTtm`, `FcfMarginTtm`

### Live Fundamentals (direct FMP calls)
```http
GET /api/fundamentals/{symbol}/income-statement/stable?period=annual|quarter&limit=5
GET /api/fundamentals/{symbol}/balance-sheet/stable?period=annual|quarter&limit=3
GET /api/fundamentals/{symbol}/cash-flow/stable?period=annual|quarter&limit=3
GET /api/fundamentals/{symbol}/snapshot/stable?period=annual|quarter&limit=3
GET /api/fundamentals/{symbol}/metrics/ttm
```

### Analytics (Buffett metrics)
```http
GET /api/analytics/roe?symbol=AAPL
GET /api/analytics/roa?symbol=AAPL
GET /api/analytics/equity-ratio?symbol=AAPL
GET /api/analytics/debt-to-equity?symbol=AAPL
GET /api/analytics/debt-to-assets?symbol=AAPL
GET /api/analytics/net-margin?symbol=AAPL
GET /api/analytics/pe?symbol=AAPL
GET /api/analytics/pb?symbol=AAPL
GET /api/analytics/oeps?symbol=AAPL
GET /api/analytics/p-to-oe?symbol=AAPL
GET /api/analytics/fcf?symbol=AAPL
GET /api/analytics/fcf-yield?symbol=AAPL
GET /api/analytics/fcf-margin?symbol=AAPL
GET /api/analytics/owner-earnings?symbol=AAPL
GET /api/analytics/owner-earnings-yield?symbol=AAPL
GET /api/analytics/equity-cagr?symbol=AAPL
```

### Admin & Maintenance
```http
GET  /api/admin/info
POST /api/admin/vacuum
POST /api/admin/prune?maxAgeDays=1095&keepPerSymbol=1000
POST /api/admin/truncate?scope=prices|fundamentals|tickers|all
```

### Demo Seeds (only with `DemoMode=true`)
```http
POST /api/admin/seed/ticker?symbol=AAPL&name=Apple%20Inc
POST /api/admin/seed/annual?symbol=AAPL&year=2024&netIncome=100000000&equity=600000000
POST /api/admin/seed/revenue?symbol=AAPL&year=2024&revenue=6000000000
POST /api/admin/seed/liabilities?symbol=AAPL&year=2024&totalLiabilities=300000000
POST /api/admin/seed/assets?symbol=AAPL&year=2024&totalAssets=1500000000
POST /api/admin/seed/shares?symbol=AAPL&year=2024&shares=5000000000
POST /api/admin/seed/price?symbol=AAPL&date=2024-12-31&close=200
```

---

## 🧱 Data Model (EF Core)

**Entities**
- `income_statements` → `IncomeStatementEntity`
- `balance_sheets` → `BalanceSheetEntity`
- `cash_flows` → `CashFlowEntity` (includes `ChangeInWorkingCapital`)
- `prices` → `Price`
- `tickers` → `Ticker`

**Constraints**
- Unique `(Symbol, Date, Frequency)` per fundamentals table
- `DateOnly` stored as UTC `DateTime` in SQLite
- Raw ints for API monetary values; decimals for prices

---

## 📂 Project Structure

```
Portfolio.Api/
├─ Controllers/
│  ├─ AdminController.cs
│  ├─ AnalyticsController.cs
│  ├─ DataController.cs
│  ├─ FundamentalsController.cs
│  ├─ IngestController.cs
│  └─ QuotesController.cs
│
├─ Data/
│  ├─ Entities/
│  │  ├─ BalanceSheetEntity.cs
│  │  ├─ CashFlowEntity.cs
│  │  └─ IncomeStatementEntity.cs
│  ├─ AppDbContext.cs
│  └─ Migrations/
│
├─ Models/
│  ├─ Price.cs
│  ├─ RefreshResponse.cs
│  ├─ Ticker.cs
│  └─ TimeseriesPoint.cs
│
├─ Services/
│  ├─ AlphaVantageClient.cs
│  ├─ BalanceSheetIngestService.cs
│  ├─ CashFlowIngestService.cs
│  ├─ FmpClient.cs
│  ├─ IncomeIngestService.cs
│  ├─ ISeedServices.cs
│  ├─ MaintenanceService.cs
│  └─ SeedService.cs
│
├─ Program.cs
├─ appsettings.json
└─ README.md
```

---

## 🧪 Quick Test

```powershell
# Ingest real annuals
Invoke-RestMethod -Method Post "http://localhost:5046/api/admin/ingest/fmp-annual?symbol=AAPL&limit=5"
Invoke-RestMethod -Method Post "http://localhost:5046/api/admin/ingest/fmp-cashflow-annual?symbol=AAPL&limit=5"

# Seed price
Invoke-RestMethod -Method Post "http://localhost:5046/api/admin/seed/price?symbol=AAPL&date=2024-09-30&close=200"

# Check DB
Invoke-RestMethod "http://localhost:5046/api/admin/info"

# Analytics
Invoke-RestMethod "http://localhost:5046/api/analytics/roe?symbol=AAPL"
Invoke-RestMethod "http://localhost:5046/api/analytics/fcf-yield?symbol=AAPL"
Invoke-RestMethod "http://localhost:5046/api/analytics/owner-earnings-yield?symbol=AAPL"
```

---

## 📜 License

MIT
