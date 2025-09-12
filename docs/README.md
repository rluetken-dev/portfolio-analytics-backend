# 📖 Project Documentation – Portfolio Analytics Backend

This document provides a **deep dive** into the architecture, data model, and API of the Portfolio Analytics Backend.

---

## 📚 Table of Contents
1. [Highlights](#-highlights)
2. [Getting Started](#-getting-started)
3. [Configuration](#-configuration)
4. [API Overview](#-api-overview)
   - [Ingest](#ingest-writes-to-sql)
   - [Read](#read-from-sql)
   - [TTM](#ttm-trailing-twelve-months)
   - [Live Fundamentals](#live-fundamentals-direct-fmp-calls)
   - [Analytics](#analytics-buffett-metrics)
   - [Admin & Maintenance](#admin--maintenance)
   - [Demo Seeds](#demo-seeds-only-with-demomodetrue)
5. [Data Model](#-data-model-ef-core)
6. [Project Structure](#-project-structure)
7. [Quick Test](#-quick-test)
8. [Further Resources](#-further-resources)

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
git clone https://github.com/rluetken-dev/portfolio-analytics-backend.git
cd portfolio-analytics-backend/Portfolio.Api

dotnet restore
dotnet user-secrets init
dotnet user-secrets set "Fmp:ApiKey" "YOUR_FMP_API_KEY"
dotnet ef database update --project Portfolio.Api
dotnet run
```

Swagger UI → `http://localhost:5046/swagger` (port may differ)

---

## 🔐 Configuration

```json
{
  "ConnectionStrings": { "Default": "Data Source=portfolio.db" },
  "Fmp": { "ApiKey": "override-with-user-secrets" },
  "DemoMode": true
}
```

- **Fmp:ApiKey** – required for ingest/live endpoints  
- **DemoMode** – enables `/api/admin/seed/*` and destructive ops (should be `false` in production)  
- Free-tier FMP plans require `limit ≤ 5` on many calls  

---

## 📚 API Overview

### Ingest (writes to SQL)
```http
POST /api/admin/ingest/fmp-annual?symbol=AAPL&limit=5
POST /api/admin/ingest/fmp-cashflow-annual?symbol=AAPL&limit=5
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

### Live Fundamentals (direct FMP calls)
```http
GET /api/fundamentals/{symbol}/income-statement/stable
GET /api/fundamentals/{symbol}/balance-sheet/stable
GET /api/fundamentals/{symbol}/cash-flow/stable
```

### Analytics (Buffett metrics)
```http
GET /api/analytics/roe?symbol=AAPL
GET /api/analytics/pe?symbol=AAPL
GET /api/analytics/fcf-yield?symbol=AAPL
```

### Admin & Maintenance
```http
GET  /api/admin/info
POST /api/admin/vacuum
POST /api/admin/prune
```

### Demo Seeds (only with `DemoMode=true`)
```http
POST /api/admin/seed/ticker?symbol=AAPL&name=Apple%20Inc
POST /api/admin/seed/price?symbol=AAPL&date=2024-09-30&close=200
```

---

## 🧱 Data Model (EF Core)

Entities:
- `income_statements` → `IncomeStatementEntity`
- `balance_sheets` → `BalanceSheetEntity`
- `cash_flows` → `CashFlowEntity`
- `prices` → `Price`
- `tickers` → `Ticker`

Constraints:
- Unique `(Symbol, Date, Frequency)` per fundamentals table  
- Dates stored as UTC `DateTime` in SQLite  

---

## 📂 Project Structure

```
Portfolio.Api/
├─ Controllers/
├─ Data/
├─ Migrations/
├─ Models/
├─ Services/
├─ Program.cs
└─ appsettings.json
```

---

## 🧪 Quick Test

```powershell
Invoke-RestMethod -Method Post "http://localhost:5046/api/admin/ingest/fmp-annual?symbol=AAPL&limit=5"
Invoke-RestMethod "http://localhost:5046/api/analytics/roe?symbol=AAPL"
```

---

## 📎 Further Resources
- [Commit Conventions](COMMITS.md)  
- [OpenAPI Spec](openapi.yaml)  
- [Postman Collection](portfolio_analytics_postman_collection.json)
