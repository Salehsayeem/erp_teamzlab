using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Erp.Api.Tests;

// Owner-approved API explorer (Swashbuckle): the OpenAPI document must describe the real routes
// (typed *Request records give accurate request schemas) and the UI must serve, with the Bearer
// stub-auth scheme preset so the Authorize button works.
public class SwaggerDocsTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;
    public SwaggerDocsTests(TestApiFactory factory) => _factory = factory;

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        var client = _factory.CreateClient();
        await using var stream = await client.GetStreamAsync(path);
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Openapi_document_lists_real_routes()
    {
        var doc = await GetJsonAsync("/swagger/v1/swagger.json");
        Assert.Equal("erp_teamzlab API", doc.GetProperty("info").GetProperty("title").GetString());
        var paths = doc.GetProperty("paths");
        foreach (var route in new[] { "/api/v1/suppliers", "/api/v1/purchase-orders", "/api/v1/receipts", "/api/v1/stock/transfers", "/api/v1/auth/token-exchange" })
            Assert.True(paths.TryGetProperty(route, out _), $"missing route {route} in swagger.json");
    }

    [Fact]
    public async Task Bearer_stub_auth_scheme_is_documented()
    {
        var doc = await GetJsonAsync("/swagger/v1/swagger.json");
        var scheme = doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task Swagger_ui_is_served()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/swagger/index.html");
        Assert.True(res.IsSuccessStatusCode, $"swagger UI returned {(int)res.StatusCode}");
    }
}
