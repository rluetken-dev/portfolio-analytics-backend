# 📊 Portfolio Analytics Backend (.NET 8 + EF Core + SQLite)

[![CI/CD](https://github.com/rluetken-dev/portfolio-analytics-backend/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/rluetken-dev/portfolio-analytics-backend/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-8.0-blue)
![EF Core](https://img.shields.io/badge/EF_Core-8.0-green)
![SQLite](https://img.shields.io/badge/SQLite-3-lightgrey)
![Swagger](https://img.shields.io/badge/Swagger-OpenAPI-85EA2D)
![License](https://img.shields.io/badge/license-MIT-green)

Backend service for ingesting and analyzing **fundamental data** (Income, Balance, Cashflow).  
Data source: Financial Modeling Prep (**FMP `/stable`**).  
**TTM (Trailing Twelve Months)** metrics are **calculated locally** from stored quarterly data – no external API call required.

---

## 🚀 Quickstart

```bash
git clone https://github.com/rluetken-dev/portfolio-analytics-backend.git
cd portfolio-analytics-backend/Portfolio.Api

# Set local secrets once
# FMP is required for live financial data; Alpha Vantage is optional.
dotnet user-secrets set "Jwt:Secret" "<your_local_jwt_secret>"
dotnet user-secrets set "Fmp:ApiKey" "<your_api_key>"
dotnet user-secrets set "AlphaVantage:ApiKey" "<your_av_api_key>"   # optional

# Run the API
dotnet run
```

The API will be available at  
👉 [http://localhost:5046](http://localhost:5046)

---

## 📖 Documentation

- **Detailed project documentation**: [docs/README.md](./docs/README.md)
- **OpenAPI specification**: [docs/openapi.yaml](./docs/openapi.yaml)
- **Swagger UI**: [http://localhost:5046/swagger](http://localhost:5046/swagger)
- **Analytics endpoints overview**: [docs/analytics-endpoints.md](./docs/analytics-endpoints.md)
- **Postman collection**: [docs/portfolio_analytics_postman_collection.json](./docs/portfolio_analytics_postman_collection.json)
- **Commit conventions**: [docs/COMMITS.md](./docs/COMMITS.md)

---

## 📂 Project Structure

```
portfolio-analytics-backend/
├─ Portfolio.Api/     # ASP.NET Core API (code)
├─ docs/              # Documentation (API, OpenAPI, Postman, guides)
├─ .github/           # CI/CD workflows
├─ .gitignore
└─ README.md          # Project overview & quickstart
```

---

**Built with ❤️ for the investment community**