# 📊 Portfolio Analytics Backend (.NET 8 + EF Core + SQLite)

![CI](https://github.com/rluetken-dev/portfolio-analytics-backend/actions/workflows/ci.yml/badge.svg?branch=main)
![.NET](https://img.shields.io/badge/.NET-8.0-blue)
![License](https://img.shields.io/badge/license-MIT-green)

Backend service for ingesting and analyzing **fundamental data** (Income, Balance, Cashflow).  
Data source: Financial Modeling Prep (**FMP `/stable`**).  
**TTM (Trailing Twelve Months)** metrics are **calculated locally** from stored quarterly data – no external API call required.

---

## 🚀 Quickstart

```bash
git clone https://github.com/rluetken-dev/portfolio-analytics-backend.git
cd portfolio-analytics-backend/Portfolio.Api

# Set API key (once)
dotnet user-secrets set "Fmp:ApiKey" "<your_api_key>"

# Run the API
dotnet run
```

The API will be available at  
👉 [http://localhost:5179](http://localhost:5179)

---

## 📖 Documentation

- [Detailed project documentation](docs/README.md)  
- [OpenAPI Spec](docs/openapi.yaml)  
- [Postman Collection](docs/portfolio_analytics_postman_collection.json)  
- [Commit Conventions](docs/COMMITS.md)  

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
