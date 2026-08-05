# Portfolio Analytics Backend - Detailed Guide

This document describes the backend architecture, local setup, configuration, data model, API areas, demo data workflow, and development conventions.

For a short project overview, see the root [README.md](../README.md).

## Overview

Portfolio Analytics Backend is an ASP.NET Core Web API for managing user portfolios and analyzing locally stored stock data.

The backend currently supports:

- user registration and login with JWT authentication
- refresh tokens
- user cash balance
- portfolio holdings
- buy and sell transactions
- local ticker storage
- quote and time-series data
- fundamentals storage
- financial analytics endpoints
- SQLite persistence through EF Core
- Swagger/OpenAPI documentation
- local seed data for demo workflows
- optional external provider integration

The project is intended to be usable as a local portfolio demo without requiring public API keys for every basic workflow.

## Architecture

```text
portfolio-analytics-backend/
|-- Portfolio.Api/
|   |-- Controllers/
|   |-- Data/
|   |-- DTOs/
|   |-- Models/
|   |-- Services/
|   |-- Seed/
|   |-- SeedData/
|   |-- Middleware/
|   |-- Migrations/
|   `-- Program.cs
|-- Portfolio.Api.Tests/
|-- docs/
|-- .github/
|-- .gitignore
`-- README.md
```

Runtime flow:

```text
Frontend or Swagger
        |
        v
ASP.NET Core Controllers
        |
        v
Services and business logic
        |
        v
EF Core DbContext
        |
        v
SQLite portfolio.db
```

External provider flow:

```text
FmpClient              -> Financial Modeling Prep
AlphaVantageClient    -> Alpha Vantage
SeedFileService       -> local SeedData/companies/*.json
```

## Runtime Modes

### Local Demo Mode

The default configuration uses SQLite and enables demo-oriented behavior:

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=portfolio.db"
  },
  "DemoMode": true
}
```

Demo mode is intended for local development and portfolio presentation. It allows local database workflows and supports seed data stored in the repository.

### Live Provider Mode

Live provider calls require API keys:

- `Fmp:ApiKey` for Financial Modeling Prep data
- `AlphaVantage:ApiKey` for Alpha Vantage quote data

Provider keys should be configured through user secrets or environment variables. They must not be committed.

## Configuration

### appsettings.json

Default local configuration:

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=portfolio.db"
  },
  "AlphaVantage": {
    "BaseUrl": "https://www.alphavantage.co",
    "ApiKey": ""
  },
  "Fmp": {
    "ApiKey": ""
  },
  "Jwt": {
    "Secret": "CHANGE_ME_USE_USER_SECRETS"
  },
  "DemoMode": true
}
```

### User Secrets

From `Portfolio.Api/`:

```powershell
dotnet user-secrets set "Jwt:Secret" "<your-local-jwt-secret>"
dotnet user-secrets set "Fmp:ApiKey" "<your-fmp-api-key>"
dotnet user-secrets set "AlphaVantage:ApiKey" "<your-alpha-vantage-api-key>"
```

`Jwt:Secret` is required for authentication. Provider keys are only required for live provider calls.

### Environment Variables

For deployment-style configuration:

```powershell
$env:ConnectionStrings__Default="Data Source=portfolio.db"
$env:Jwt__Secret="<your-jwt-secret>"
$env:Fmp__ApiKey="<your-fmp-api-key>"
$env:AlphaVantage__ApiKey="<your-alpha-vantage-api-key>"
```

## Run Locally

From the repository root:

```powershell
cd .\Portfolio.Api
dotnet run
```

The API listens on:

```text
http://localhost:5046
```

Swagger UI:

```text
http://localhost:5046/swagger
```

Health checks:

```text
http://localhost:5046/health
http://localhost:5046/api/System/status
```

## Database

The backend uses SQLite through EF Core.

Default database file:

```text
Portfolio.Api/portfolio.db
```

The database is created and migrated on startup. Database files are ignored by Git.

To reset local data:

```powershell
cd .\Portfolio.Api
Rename-Item .\portfolio.db portfolio.old.db
dotnet run
```

## Demo Data

Demo company data is stored in:

```text
Portfolio.Api/SeedData/companies/
Portfolio.Api/Data/companies-fallback.json
```

The seed files contain selected company profiles, quote rows, and fundamentals. They are intended to support local portfolio demos without live provider calls.

Available seed companies currently include AAPL, MSFT, NVDA, GOOGL, AMZN, TSLA, KO, JPM, JNJ, DIS, XOM, PLD, NEE, LMT, and LIN.

Admin seed endpoints can validate and apply seed files in development/demo scenarios.

To apply demo data locally, start the API, authenticate as an admin user, and run the seed apply endpoint for selected symbols, for example:

```http
POST /api/admin/seed/company-file/AAPL/apply
POST /api/admin/seed/company-file/MSFT/apply
```

Seed endpoints require admin authorization.

## API Areas

### Users

User endpoints support registration, login, token refresh, logout, current-user lookup, and wallet operations.

