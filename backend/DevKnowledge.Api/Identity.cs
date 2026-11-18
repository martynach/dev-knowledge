using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using DevKnowledge.Domain;
using DevKnowledge.Infrastructure;

namespace DevKnowledge.Api;

public sealed class DevelopmentAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id = Request.Headers["X-Dev-User"].ToString();
        if (!new[] { "admin", "editor", "other", "reader" }.Contains(id)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim("sub", id), new Claim("iss", "local"), new Claim("name", id)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

public sealed class IdentityService(IStore store, IConfiguration config, IWebHostEnvironment environment)
{
    public static string Subject(ClaimsPrincipal user) => user.FindFirstValue("iss") + "|" + user.FindFirstValue("sub");
    public async Task<string?> Role(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true) return null;
        var subject = Subject(user);
        if (subject == config["Auth:AdminSubject"]) return "Admin";
        if (environment.IsDevelopment() && config.GetValue<bool>("Auth:Development") && subject is "local|editor" or "local|other") return "Editor";
        return (await store.Get<RoleGrant>("roles", subject, ct))?.Role;
    }
}
