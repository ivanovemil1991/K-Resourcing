using System.Text.Json.Serialization;
using System.Diagnostics;
using Claims.Api;
using Claims.Application;
using Claims.Infrastructure;
using Claims.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using ApiAuthenticationOptions = Claims.Api.AuthenticationOptions;

var builder = WebApplication.CreateBuilder(args);

// Claims are small JSON documents: hide the server banner and refuse oversized bodies early.
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 64 * 1024;
});

DatabaseConnections? localDatabases = null;
builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection("Database"))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<EncryptionOptions>, EncryptionOptionsValidator>();
builder.Services.AddOptions<EncryptionOptions>()
    .Bind(builder.Configuration.GetSection("Encryption"))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ApiAuthenticationOptions>, AuthenticationOptionsValidator>();
builder.Services.AddOptions<ApiAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection("Authentication"))
    .ValidateOnStart();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<ApiAuthenticationOptions>>((jwt, configured) =>
    {
        jwt.Authority = configured.Value.Authority;
        jwt.Audience = configured.Value.Audience;
        jwt.MapInboundClaims = false;
    });
builder.Services.AddAuthorization();
builder.Services.AddOptions<AuthorizationOptions>()
    .Configure<IOptions<ApiAuthenticationOptions>>((authorization, configured) =>
    {
        void AddPolicy(string name, bool write) => authorization.AddPolicy(name, policy =>
        {
            if (configured.Value.Enabled)
                policy.RequireAuthenticatedUser().RequireAssertion(context => AuthorizationPolicies.HasPermission(context.User, write));
            else
                policy.RequireAssertion(_ => true);
        });
        AddPolicy(AuthorizationPolicies.Read, false);
        AddPolicy(AuthorizationPolicies.Write, true);
    });
builder.Services.AddSingleton(services =>
{
    var options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    return options.UseLocalContainers
        ? localDatabases ?? throw new InvalidOperationException("Local databases have not started.")
        : new DatabaseConnections(options.AuditSqlServer, options.ClaimsMongoDb);
});
builder.Logging.ClearProviders();
builder.Logging.Configure(options => options.ActivityTrackingOptions =
    ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
builder.Logging.AddConsole(options => options.FormatterName = "trace-json");
builder.Logging.AddConsoleFormatter<TraceJsonFormatter, ConsoleFormatterOptions>(options => options.IncludeScopes = true);

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] =
        Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "Claims.xml"));
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

builder.Services.AddRequestRateLimiting();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration["Redis:ConnectionString"]);
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("databases", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();
var databaseOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
var authenticationOptions = app.Services.GetRequiredService<IOptions<ApiAuthenticationOptions>>().Value;
app.Logger.LogInformation("Starting application; local databases: {LocalDatabases}, audit migration: {MigrateAudit}, authentication: {Authentication}",
    databaseOptions.UseLocalContainers, databaseOptions.MigrateAuditOnStart, authenticationOptions.Enabled);
if (databaseOptions.UseLocalContainers)
    localDatabases = await LocalDatabaseContainers.StartAsync();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next();
});
app.Use(async (context, next) =>
{
    await next();
    if (context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        app.Logger.LogWarning("Authorization denied with status {StatusCode}", context.Response.StatusCode);
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Outside Development the redirect is only active when an HTTPS port is configured (ASPNETCORE_HTTPS_PORT),
// so a TLS-terminating ingress that talks plain HTTP to the container does not cause a redirect loop.
if (app.Environment.IsDevelopment() || app.Configuration["HTTPS_PORT"] is not null)
    app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (databaseOptions.MigrateAuditOnStart)
{
    app.Logger.LogInformation("Applying audit database migrations");
    await app.Services.MigrateAuditDatabaseAsync();
    app.Logger.LogInformation("Audit database migrations applied");
}

app.Run();

/// <summary>Application entry point exposed to integration tests.</summary>
public partial class Program;
