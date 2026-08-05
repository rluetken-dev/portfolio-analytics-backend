# Portfolio Analytics Backend

[![CI/CD](https://github.com/rluetken-dev/portfolio-analytics-backend/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/rluetken-dev/portfolio-analytics-backend/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-8.0-blue)
![EF Core](https://img.shields.io/badge/EF_Core-8.0-green)
![SQLite](https://img.shields.io/badge/SQLite-3-lightgrey)
![Swagger](https://img.shields.io/badge/Swagger-OpenAPI-85EA2D)

ASP.NET Core backend for portfolio analytics, stock fundamentals, user portfolios, transactions, and local financial data analysis.

The project is designed as a portfolio and training backend. It supports local SQLite persistence, JWT authentication, Swagger documentation, demo seed data, and optional integration with external financial data providers.

## Current Status

The backend is in active development and currently supports:

- user registration and login with JWT authentication
- refresh tokens
- user cash balance
- portfolio holdings
- buy/sell transactions
- local ticker database
- quote and time-series endpoints
- fundamental data storage
- analytics endpoints for financial ratios
- SQLite persistence with EF Core migrations
- Swagger/OpenAPI documentation
- demo seed data for selected companies

## Demo And API Key Behavior

The project is intended to run locally without external API keys for demo and database-backed workflows.

External provider keys are only required for live data ingestion:

- Financial Modeling Prep: fundamentals and company profile data
- Alpha Vantage: quote and price data

Without API keys, database-backed workflows can use local seed data and stored records. Live ingestion endpoints require provider keys.

Demo data is stored in:

```text
Portfolio.Api/SeedData/companies/
Portfolio.Api/Data/companies-fallback.json
```

## Tech Stack

- C#
- .NET 8
- ASP.NET Core Web API
- EF Core
- SQLite
- JWT authentication
- Swagger/OpenAPI
- xUnit
- Financial Modeling Prep integration
- Alpha Vantage integration

## Run The API

From the repository root:

```powershell
cd .\Portfolio.Api
dotnet run
```

The API listens on:

```text
http://localhost:5046
```

Swagger UI is available at:

```text
http://localhost:5046/swagger
```

## Configuration

Default local configuration uses SQLite:

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=portfolio.db"
  },
  "DemoMode": true
}
```

For local authentication and optional live external data, configure user secrets:

```powershell
cd .\Portfolio.Api

dotnet user-secrets set "Jwt:Secret" "<your-local-jwt-secret>"
dotnet user-secrets set "Fmp:ApiKey" "<your-fmp-api-key>"
dotnet user-secrets set "AlphaVantage:ApiKey" "<your-alpha-vantage-api-key>"
```

`Jwt:Secret` is required for authentication. `Fmp:ApiKey` and `AlphaVantage:ApiKey` are only required for live provider calls.

Do not commit real API keys, secrets, or local database files.

## Database

The local SQLite database file is created as:

```text
Portfolio.Api/portfolio.db
```

Database files are ignored by Git.

To reset local data during development, stop the API and remove or rename the database file:

```powershell
cd .\Portfolio.Api
Rename-Item .\portfolio.db portfolio.old.db
```

Then start the API again.

## Main API Areas

### System

- `GET /health`
- Swagger UI: `http://localhost:5046/swagger`

### Users

- `POST /api/User/register`
- `POST /api/User/login`
- `POST /api/User/refresh`
- `POST /api/User/logout`
- `GET /api/User/me`
- `GET /api/User/balance`
- `POST /api/User/deposit`
- `POST /api/User/withdraw`

### Portfolio

- `GET /api/UserCompany`
- `POST /api/UserCompany`
- `PUT /api/UserCompany/{id}`
- `DELETE /api/UserCompany/{id}`

### Transactions

- `POST /api/UserCompanyTransactions`
- `GET /api/UserCompanyTransactions/mine`
- `GET /api/UserCompanyTransactions/by-symbol/{symbol}`

### Companies

- `GET /api/companies`
- `GET /api/companies/search`
- `POST /api/companies/add`
- `POST /api/companies/add-popular`

### Quotes And Analytics

- `GET /api/quotes/latest`
- `GET /api/quotes/timeseries`
- `GET /api/quotes/ohlc`
- `GET /api/analytics/roe`
- `GET /api/analytics/pe`
- `GET /api/analytics/pb`
- `GET /api/analytics/fcf-yield`

## Tests

Run tests from the repository root:

```powershell
dotnet test
```

## Documentation

- [Detailed backend guide](./docs/README.md)
- [Analytics endpoints](./docs/analytics-endpoints.md)
- [OpenAPI specification](./docs/openapi.yaml)
- [Postman collection](./docs/portfolio_analytics_postman_collection.json)
- [Commit conventions](./docs/COMMITS.md)

## Related Projects

Frontend repository:

[portfolio-analytics-frontend](https://github.com/rluetken-dev/portfolio-analytics-frontend)

## Notes

This project is a portfolio and training project focused on ASP.NET Core, EF Core, authentication, financial data modeling, local-first workflows, and API integration.
