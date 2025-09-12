# 📊 Analytics API Endpoints

| Method | Endpoint                              | Description                                                                                                                                          |
|--------|---------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------|
| GET    | `/api/analytics/roe`                  | Returns latest annual **ROE** (Return on Equity). Uses average equity if a prior annual equity exists; otherwise falls back to end-of-period equity. |
| GET    | `/api/analytics/debt-to-equity`       | Returns the latest annual **Debt-to-Equity ratio (D/E)**.                                                                                            |
| GET    | `/api/analytics/net-margin`           | Returns the latest annual **Net Margin**.                                                                                                            |
| GET    | `/api/analytics/roa`                  | Returns the latest annual **ROA** (Return on Assets).                                                                                                |
| GET    | `/api/analytics/equity-ratio`         | Returns the latest annual **Equity Ratio**.                                                                                                          |
| GET    | `/api/analytics/debt-to-assets`       | Returns the latest annual **Debt-to-Assets ratio**.                                                                                                  |
| GET    | `/api/analytics/price`                | Returns the latest available **close price** for a ticker.                                                                                           |
| GET    | `/api/analytics/eps`                  | Returns the latest annual **Earnings Per Share (EPS)**.                                                                                              |
| GET    | `/api/analytics/pe`                   | Returns the latest annual **Price-to-Earnings ratio (P/E)**.                                                                                         |
| GET    | `/api/analytics/bvps`                 | Returns the latest annual **Book Value per Share (BVPS)**.                                                                                           |
| GET    | `/api/analytics/pb`                   | Returns the latest **Price-to-Book ratio (P/B)**.                                                                                                    |
| GET    | `/api/analytics/asset-turnover`       | Returns the latest annual **Asset Turnover**.                                                                                                        |
| GET    | `/api/analytics/equity-cagr`          | Returns **Equity CAGR** (Compound Annual Growth Rate) using earliest and latest annual balance rows.                                                 |
| GET    | `/api/analytics/fcf`                  | Returns latest annual **Free Cash Flow (FCF)**.                                                                                                      |
| GET    | `/api/analytics/fcf-yield`            | Returns latest annual **FCF Yield = FCF / MarketCap**.                                                                                               |
| GET    | `/api/analytics/fcf-margin`           | Returns latest annual **FCF Margin = FCF / Revenue**.                                                                                                |
| GET    | `/api/analytics/owner-earnings`       | Returns latest annual **Owner Earnings** (Buffett-style).                                                                                            |
| GET    | `/api/analytics/owner-earnings-yield` | Returns latest annual **Owner Earnings Yield = OE / MarketCap**.                                                                                     |
| GET    | `/api/analytics/oeps`                 | Returns latest annual **Owner Earnings per Share (OEPS)**.                                                                                           |
| GET    | `/api/analytics/p-to-oe`              | Returns latest annual **Price-to-Owner-Earnings ratio (P/OE)**.                                                                                      |
