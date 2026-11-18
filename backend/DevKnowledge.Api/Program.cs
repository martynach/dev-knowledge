using System.Security.Claims;
using System.Threading.RateLimiting;
using DevKnowledge.Api;
using DevKnowledge.Domain;
using DevKnowledge.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var devAuth = builder.Configuration.GetValue<bool>("Auth:Development");
var local = builder.Configuration.GetValue<bool>("Storage:Local");
if (!builder.Environment.IsDevelopment() && (devAuth || local)) throw new InvalidOperationException("Local authentication and storage are forbidden outside Development.");
if (string.IsNullOrWhiteSpace(builder.Configuration["Auth:AdminSubject"])) throw new InvalidOperationException("Configure Auth:AdminSubject as issuer|subject from a verified identity.");
builder.Services.AddProblemDetails();
builder.Services.AddAuthorization();
builder.Services.AddExceptionHandler(options => options.ExceptionHandler = async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var status = error is BadHttpRequestException bad ? bad.StatusCode : 500;
    await Results.Problem(statusCode: status, title: status == 400 ? "Invalid request body." : "The request could not be completed.").ExecuteAsync(context);
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 700_000);
if (devAuth) builder.Services.AddAuthentication("Development").AddScheme<AuthenticationSchemeOptions, DevelopmentAuth>("Development", _ => { });
else
{
    var issuer = builder.Configuration["Auth:Issuer"] ?? throw new InvalidOperationException("Auth:Issuer required.");
    var audience = builder.Configuration["Auth:Audience"] ?? throw new InvalidOperationException("Auth:Audience required.");
    if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidOperationException("Issuer must be HTTPS.");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.Authority = issuer;
        options.Audience = audience;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = issuer, ValidateAudience = true, ValidAudience = audience, ValidateLifetime = true, ValidateIssuerSigningKey = true, RequireSignedTokens = true, RequireExpirationTime = true, ClockSkew = TimeSpan.FromMinutes(1), NameClaimType = "name" };
        options.Events = new JwtBearerEvents { OnTokenValidated = context => { if (string.IsNullOrEmpty(context.Principal?.FindFirstValue("sub"))) context.Fail("Missing subject."); return Task.CompletedTask; } };
    });
}
builder.Services.AddSingleton<IStore>(_ => local ? new LocalStore(builder.Configuration["Storage:Path"] ?? "data/local.json") : new CosmosStore(builder.Configuration["Cosmos:Endpoint"] ?? throw new InvalidOperationException("Cosmos endpoint required."), builder.Configuration["Cosmos:Database"] ?? "devknowledge"));
builder.Services.AddScoped<IdentityService>();
builder.Services.AddCors(options => options.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, ct) => { context.HttpContext.Response.Headers.RetryAfter = "60"; await Results.Problem(statusCode: 429, title: "Too many requests. Try again later.").ExecuteAsync(context.HttpContext); };
    options.AddPolicy("applications", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages(async context => await Results.Problem(statusCode: context.HttpContext.Response.StatusCode).ExecuteAsync(context.HttpContext));
app.Use(async (context, next) => { context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["Referrer-Policy"] = "no-referrer"; if (context.Request.Path.StartsWithSegments("/api/me") || context.Request.Path.StartsWithSegments("/api/admin")) context.Response.Headers.CacheControl = "no-store"; await next(); });
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/articles", async (IStore store, string? search, string? tag, int? page, CancellationToken ct) =>
{
    if (page is < 1 or > 10000 || search?.Length > 200 || tag?.Length > 24) return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid page, search or tag."] });
    var result = await store.Browse(page ?? 1, search, tag, null, ct);
    return Results.Ok(new { items = result.Items.Select(a => new { a.Id, a.Title, a.Tags, a.AuthorId, a.AuthorName, a.PublishedAt }), total = result.Total, page = page ?? 1, pageSize = 10, limited = result.Limited });
});
app.MapGet("/api/articles/{id}", async (string id, IStore store, CancellationToken ct) => await store.Get<Article>("articles", id, ct) is { } article ? Results.Ok(article) : Results.NotFound());
app.MapGet("/api/me", async (ClaimsPrincipal user, IdentityService identity, CancellationToken ct) => Results.Ok(new { subject = IdentityService.Subject(user), name = user.FindFirstValue("name") ?? "Member", role = await identity.Role(user, ct) })).RequireAuthorization();
app.MapGet("/api/me/articles", async (ClaimsPrincipal user, IdentityService identity, IStore store, int? page, CancellationToken ct) =>
{
    if (!Rules.CanWrite(await identity.Role(user, ct))) return Results.Forbid();
    if (page is < 1 or > 10000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["page"] = ["Invalid page."] });
    var result = await store.Browse(page ?? 1, null, null, IdentityService.Subject(user), ct);
    return Results.Ok(new { items = result.Items, total = result.Total, page = page ?? 1, pageSize = 10, limited = false });
}).RequireAuthorization();
app.MapPost("/api/articles", async (ArticleInput input, ClaimsPrincipal user, IdentityService identity, IStore store, CancellationToken ct) =>
{
    if (!Rules.CanWrite(await identity.Role(user, ct))) return Results.Forbid();
    var errors = Rules.Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
    var now = DateTimeOffset.UtcNow;
    var article = new Article(Guid.NewGuid().ToString("N"), input.Title.Trim(), input.Markdown, input.Tags.Distinct().ToArray(), IdentityService.Subject(user), user.FindFirstValue("name") ?? "Member", now, now);
    await store.Put("articles", article.Id, article, ct);
    return Results.Created($"/api/articles/{article.Id}", article);
}).RequireAuthorization();
app.MapPut("/api/articles/{id}", async (string id, ArticleInput input, ClaimsPrincipal user, IdentityService identity, IStore store, CancellationToken ct) =>
{
    var role = await identity.Role(user, ct); if (!Rules.CanWrite(role)) return Results.Forbid();
    var article = await store.Get<Article>("articles", id, ct); if (article is null) return Results.NotFound();
    if (!Rules.CanEdit(article, IdentityService.Subject(user), role)) return Results.Forbid();
    var errors = Rules.Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
    article = article with { Title = input.Title.Trim(), Markdown = input.Markdown, Tags = input.Tags.Distinct().ToArray(), UpdatedAt = DateTimeOffset.UtcNow };
    await store.Put("articles", id, article, ct); return Results.Ok(article);
}).RequireAuthorization();
app.MapDelete("/api/articles/{id}", async (string id, ClaimsPrincipal user, IdentityService identity, IStore store, CancellationToken ct) =>
{
    var role = await identity.Role(user, ct); if (!Rules.CanWrite(role)) return Results.Forbid();
    var article = await store.Get<Article>("articles", id, ct); if (article is null) return Results.NotFound();
    if (!Rules.CanEdit(article, IdentityService.Subject(user), role)) return Results.Forbid();
    await store.Delete("articles", id, ct); return Results.NoContent();
}).RequireAuthorization();
app.MapPost("/api/editor-applications", async (ApplicationInput input, IStore store, CancellationToken ct) =>
{
    var errors = Rules.Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
    if (string.IsNullOrEmpty(input.Website)) { var application = new EditorialApplication(Guid.NewGuid().ToString("N"), input.Name.Trim(), input.Email, input.Motivation, "Pending", DateTimeOffset.UtcNow); await store.Put("applications", application.Id, application, ct); }
    return Results.Accepted(value: new { message = "Thank you. Your application has been received for review." });
}).RequireRateLimiting("applications");
app.MapGet("/api/admin/editor-applications", async (ClaimsPrincipal user, IdentityService identity, IStore store, CancellationToken ct) => await identity.Role(user, ct) == "Admin" ? Results.Ok(await store.List<EditorialApplication>("applications", 200, ct)) : Results.Forbid()).RequireAuthorization();
app.MapPost("/api/admin/editor-applications/{id}/approve", async (string id, ApprovalInput input, ClaimsPrincipal user, IdentityService identity, IStore store, CancellationToken ct) =>
{
    if (await identity.Role(user, ct) != "Admin") return Results.Forbid();
    // Manual fallback: admin independently verifies the applicant's issuer + subject; email is never a grant key.
    if (string.IsNullOrWhiteSpace(input.Subject) || input.Subject.Length > 600 || !input.Subject.Contains('|') || !input.Subject.StartsWith(devAuth ? "local|" : builder.Configuration["Auth:Issuer"] + "|", StringComparison.Ordinal)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["subject"] = ["Enter the independently verified issuer|subject from this identity tenant."] });
    var application = await store.Get<EditorialApplication>("applications", id, ct); if (application is null) return Results.NotFound();
    if (application.Status != "Pending" && application.ApprovedSubject != input.Subject) return Results.Conflict();
    var existing = await store.Get<RoleGrant>("roles", input.Subject, ct);
    if (existing?.Role != "Admin") await store.Put("roles", input.Subject, new RoleGrant(input.Subject, "Editor"), ct);
    await store.Put("applications", id, application with { Status = "Approved", ApprovedSubject = input.Subject }, ct);
    app.Logger.LogInformation("Application {ApplicationId} approved by {Subject}", id, IdentityService.Subject(user));
    return Results.NoContent();
}).RequireAuthorization();
app.MapPost("/api/admin/editor-applications/{id}/reject", async (string id, ClaimsPrincipal user, IdentityService identity, IStore store, CancellationToken ct) =>
{
    if (await identity.Role(user, ct) != "Admin") return Results.Forbid();
    var application = await store.Get<EditorialApplication>("applications", id, ct); if (application is null) return Results.NotFound();
    if (application.Status != "Pending") return Results.Conflict();
    await store.Put("applications", id, application with { Status = "Rejected" }, ct); return Results.NoContent();
}).RequireAuthorization();
if (local && builder.Configuration.GetValue<bool>("Storage:Seed"))
{
    var store = app.Services.GetRequiredService<IStore>();
    if (await store.Get<Article>("articles", "welcome", CancellationToken.None) is null)
        await store.Put("articles", "welcome", new Article("welcome", "Build knowledge that lasts", "# Welcome to DevKnowledge\n\nA shared library for the things we learn while building software.\n\n## Start with a small idea\n\nWrite clear explanations, share working examples, and help the next developer.\n\n```csharp\nvar knowledge = await LearnAsync();\nConsole.WriteLine(knowledge);\n```\n\n## Make it useful\n\nUse tags to connect ideas. Every article is public as soon as you publish it.", ["getting-started", "dotnet"], "local|editor", "editor", DateTimeOffset.Parse("2026-11-01T12:00:00Z"), DateTimeOffset.Parse("2026-11-01T12:00:00Z")), CancellationToken.None);
}
app.Run();
public partial class Program;

