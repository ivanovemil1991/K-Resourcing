# Claims API – Solution Notes

The sections below cover the design, deployment, assumptions and remaining verification.

## How to run

Requirements: .NET 10 SDK and a running Docker engine for the default Development setup
and integration tests. Production uses configured external databases instead.

```powershell
dotnet run --project Claims          # Swagger UI on /swagger (Development environment)
dotnet test                          # unit + integration tests
dotnet test -- --filter-class "*PremiumCalculatorTests"   # a single test class
```

---

## Actions taken: Azure deployment and scaling

- Database connection strings, the MongoDB database name, local containers, migration on
  startup and outbox polling are configuration settings. Startup validates them;
  local containers are allowed only in Development. No credentials are committed.
- Instances share the configured databases and keep no application session state.
  Claims and covers are saved with their audit outbox entry in one MongoDB transaction.
  A worker retries delivery to SQL; a unique SQL receipt and the audit row are committed
  together, so competing replicas and retries cannot create duplicate rows for one event.
  SQL delivery is eventual, not a distributed transaction across MongoDB and SQL.
- JWT bearer authentication validates Entra-issued tokens. Read endpoints require
  `claims.read` or `Claims.Read`; writes require `claims.write` or `Claims.Write`.
  Write permissions also allow reads. Authentication can be disabled only in Development
  or Test; the checked-in Development configuration disables it for local use.
- `/health/live` checks that the process is running. `/health/ready` checks both databases
  with a five-second timeout; it does not check the audit schema. Logs are JSON on stdout,
  with trace scopes. Swagger remains Development-only.
- Audit migrations default to off in Production, so replicas do not all migrate on startup.
  Apply migrations once as a deployment step before starting or updating replicas.
- A multi-stage `Dockerfile` publishes a non-root container listening on port 8080.
  HTTPS is terminated at the Azure ingress; the app does not trust forwarded headers or
  redirect HTTP in Production.

### Required application settings

Use App Service application settings or Container Apps secrets/environment variables.
Double underscores map to nested configuration; keep connection strings in Key Vault or
the platform's secret store, never in source control.

| Setting | Purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` for Azure. |
| `Authentication__Enabled` | `true` in Production (the default). |
| `Authentication__Authority` | Tenant-specific Entra authority, e.g. `https://login.microsoftonline.com/<tenant-id>/v2.0`. |
| `Authentication__Audience` | Expected API audience in the access token; use the value configured for the API registration. |
| `Database__AuditSqlServer` | Azure SQL connection string, including server and database; enable encrypted connections. |
| `Database__ClaimsMongoDb` | Connection string for a transaction-capable MongoDB replica set, such as MongoDB Atlas on Azure. |
| `Database__MongoDatabaseName` | MongoDB database name; defaults to `Claims`. |
| `Database__UseLocalContainers` | `false` in Production; `true` is allowed only in Development. |
| `Database__MigrateAuditOnStart` | `false` in Production; run migrations separately. |
| `Database__AuditPollSeconds` | Delay between outbox scans; defaults to `5`. |

### Deploy and scale

1. Provision Azure SQL and a MongoDB Atlas replica set on Azure, private networking/firewall
   access, and credentials with the permissions needed by the app. Pre-create the `claims`,
   `covers`, and `auditOutbox` collections before accepting traffic. A standalone MongoDB
   server cannot run the required transactions.
2. Build and push the image: `docker build -t <registry>/claims:<tag> .`, then
   `docker push <registry>/claims:<tag>`. Deploy it to Azure App Service for Containers
   or Azure Container Apps with the settings above and port 8080.
3. Before routing traffic, run `dotnet ef database update --project Claims --context AuditContext`
   once from a deployment job with .NET 10, the EF CLI and the same production database settings.
   The job needs schema-change permissions; use least-privilege runtime credentials separately.
   When upgrading from the in-memory audit version, drain and stop the old instances before
   enabling traffic on the new ones; old instances do not write to the outbox.
4. Register the API in Entra ID, expose `claims.read` and `claims.write` delegated scopes
   and/or define `Claims.Read` and `Claims.Write` application roles. Grant clients only
   the necessary permissions and set the authority and audience above.
5. Require HTTPS at Azure ingress, configure readiness/liveness probes, and scale replicas
   against the same databases. Monitor readiness, relay errors and the size and age of
   `auditOutbox`; provision capacity for an extended SQL outage.

### Database compatibility and verification