```http
POST /api/User/register
POST /api/User/login
POST /api/User/refresh
POST /api/User/logout
GET  /api/User/me
GET  /api/User/balance
POST /api/User/deposit
POST /api/User/withdraw
```

New users start with a zero cash balance. Cash can be added through the deposit endpoint.

### Portfolio Holdings

Portfolio endpoints manage the current user's company holdings.

```http
GET    /api/UserCompany
POST   /api/UserCompany
PUT    /api/UserCompany/{id}
DELETE /api/UserCompany/{id}
```

### Transactions

Transaction endpoints record and retrieve buy/sell activity.

```http
POST /api/UserCompanyTransactions
GET  /api/UserCompanyTransactions/mine
GET  /api/UserCompanyTransactions/by-symbol/{symbol}
```

### Companies

Company endpoints support local ticker lookup, search, adding companies, and adding predefined popular companies.

```http
GET  /api/companies
GET  /api/companies/search?q=AAPL&limit=10
POST /api/companies/add
POST /api/companies/add-popular
POST /api/companies/{symbol}/refresh-profile
```

### Quotes

Quote endpoints provide locally stored price data and optional live refresh.

```http
POST /api/quotes/refresh?symbols=AAPL&range=30d
GET  /api/quotes/latest?symbol=AAPL&take=5
GET  /api/quotes/current?symbol=AAPL
GET  /api/quotes/timeseries?symbol=AAPL
GET  /api/quotes/ohlc?symbol=AAPL
GET  /api/quotes/quarters?symbol=AAPL
```

Live refresh/current quote calls require Alpha Vantage access. Database-backed quote reads can work from seeded or previously stored records.

### Analytics

Analytics endpoints calculate financial metrics from stored price and fundamentals data.

Examples:

```http
GET /api/analytics/roe?symbol=AAPL
GET /api/analytics/roa?symbol=AAPL
GET /api/analytics/pe?symbol=AAPL
GET /api/analytics/pb?symbol=AAPL
GET /api/analytics/fcf-yield?symbol=AAPL
GET /api/analytics/owner-earnings?symbol=AAPL
GET /api/analytics/equity-cagr?symbol=AAPL
```

See [analytics-endpoints.md](./analytics-endpoints.md) for the analytics endpoint overview.

### Admin And Maintenance

Admin endpoints support database diagnostics, maintenance, truncation, user management, ingestion helpers, and seed tools.

Examples:

```http
GET  /api/admin/info
POST /api/admin/vacuum
POST /api/admin/prune
POST /api/admin/truncate
POST /api/admin/seed/company-file/{symbol}
POST /api/admin/seed/company-file/{symbol}/apply
```

Admin endpoints require authorization and are intended for local development, maintenance, and demo setup.

## Error Handling

The backend uses a global error handling middleware and custom application exceptions.

Typical behavior:

| Situation | Expected Response |
|---|---|
| Invalid request | `400 Bad Request` |
| Unauthorized request | `401 Unauthorized` |
| Forbidden operation | `403 Forbidden` |
| Missing resource | `404 Not Found` |
| Provider rate limit | `429 Too Many Requests` or provider-specific error response |
| Upstream provider failure | `502 Bad Gateway` or `503 Service Unavailable` |

Live provider errors should be handled without exposing secrets or stack traces.

## Security

Security expectations:

- real API keys must be stored in user secrets or environment variables
- local SQLite database files must not be committed
- JWT secret must not use the placeholder value outside local demos
- admin endpoints must remain protected
- public deployments should use production-grade CORS, HTTPS, and secret management

## Testing

Run all tests:

```powershell
dotnet test
```

The current test project includes focused tests for finance math logic.

Recommended future test coverage:

- user registration and login
- wallet deposit and withdrawal
- portfolio transaction behavior
- seed file loading
- analytics endpoint behavior with seeded data
- provider-missing-key behavior

## Development Notes

Useful commands:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project .\Portfolio.Api\Portfolio.Api.csproj
```

Before publishing changes:

```powershell
git status -sb
dotnet test
```

Check for accidentally tracked local files or secrets:

```powershell
git ls-files | Select-String "portfolio.db|appsettings.Development.json|.user|bin/|obj/|.vs/"
Select-String -Path .\**\*.json, .\**\*.cs, .\README.md, .\docs\*.md -Pattern "Password=|ApiKey=|secret|token|Training123|192.168."
```

## Documentation Files

- [Root README](../README.md)
- [Analytics endpoints](./analytics-endpoints.md)
- [OpenAPI specification](./openapi.yaml)
- [Postman collection](./portfolio_analytics_postman_collection.json)
- [Commit conventions](./COMMITS.md)

## Roadmap

Potential next improvements:

- make missing-provider-key behavior consistent across all external clients
- add an explicit demo setup workflow
- add integration tests for authentication and portfolio workflows
- add tests for seed file loading and analytics with seeded data
- document frontend/backend startup together
- add screenshots to the frontend README
