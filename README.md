# Portfolio Analytics Backend (.NET 8 + EF Core + SQLite)

![CI](https://github.com/rluetken-dev/portfolio-analytics-backend/actions/workflows/ci.yml/badge.svg?branch=main)
![.NET](https://img.shields.io/badge/.NET-8.0-blue)
![License](https://img.shields.io/badge/License-MIT-green)

Backend service for fetching, ingesting, and analyzing stock **fundamentals**.  
**Now using Financial Modeling Prep (FMP) `/stable`** endpoints for Income Statement, Balance Sheet, and Cash Flow data.

---

## ✨ Highlights (2025‑09‑10)
- ✅ Migrated fundamentals to **FMP `/stable`**
- ✅ Added **ingest services** for Income/Balance/Cash (+ EF entities & migrations)
- ✅ Added **read endpoints** for each statement
- ✅ Added **TTM (Trailing Twelve Months)** metrics
- ✅ Added **plan‑safe caps** (configurable quotas to respect FMP plan limits)
- ✅ Added **snapshot route** to fetch a concise, latest view

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download)
- FMP API key ([financialmodelingprep.com](https://financialmodelingprep.com))

### Setup & Run
```bash
# Clone
git clone https://github.com/rluetken-dev/portfolio-analytics-backend.git
cd portfolio-analytics-backend/Portfolio.Api

# Restore
dotnet restore

# Configure secrets (reads from configuration key "Fmp:ApiKey")
dotnet user-secrets init
dotnet user-secrets set "Fmp:ApiKey" "YOUR_FMP_API_KEY"

# Apply database migrations (if not created yet)
# (Install dotnet-ef once: dotnet tool install --global dotnet-ef)
dotnet ef database update --project Portfolio.Api

# Run the API
dotnet run
```
> Swagger: `http://localhost:5046/swagger` (port may differ on your machine)

---

## 🔐 Configuration

`appsettings.json` relevant keys (values can be overridden by user-secrets / environment variables):

```json
{
  "Fmp": {
    "ApiKey": "override-with-user-secrets",
    "UseStable": true
  },
  "PlanCaps": {
    "RequestsPerMinute": 5,
    "RequestsPerDay": 250
  },
  "ConnectionStrings": {
    "Default": "Data Source=portfolio.db"
  }
}
```

- **Fmp:ApiKey** – required, set via `dotnet user-secrets` as shown above.
- **UseStable** – use the FMP `/stable` endpoints for more consistent series.
- **PlanCaps** – optional throttling/safety limits so the app stays within your FMP plan.

---

## 📚 API Overview

> **Note:** Exact parameter names may evolve. Use Swagger for the authoritative contract.

### Ingest
Ingest fundamentals from FMP and persist them in SQLite.
```http
POST /api/ingest/fundamentals?symbol=AAPL&years=5&quarterly=false
```
- Pulls **Income Statement**, **Balance Sheet**, and **Cash Flow** (FMP `/stable`).
- Respects **PlanCaps** and logs skipped pages if limits would be exceeded.

### Read: Statements
Return the most recent persisted rows.
```http
GET /api/fundamentals/income?symbol=AAPL&limit=4
GET /api/fundamentals/balance?symbol=AAPL&limit=4
GET /api/fundamentals/cash?symbol=AAPL&limit=4
```

### Read: TTM Metrics
Compute trailing‑twelve‑months from the persisted series.
```http
GET /api/fundamentals/ttm?symbol=AAPL
```

### Snapshot
A concise, latest view (e.g., last reported values across statements).
```http
GET /api/data/snapshot?symbol=AAPL
```

---

## 🧱 Tech Stack
- C# / **.NET 8** Web API
- **EF Core** + **SQLite**
- **HttpClient** (typed) with resilience policies (Polly-ready)
- **Swagger / OpenAPI**

---

## 📂 Project Structure
```
portfolio-analytics-backend/
├─ Portfolio.Api/
│  ├─ Controllers/
│  │  ├─ DataController.cs           # snapshot, health, misc data views
│  │  ├─ FundamentalsController.cs   # read endpoints (income/balance/cash/ttm)
│  │  └─ IngestController.cs         # ingest fundamentals from FMP
│  ├─ Data/
│  │  ├─ Entities/
│  │  │  ├─ IncomeStatementEntity.cs
│  │  │  ├─ BalanceSheetEntity.cs
│  │  │  └─ CashFlowEntity.cs
│  │  └─ Migrations/                 # EF Core migrations
│  ├─ Services/
│  │  ├─ FmpClient.cs                # thin client; reads Fmp:ApiKey
│  │  └─ IngestService.cs            # pulls & maps FMP models -> EF entities
│  ├─ appsettings*.json
│  └─ Program.cs / Startup
└─ README.md
```

---

## 🧪 Testing
```bash
dotnet test
```
Planned: unit tests for mapping, TTM computation, and controller contracts.

---

## 🧭 Roadmap
- [ ] Derived ratios (e.g., margins, leverage, quality checks)
- [ ] Portfolio rollups (positions, weights, P&L)
- [ ] Dockerfile + compose (app + DB)
- [ ] Basic auth or API key for write routes
- [ ] Caching headers & ETags on read endpoints

---

## 📜 License
MIT
