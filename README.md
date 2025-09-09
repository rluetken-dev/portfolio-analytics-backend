# portfolio-analytics-backend
Backend service for fetching and analyzing stock quotes.

<!-- Badges -->
![CI](https://github.com/rluetken-dev/portfolio-analytics-backend/actions/workflows/ci.yml/badge.svg?branch=main)
![.NET](https://img.shields.io/badge/.NET-8.0-blue)
![License](https://img.shields.io/badge/License-MIT-green)
<!-- Optional: add CI badge once GitHub Actions is configured -->

---

## ✨ Features
- Fetch daily stock quotes from **Alpha Vantage** (free tier)
- Persist data in **SQLite** via **Entity Framework Core**
- Simple REST API with `POST /api/quotes/refresh` and `GET /api/quotes/latest`
- Interactive Swagger UI

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download)
- Alpha Vantage API Key (free tier: [https://www.alphavantage.co/support/#api-key](https://www.alphavantage.co/support/#api-key))

### Quickstart
```bash
# Clone
git clone https://github.com/YOURUSER/portfolio-analytics-backend.git
cd portfolio-analytics-backend/Portfolio.Api

# Setup
dotnet restore

# Configure API key
dotnet user-secrets init
dotnet user-secrets set "AlphaVantage:ApiKey" "YOUR_KEY"

# Run
dotnet run
```

> Default Swagger docs: `http://localhost:5046/swagger`

---

## 📚 Usage / API

### Refresh quotes
```http
POST /api/quotes/refresh?symbols=AAPL,MSFT&range=5d
```

Example response:
```json
{
  "ok": true,
  "symbols": ["AAPL","MSFT"],
  "inserted": 5,
  "skipped": 0
}
```

### Latest quotes
```http
GET /api/quotes/latest?symbol=AAPL&take=5
```

Example response:
```json
[
  { "id": 1, "symbol": "AAPL", "asOfDate": "2025-09-05", "close": 220.15 },
  { "id": 2, "symbol": "AAPL", "asOfDate": "2025-09-04", "close": 221.70 }
]
```

---

## 🧱 Tech Stack
- C# / .NET 8 Web API
- EF Core + SQLite
- HttpClient with resilience (Polly)
- Swagger / OpenAPI

---

## 📂 Project Structure
```
portfolio-analytics-backend/
├─ Portfolio.Api/           # ASP.NET Core Web API
│  ├─ Controllers/          # QuotesController
│  ├─ Data/                 # EF Core DbContext, Migrations
│  ├─ Models/               # Price entity
│  └─ Services/             # AlphaVantageClient
├─ docs/                    # optional: swagger.png, diagrams
├─ commits.md               # Conventional Commits guide
└─ README.md
```

---

## 🗂️ Endpoints
| Method | Route                     | Description                       |
|-------:|----------------------------|-----------------------------------|
| POST   | `/api/quotes/refresh`     | Fetch new quotes & persist to DB |
| GET    | `/api/quotes/latest`      | Get recent cached quotes         |

---

## 🧪 Tests
```bash
dotnet test
```
(Currently no unit tests – planned in roadmap)

---

## 🧭 Roadmap / Next Steps
- [ ] Import transactions from CSV
- [ ] Portfolio/position aggregation
- [ ] Charts & frontend integration
- [ ] Dockerfile for containerized deployment

➡️ See [COMMITS.md](./COMMITS.md) for commit rules & examples.

---

## 🔐 Configuration
- `AlphaVantage:ApiKey` → set via `dotnet user-secrets`
- Connection string (SQLite) → `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "Default": "Data Source=portfolio.db"
  }
}
```

---

## 🛳️ Deployment
```bash
# Build & run Docker container
docker build -t portfolio-analytics-backend .
docker run -p 5000:5000 portfolio-analytics-backend
```

---

## 📜 License
This project is released under the MIT License.
