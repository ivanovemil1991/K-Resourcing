# Flows

How a request travels through the code. The diagrams are [Mermaid](https://mermaid.js.org) and render on GitHub.
Design decisions and trade-offs are in [SOLUTION.md](SOLUTION.md).

## Layers

Dependencies point inwards: `Api` and `Infrastructure` know `Application`, which knows `Domain`.
`Application` defines the interfaces (`IRepository<T>`, `IAuditer`) that `Infrastructure` implements.

```mermaid
flowchart LR
    Client --> Api["Api<br/>controllers, auth, rate limit, error mapping"]
    Api --> App["Application<br/>command/query handlers, validators, PremiumCalculator"]
    App --> Domain["Domain<br/>Claim, Cover"]
    Infra["Infrastructure<br/>MongoRepository, cover cache, outbox, relay"] -.implements.-> App
    Infra --> Mongo[(MongoDB)]
    Infra --> Redis[(Redis)]
    Infra --> Sql[(SQL Server audit)]
```

| Where | What to read |
|---|---|
| `Api/Controllers` | Thin: bind input, call one handler, map the result to HTTP |
| `Application/Claims`, `Application/Covers` | One handler per use case, plus its command/query and validator |
| `Application/Covers/PremiumCalculator.cs` | The tiered premium rules |
| `Infrastructure/Persistence` | `EfRepository<T>` (MongoDB) and `CachedCoverRepository` (Redis decorator) |
| `Infrastructure/Auditing` | `OutboxAuditer` stages events; `AuditOutboxRelay` delivers them to SQL |

## Request pipeline

```mermaid
flowchart TD
    A[HTTP request] --> B[Trace id and logging]
    B --> C[Authentication: Entra JWT]
    C --> D{"Rate limit<br/>30 per second per caller"}
    D -- exceeded --> E[429]
    D -- ok --> F{"Authorization<br/>Read or Write policy"}
    F -- denied --> G[401 / 403]
    F -- ok --> H[Controller] --> I[Handler]
    I -- ValidationException --> J[400 ProblemDetails with every broken rule]
    I -- other exception --> K[500 ProblemDetails with trace id]
    I -- result --> L[200 / 201 / 204 / 404]
```

## Create a claim

The validator needs the cover, which comes from the cache when possible.

```mermaid
sequenceDiagram
    participant C as Client
    participant H as CreateClaimCommandHandler
    participant V as CreateClaimCommandValidator
    participant R as Cover repository (cached)
    participant A as OutboxAuditer
    participant M as MongoDB

    C->>H: POST /Claims
    H->>V: validate
    V->>R: GetById(coverId)
    R-->>V: cover
    Note over V: DamageCost ≤ 100,000<br/>Created within cover period
    V-->>H: valid (otherwise 400)
    H->>A: stage "Claim created" event
    H->>M: save claim + outbox event (one transaction)
    H-->>C: 201 claim
```

## Create or delete a cover (cache)

The cover cache is rebuilt on every change, so it never serves a deleted cover.

```mermaid
flowchart TD
    Read[GetById] --> Hit{In Redis?}
    Hit -- yes --> Return[Return cover]
    Hit -- no --> Db[Read MongoDB] --> Found{Found?}
    Found -- yes --> Store[Store in Redis, 5 min] --> Return
    Found -- no --> Null[Return null, nothing cached]

    Create[Create cover] --> SaveDb[Save to MongoDB] --> Warm[Write to Redis]
    Delete[Delete cover] --> DelDb[Delete from MongoDB] --> Evict[Remove from Redis]
```

If Redis fails or times out (500 ms), the error is logged as a warning and the database is used instead.

## Auditing: outbox and relay

The HTTP request only writes to MongoDB. The audit event is saved in the same transaction as the change, so
it cannot be lost or exist without the change. `AuditOutboxRelay` copies it to SQL Server in the background.

```mermaid
sequenceDiagram
    participant H as Handler
    participant M as MongoDB (entity + outbox)
    participant R as AuditOutboxRelay
    participant S as SQL Server (audit + receipt)

    H->>M: save entity and outbox event (one transaction)
    H-->>H: respond to the client
    loop every AuditPollSeconds
        R->>M: read next batch (50, by id)
        R->>S: save audit row + receipt (one transaction)
        alt receipt already exists
            S-->>R: duplicate, treated as delivered
        end
        R->>M: delete the outbox event
    end
```

Failure behaviour:

| Failure | Result |
|---|---|
| SQL unavailable | Event stays in the outbox, error logged, retried on the next scan |
| Crash after the SQL write, before the outbox delete | Event is relayed again; the receipt makes it a skipped duplicate |
| Two instances relay the same event | One SQL insert wins, the other is a skipped duplicate |
| One event keeps failing | It is skipped within the scan so it cannot block later events |

## Premium calculation

`PremiumCalculator.Calculate` multiplies the base day rate (1250) by the vessel multiplier, then prices the days
tier by tier.

| Days | Yacht discount | Other types discount |
|---|---|---|
| 1–30 | none | none |
| 31–180 | 5% | 2% |
| 181 and later | 8% | 3% |

Vessel multipliers: Yacht 1.1, Passenger ship 1.2, Tanker 1.5, other 1.3.
