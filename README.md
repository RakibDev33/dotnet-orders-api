# dotnet-orders-api

![CI](https://github.com/RakibDev33/dotnet-orders-api/actions/workflows/ci.yml/badge.svg)

An order management API built with **ASP.NET Core 10 minimal APIs**, **EF Core 10** and **SQLite**. It demonstrates a rich domain model that protects its own invariants, thin HTTP endpoints, consistent RFC 9457 problem details, optimistic concurrency, and integration tests that boot the real application in memory.

## Features

- Order lifecycle enforced by the aggregate: `Pending -> Paid -> Shipped`, cancellation allowed until shipping
- Duplicate SKUs in a request are merged into one line; conflicting prices for the same SKU are rejected
- Request validation with field-level errors (`400 ValidationProblem`)
- Business rule violations returned as `409 Conflict` problem details
- Optimistic concurrency via a version column, so two concurrent state changes cannot silently overwrite each other
- Filtering (`status`, `customerEmail`) and paging on the list endpoint
- `TimeProvider` injection for testable timestamps, health checks, OpenAPI document in development
- xUnit v3 unit tests for the domain and `WebApplicationFactory` integration tests against a real SQLite database
- Multi-stage Docker image running as the non-root `app` user

## Project structure

```
src/Orders.Api/
├── Domain/                  # Order aggregate, OrderLine, DomainException (no framework dependencies)
├── Data/OrdersDbContext.cs  # EF Core mapping: owned lines, enum-as-string, concurrency token
├── Features/Orders/         # Endpoint group and request/response contracts
├── Infrastructure/          # IExceptionHandler mapping exceptions to problem details
└── Program.cs               # Composition root
tests/Orders.Api.Tests/
├── OrderTests.cs            # Domain unit tests
└── OrderEndpointsTests.cs   # HTTP-level integration tests
```

The domain layer has no dependency on ASP.NET Core or EF Core. EF Core maps the aggregate through private setters and a private backing field, so the only way to change an order is through its methods.

## API

| Method | Path                       | Description                                         |
| ------ | -------------------------- | --------------------------------------------------- |
| POST   | `/api/orders`              | Create an order                                     |
| GET    | `/api/orders`              | List orders (`status`, `customerEmail`, `limit`, `offset`) |
| GET    | `/api/orders/{id}`         | Get an order                                        |
| POST   | `/api/orders/{id}/pay`     | Mark a pending order as paid                        |
| POST   | `/api/orders/{id}/ship`    | Ship a paid order                                   |
| POST   | `/api/orders/{id}/cancel`  | Cancel an order that has not shipped                |
| GET    | `/health`                  | Health check                                        |

```bash
curl -X POST http://localhost:5000/api/orders \
  -H 'content-type: application/json' \
  -d '{"customerEmail":"buyer@example.com","lines":[{"sku":"KB-01","quantity":2,"unitPrice":49.90}]}'
```

Trying to ship an order that has not been paid:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "The request conflicts with the current state of the order.",
  "status": 409,
  "detail": "Cannot ship an order that is pending."
}
```

## Running locally

Requires the .NET 10 SDK.

```bash
dotnet run --project src/Orders.Api       # creates orders.db on first start
dotnet test                               # unit + integration tests
```

In the Development environment the OpenAPI document is served at `/openapi/v1.json`.

### Docker

```bash
docker build -t orders-api .
docker run -p 8080:8080 -v orders-data:/app/data orders-api
```

## Design notes

- **Invariants live in the aggregate.** Endpoints never set `Status` directly; they call `MarkPaid`, `Ship` or `Cancel`, which throw `DomainException` when a transition is not allowed. One exception handler turns that into a 409 response.
- **Concurrency.** `Version` is an EF Core concurrency token that the aggregate increments on every transition. A lost race surfaces as `DbUpdateConcurrencyException` and is returned as 409 instead of a silent overwrite.
- **Schema creation.** The sample calls `EnsureCreated` at startup to stay self-contained. A production deployment would use EF Core migrations applied from the release pipeline.

## License

MIT
