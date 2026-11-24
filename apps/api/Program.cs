using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using Erp.Api;
using Erp.Api.Data;
using Erp.Api.Middleware;
using Erp.Api.Services;
using Microsoft.EntityFrameworkCore;

EnvLoader.Load(); // .env (resolved upward from cwd) → process env; missing file = no-op (task-2 §1)

var builder = WebApplication.CreateBuilder(args);
// connection string name Default — assembled from PG* env vars unless a fixture/test supplies an
// explicit ConnectionStrings:Default override. Npgsql connects lazily: the API boots with no DB touched.
var connectionString = builder.Configuration.GetConnectionString("Default") is { Length: > 0 } explicitCs
    ? explicitCs
    : DbConfig.Assemble(key => builder.Configuration[key!]);
builder.Services.AddDbContext<ErpDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddErpServices(); // scoped repos + services behind the Task-1 interfaces
// Envelope.Ok/Fail and Idem.Wrap still respond via Results.Json, which reads Http.Json options —
// keep the enum-string converter here so response bodies stay byte-identical
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// minimal APIs bound missing JSON fields as nulls and let Guards decide; [ApiController] would
// short-circuit them with a ProblemDetails 400 — suppress that so validation stays in one place
builder.Services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Strict CORS (CLAUDE.md security rules): only listed browser origins may call the API.
// Default covers the dev frontend; override with the CorsOrigins env var (comma-separated).
// Null OR set-but-empty/whitespace both fall back to the default (CorsOrigins= in .env must not
// silently block every browser call), and trailing slashes are trimmed because Origin headers
// never carry one (fetch spec).
var corsConfig = builder.Configuration["CorsOrigins"];
var corsOrigins = (string.IsNullOrWhiteSpace(corsConfig) ? "http://localhost:3000" : corsConfig)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(o => o.TrimEnd('/'))
    .ToArray();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));
// Swagger UI (owner-approved Swashbuckle): typed *Request records give accurate request schemas;
// the Bearer scheme presets the Authorize button with the stub-auth token (= seeded user id).
// Served in every environment for now — revisit when a hardened prod profile exists.
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "erp_teamzlab API",
        Version = "v1",
        Description = "Envelope everywhere: { success: true, data } | { success: false, error: { code, message } }.",
    });
    o.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.SecuritySchemeType.Http,
        Scheme = "bearer",
        Description = "Stub auth: POST /api/v1/auth/token-exchange, or use a seeded id directly — U-001 admin, U-002 procurement, U-003 finance, U-004 sales.",
    });
    o.AddSecurityRequirement(doc => new Microsoft.OpenApi.OpenApiSecurityRequirement
    {
        [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", doc)] = [],
    });
});
var app = builder.Build();

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ErrorMiddleware>();
app.UseMiddleware<BindingEnvelopeMiddleware>();
app.UseCors(); // before AuthMiddleware — browser preflight OPTIONS carries no Authorization header
app.UseMiddleware<AuthMiddleware>();
app.UseSwagger();
app.UseSwaggerUI(o => o.DocumentTitle = "erp_teamzlab API");
app.MapControllers();

app.Run();

public partial class Program; // REQUIRED — WebApplicationFactory<Program> depends on it
