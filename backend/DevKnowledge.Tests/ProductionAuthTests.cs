using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using DevKnowledge.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DevKnowledge.Tests;

public class ProductionAuthTests
{
    private const string Issuer = "https://identity.example.test/tenant/v2.0";
    private const string Audience = "devknowledge-api";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("isolated-test-signing-key-at-least-32-bytes-long"));
    private sealed class Factory(bool unsafeAuth = false, bool unsafeStorage = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            foreach (var setting in new Dictionary<string, string> { ["Auth:Development"] = unsafeAuth.ToString(), ["Storage:Local"] = unsafeStorage.ToString(), ["Auth:Issuer"] = Issuer, ["Auth:Audience"] = Audience, ["Auth:AdminSubject"] = Issuer + "|admin" }) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStore>();
                // Inject isolated persistence directly; production configuration cannot select this adapter.
                services.AddSingleton<IStore>(new LocalStore(Path.Combine(Path.GetTempPath(), "unused-" + Guid.NewGuid() + ".json")));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    options.Configuration.SigningKeys.Add(Key);
                    options.ConfigurationManager = null;
                    options.TokenValidationParameters.IssuerSigningKey = Key;
                });
            });
        }
    }
    private static string Token(string subject = "member", string issuer = Issuer, string audience = Audience, bool expired = false, SecurityKey? key = null) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience, [new Claim("sub", subject), new Claim("name", "Test member"), new Claim("role", "Admin")], DateTime.UtcNow.AddHours(-2), expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(10), new SigningCredentials(key ?? Key, SecurityAlgorithms.HmacSha256)));
    [Fact]
    public async Task ValidatedSubjectBootstrapsAdminButTokenRoleDoesNotGrantRights()
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        var member = await client.GetStringAsync("/api/me"); Assert.Contains("\"role\":null", member);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("admin"));
        Assert.Contains("\"role\":\"Admin\"", await client.GetStringAsync("/api/me"));
    }
    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("signature")]
    [InlineData("malformed")]
    public async Task RejectsInvalidRealBearerTokens(string failure)
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        var token = failure switch { "issuer" => Token(issuer: "https://wrong.example.test"), "audience" => Token(audience: "other-api"), "expired" => Token(expired: true), "signature" => Token(key: new SymmetricSecurityKey(Encoding.UTF8.GetBytes("different-isolated-test-signing-key-32-bytes"))), _ => "not-a-jwt" };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }
    [Fact]
    public async Task ProductionIgnoresDevelopmentHeaders()
    {
        using var factory = new Factory(); using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Dev-User", "admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ProductionFailsClosedForDevelopmentAdapters(bool auth, bool storage)
    {
        using var factory = new Factory(auth, storage);
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }
}
