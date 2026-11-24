using System.Text.Json.Serialization;
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
var corsOrigins = (builder.Configuration["CorsOrigins"] ?? "http://localhost:3000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));
var app = builder.Build();

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ErrorMiddleware>();
app.UseMiddleware<BindingEnvelopeMiddleware>();
app.UseCors(); // before AuthMiddleware — browser preflight OPTIONS carries no Authorization header
app.UseMiddleware<AuthMiddleware>();
app.MapControllers();

app.Run();

public partial class Program; // REQUIRED — WebApplicationFactory<Program> depends on it
