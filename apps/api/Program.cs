using System.Text.Json.Serialization;
using Erp.Api.Middleware;
using Erp.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddErpServices(); // Db.I + all IX → X pairs (singletons)
// Envelope.Ok/Fail and Idem.Wrap still respond via Results.Json, which reads Http.Json options —
// keep the enum-string converter here so response bodies stay byte-identical
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// minimal APIs bound missing JSON fields as nulls and let Guards decide; [ApiController] would
// short-circuit them with a ProblemDetails 400 — suppress that so validation stays in one place
builder.Services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var app = builder.Build();

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ErrorMiddleware>();
app.UseMiddleware<BindingEnvelopeMiddleware>();
app.UseMiddleware<AuthMiddleware>();
app.MapControllers();

app.Run();

public partial class Program; // REQUIRED — WebApplicationFactory<Program> depends on it
