using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Claims.Tests.Unit;

public class AuthenticationTests
{
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("only-for-tests-not-a-real-signing-key-123"));
    private const string Issuer = "https://issuer.example.test";
    private const string Audience = "test-api";

    [Fact]
    public async Task Endpoints_require_a_valid_token_and_the_right_permission()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:AuditSqlServer"] = "Server=127.0.0.1,1;Database=Audit;User Id=test;Password=test",
                    ["Database:ClaimsMongoDb"] = "mongodb://127.0.0.1:1",
                    ["Authentication:Authority"] = Issuer,
                    ["Authentication:Audience"] = Audience
                }));
            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
                {
                    jwt.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    jwt.Configuration.SigningKeys.Add(Key);
                    jwt.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(jwt.Configuration);
                    jwt.TokenValidationParameters.ValidIssuer = Issuer;
                    jwt.TokenValidationParameters.ValidAudience = Audience;
                }));
        });
        using var client = factory.CreateClient();
        var cancellation = TestContext.Current.CancellationToken;
        var start = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var end = DateTime.UtcNow.Date.AddDays(2).ToString("yyyy-MM-dd");
        var quote = $"/Covers/compute?startDate={start}&endDate={end}&coverType=Yacht";

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(quote, null, cancellation)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(quote, null, cancellation)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("unrelated"));
        var forbidden = await client.PostAsync(quote, null, cancellation);
        Assert.True(forbidden.StatusCode == HttpStatusCode.Forbidden,
            $"{forbidden.StatusCode}: {string.Join(", ", forbidden.Headers.WwwAuthenticate)}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("claims.read"));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(quote, null, cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/Covers/example", cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", cancellation)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("claims.write"));
        var invalidCover = new { StartDate = "2000-01-01", EndDate = "2000-01-02", Type = "Yacht" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/Covers", invalidCover, cancellation)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Claims.Read", "roles"));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(quote, null, cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/Covers/example", cancellation)).StatusCode);
    }

    private static string CreateToken(string permission, string claimType = "scp") => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityToken(Issuer, Audience, [new Claim(claimType, permission)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256)));
}
