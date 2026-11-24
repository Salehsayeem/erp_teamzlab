using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Erp.Api.Tests;

// Strict-CORS review findings on Program.cs policy setup:
// 1. CorsOrigins set-but-EMPTY ("CorsOrigins=" in .env) must fall back to the dev default — an
//    empty origin list silently blocks every browser cross-origin call with the same symptom the
//    default exists to prevent.
// 2. An origin configured with a trailing slash must still match the Origin header browsers send
//    (the fetch spec never sends a trailing slash).
// Each case boots the real pipeline through a throwaway-database WebApplicationFactory with a
// per-case CorsOrigins host setting and asserts only on the Access-Control-Allow-Origin header of
// an OPTIONS preflight: the policy answers before auth/DB, so tests stay independent of database
// content.
public class CorsOriginTests
{
    private static async Task<string?> PreflightAllowOrigin(string corsOrigins, string origin)
    {
        using var factory = new CorsOriginFactory(corsOrigins);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/customers");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        using var response = await client.SendAsync(request);
        return response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            ? values.FirstOrDefault()
            : null;
    }

    [Fact]
    public async Task Empty_CorsOrigins_falls_back_to_the_dev_default()
    {
        var allow = await PreflightAllowOrigin("", "http://localhost:3000");
        Assert.Equal("http://localhost:3000", allow);
    }

    [Fact]
    public async Task Configured_origin_with_trailing_slash_still_matches_the_browser_origin()
    {
        var allow = await PreflightAllowOrigin("http://localhost:3000/", "http://localhost:3000");
        Assert.Equal("http://localhost:3000", allow);
    }

    [Fact]
    public async Task Unrelated_origin_is_never_allowed()
    {
        var allow = await PreflightAllowOrigin("https://evil.example", "http://localhost:3000");
        Assert.Null(allow);
    }

    // TestApiFactory pins the throwaway database; this subclass adds the per-case CorsOrigins host
    // setting the strict-CORS policy in Program.cs reads.
    private sealed class CorsOriginFactory(string corsOrigins) : TestApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("CorsOrigins", corsOrigins);
        }
    }
}