The [MongoDB EF Core provider](https://www.mongodb.com/docs/entity-framework/current/interact-data/transactions/)
uses a transaction by default when one `SaveChangesAsync` affects multiple root documents;
MongoDB's [transaction documentation](https://www.mongodb.com/docs/v8.3/core/transactions/)
supports transactions across collections on replica sets. [Atlas can deploy a replica set on Azure](https://www.mongodb.com/docs/atlas/cli/current/command/atlas-clusters-create/).
The app requests majority write concern. The `_id` index on the outbox supports the relay's
ordered scans, while the SQL receipt primary key provides deduplication. Keep receipts for
as long as an event might be retried or restored.

RU-based [Cosmos DB for MongoDB 7.0](https://learn.microsoft.com/en-us/azure/cosmos-db/mongodb/feature-support-70)
does not support cross-collection transactions and is **not supported** by this outbox.
Microsoft's [Azure DocumentDB transaction guide](https://learn.microsoft.com/en-us/azure/documentdb/how-to-transactions)
shows multi-document transactions but does not establish this application's cross-collection
EF provider compatibility; use Atlas pending a separate compatibility test.

The SQL and MongoDB commits are not a single distributed transaction: committed MongoDB
events remain in the outbox until SQL delivery succeeds. Retries are idempotent per event,
but HTTP requests themselves have no idempotency key and retrying an ambiguous POST can
create a second entity. Events may arrive in SQL out of order. Failures are logged and retried; a permanently invalid event needs
operator investigation. MongoDB transaction errors may surface to the client without
automatic retries.
Existing audit events lost before this change cannot be recovered by the outbox.

**Verified locally:** the full test suite passed, including replica-set rollback and concurrent
SQL relay tests. The container image built and passed hadolint. With local MongoDB and SQL
containers and a temporary test OIDC issuer, the image passed liveness, readiness, 401,
authenticated create/read and audit-delivery checks. A local three-node replica-set
primary stepdown elected a new primary; a subsequent authenticated write and its audit
were successful. This is not an Azure or Entra test.

**Still unverified:** Real Entra tokens, Atlas on Azure, Azure SQL migration and deployment,
Azure failover, load, network policies, backups and alerts. Use the runbook below in a
non-production environment before considering production deployment.

### Azure and real-token verification runbook

These commands have not been run against Azure. They assume PowerShell 7, Azure CLI with
the Container Apps extension, Atlas CLI, .NET 10 with `dotnet-ef`, Docker, and `sqlcmd`.
Provision an Azure SQL database, an Azure Container Apps environment, an Azure Container
Registry and a healthy Atlas M10-or-higher replica set on Azure first. Ensure the app and
the migration host can reach both databases, pre-create `claims`, `covers` and `auditOutbox`,
and configure a user-assigned managed identity with ACR pull access. See the
[Container Apps deployment guide](https://learn.microsoft.com/en-us/azure/container-apps/tutorial-code-to-cloud).

1. Register an Entra API with delegated scopes `claims.read` and `claims.write` (or app
   roles `Claims.Read` and `Claims.Write`). Grant consent to the client used for testing.
   Record the tenant ID, API Application ID URI and token audience. For the 403 check,
   use a separate client or user whose token has read permission but **not** write permission.
2. Set these values in a trusted, non-production shell. Secrets entered through CLI
   arguments can be visible to local processes; prefer Key Vault references or a secured
   deployment pipeline for production. Do not commit credentials or tokens.

   ```powershell
   $tenant = '<tenant-id>'; $subscription = '<subscription-id>'
   $group = '<resource-group>'; $environment = '<container-app-environment>'
   $app = '<container-app-name>'; $acr = '<registry-name>'
   $identity = '<user-assigned-identity-resource-id>'
   $apiUri = 'api://<api-app-id>'; $audience = '<expected-access-token-audience>'
   $mongoDb = 'Claims'; $sqlServer = '<sql-server>.database.windows.net'
   $sqlDatabase = '<database>'; $sqlUser = '<sql-user>'
   $sql = Read-Host 'Azure SQL connection string for the app' -AsSecureString | ConvertFrom-SecureString -AsPlainText
   $mongo = Read-Host 'Atlas connection string for the app' -AsSecureString | ConvertFrom-SecureString -AsPlainText
   $migrationSql = Read-Host 'Azure SQL connection string with migration permissions' -AsSecureString | ConvertFrom-SecureString -AsPlainText
   az login --tenant $tenant
   az account set --subscription $subscription
   ```

3. Apply the migration once, before routing traffic. The migration identity needs schema
   permissions; the app identity should have only runtime permissions.

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = 'Production'
   $env:Database__AuditSqlServer = $migrationSql
   $env:Database__ClaimsMongoDb = $mongo
   $env:Database__MongoDatabaseName = $mongoDb
   $env:Authentication__Authority = "https://login.microsoftonline.com/$tenant/v2.0"
   $env:Authentication__Audience = $audience
   dotnet ef database update --project Claims --context AuditContext
   Remove-Item Env:\Database__AuditSqlServer, Env:\Database__ClaimsMongoDb
   ```

   Expect `Done` and the `AuditReceipts` table. Drain and stop any old in-memory-audit
   instances before switching traffic.
4. Build and deploy two or more replicas. This example uses Container Apps secrets;
   replace them with Key Vault references in a production deployment.

   ```powershell
   az acr build --registry $acr --image claims:verify .
   az containerapp create --name $app --resource-group $group --environment $environment `
     --image "${acr}.azurecr.io/claims:verify" --target-port 8080 --ingress external `
     --registry-server "${acr}.azurecr.io" --user-assigned $identity --registry-identity $identity `
     --min-replicas 2 --max-replicas 5 --secrets "sql=$sql" "mongo=$mongo" `
     --env-vars "ASPNETCORE_ENVIRONMENT=Production" "Database__AuditSqlServer=secretref:sql" `
       "Database__ClaimsMongoDb=secretref:mongo" "Database__MongoDatabaseName=$mongoDb" `
       "Authentication__Authority=https://login.microsoftonline.com/$tenant/v2.0" `
       "Authentication__Audience=$audience" --query properties.provisioningState -o tsv
   $fqdn = az containerapp show -g $group -n $app --query properties.configuration.ingress.fqdn -o tsv
   $base = "https://$fqdn"
   ```

   Expect a running app and two ready replicas. Configure platform HTTPS, health probes
   (`/health/live`, `/health/ready`) and network restrictions for your environment.
5. Test real tokens. Grant Azure CLI's client the API delegated permissions first; use
   separately scoped clients if consent results in a token containing both permissions.

   ```powershell
   $readToken = az account get-access-token --tenant $tenant --scope "$apiUri/claims.read" --query accessToken -o tsv
   $writeToken = az account get-access-token --tenant $tenant --scope "$apiUri/claims.write" --query accessToken -o tsv
   $readHeaders = @{ Authorization = "Bearer $readToken" }
   $writeHeaders = @{ Authorization = "Bearer $writeToken" }
   (Invoke-WebRequest "$base/health/live" -SkipHttpErrorCheck).StatusCode   # 200
   (Invoke-WebRequest "$base/health/ready" -SkipHttpErrorCheck).StatusCode  # 200
   (Invoke-WebRequest "$base/Covers" -SkipHttpErrorCheck).StatusCode        # 401
   (Invoke-WebRequest "$base/Covers" -Headers $readHeaders -SkipHttpErrorCheck).StatusCode # 200
   $start = [DateTime]::UtcNow.Date.AddDays(1)
   $body = @{ startDate = $start.ToString('yyyy-MM-dd'); endDate = $start.AddDays(2).ToString('yyyy-MM-dd'); type = 'Yacht' } | ConvertTo-Json
   (Invoke-WebRequest "$base/Covers" -Method Post -Headers $readHeaders -ContentType 'application/json' -Body $body -SkipHttpErrorCheck).StatusCode # 403
   $created = Invoke-RestMethod "$base/Covers" -Method Post -Headers $writeHeaders -ContentType 'application/json' -Body $body
   (Invoke-WebRequest "$base/Covers/$($created.id)" -Headers $readHeaders -SkipHttpErrorCheck).StatusCode # 200
   ```

   Check SQL after the relay runs. With SQL authentication, enter a password without
   echoing it; expect exactly one `POST` audit for the created cover.

   ```powershell
   $env:SQLCMDPASSWORD = Read-Host 'SQL password' -AsSecureString | ConvertFrom-SecureString -AsPlainText
   sqlcmd -S $sqlServer -d $sqlDatabase -U $sqlUser -Q "SELECT COUNT(*) AS AuditCount FROM CoverAudits WHERE CoverId='$($created.id)' AND HttpRequestType='POST'"
   Remove-Item Env:\SQLCMDPASSWORD
   ```

6. In the **non-production** Atlas project, use a healthy M10-or-higher cluster and an
   account with the required project role. Follow the
   [Atlas failover prerequisites](https://www.mongodb.com/docs/atlas/tutorial/test-resilience/test-primary-failover/):

   ```powershell
   atlas auth login
   atlas clusters failover '<atlas-cluster-name>' --projectId '<atlas-project-id>'
   ```

   Confirm a different primary in Atlas, wait for `/health/ready` to return 200, then
   repeat the authenticated create/read and SQL count check. During election a request
   may fail; do not blindly retry an ambiguous POST because it has no idempotency key.
   In staging, temporarily block the app's SQL access, create a cover, verify its pending
   `auditOutbox` document, restore access and confirm exactly one SQL audit row. Repeat
   with two replicas and restart one    during delivery. Clear tokens and secrets from the shell after the checks:

   ```powershell
   Remove-Variable sql,mongo,migrationSql,readToken,writeToken,readHeaders,writeHeaders
   Remove-Item Env:\ASPNETCORE_ENVIRONMENT,Env:\Database__MongoDatabaseName,Env:\Authentication__Authority,Env:\Authentication__Audience
   ```

---

## Design and business rules

The project is split into API, application, domain and infrastructure folders. Controllers
map HTTP to services; services use FluentValidation validators, a premium calculator and
repository/auditing interfaces. MongoDB mappings stay in infrastructure. Request DTOs prevent
clients from setting IDs and premiums. Swagger uses XML documentation.

Requests are validated with [FluentValidation](https://docs.fluentvalidation.net):
`CreateClaimRequestValidator` and `CreateCoverRequestValidator` are registered by
`AddApplication()`, and the services call `ValidateAndThrow(Async)` before doing any work. A
`ValidationException` is mapped to `400 application/problem+json` with all errors, grouped by
field. The claim validator is async because it loads the cover (once) to check the cover rules;
the cover validator gets "today" from the injected `TimeProvider`.

| Entity | Rule | Implementation |
|---|---|---|
| Claim | DamageCost ≤ 100 000 | Also rejects negative values. |
| Claim | Created within the cover period | `StartDate.Date ≤ Created.Date ≤ EndDate.Date`; an unknown `CoverId` is a 400 too. |
| Claim | Name and CoverId required, valid Type | Added rules: empty values and undefined enum numbers are rejected. |
| Cover | StartDate not in the past | Compared by date against "today" from the injected `TimeProvider` (UTC). |
| Cover | Period ≤ 1 year | `EndDate ≤ StartDate.AddYears(1)`; additionally `EndDate > StartDate`. |
| Cover | Valid Type | Added rule: undefined enum numbers are rejected. |

The `/Covers/compute` endpoint runs the same cover validation, so it cannot quote a premium for a
cover that could never be created.

`PremiumCalculator` replaces overlapping tier checks that charged days more than once,
made days after 365 free, and rounded fractional days up. It retains the original
surcharges and applies these discounts:

```
day rate = 1250 × surcharge      (Yacht 1.10, Passenger ship 1.20, Tanker 1.50, other 1.30)

days   1–30   : day rate × 1.00
days  31–180  : day rate × 0.95 (Yacht)  | 0.98 (other)
days 181+     : day rate × 0.92 (Yacht)  | 0.97 (other)
```

The additional discount is interpreted cumulatively. Premium dates are end-exclusive,
while claim dates include both cover endpoints. Today is UTC, a year is a calendar year,
and deleting a missing item is still audited. Premium rounding is not specified.

**Review first:** confirm the discount and date assumptions with the business; review the
outbox transaction and SQL receipt handling under failures. The `AddAuditReceipts` SQL
migration adds the deduplication table; earlier audit entity nullability was corrected to
match the original schema.

Unit tests cover pricing, validation, services, configuration, outbox staging and
authorization. Integration tests exercise the HTTP API, audit delivery, transaction
rollback and concurrent relay attempts with MongoDB and SQL Server containers.

## Logging and tracing

The application writes JSON to stdout. Every log has a `TraceId`: request logs use the
W3C trace ID returned in ProblemDetails, while startup and background logs use a
process-level correlation ID when no request activity exists. Audit staging logs include
the generated `AuditId`, which links the request to later relay failures. `Information`
covers startup, migrations, mutations and relay batches; `Warning` covers validation
and authorization failures; `Error` covers unexpected failures and relay retries.
Individual audit delivery and duplicate-skip events are `Debug` (off by default).

Logs omit request bodies, tokens, connection strings and exception messages. To avoid
exposing framework-supplied data, framework events retain only category, event ID and
exception type; application failures record the exception type rather than its message.
The JSON formatter retains trace, span and request scopes, not arbitrary scope values.

For Container Apps logs sent to Log Analytics, find a request or audit event with:

```kusto
ContainerAppConsoleLogs_CL
| extend event = parse_json(Log_s)
| where tostring(event.TraceId) == "<trace-id>" or tostring(event.State.AuditId) == "<audit-id>"
| project TimeGenerated, Level = event.LogLevel, Category = event.Category, Message = event.Message, State = event.State
| order by TimeGenerated asc
```

The workspace must be configured to collect console logs; Application Insights is not
configured by this project.
