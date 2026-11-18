using System.Net;
using System.Net.Http.Json;
using System.Text;
using DevKnowledge.Domain;
using DevKnowledge.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DevKnowledge.Tests;

public sealed class ProductTests
{
    private sealed class Host : WebApplicationFactory<Program>
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "devknowledge-" + Guid.NewGuid() + ".json");
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Development"] = "true", ["Auth:AdminSubject"] = "local|admin", ["Storage:Local"] = "true", ["Storage:Seed"] = "false", ["Storage:Path"] = path }));
        public HttpClient As(string? user = null) { var client = CreateClient(); if (user is not null) client.DefaultRequestHeaders.Add("X-Dev-User", user); return client; }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (File.Exists(path)) File.Delete(path); }
    }
    private static ArticleInput Input(string title = "A useful article") => new(title, "# Heading\n\nUseful content", ["dotnet"]);
    [Fact]
    public void ValidationAndOwnershipRules()
    {
        Assert.Empty(Rules.Validate(Input()));
        Assert.NotEmpty(Rules.Validate(Input() with { Title = " " }));
        Assert.NotEmpty(Rules.Validate(Input() with { Markdown = new string('é', 51201) }));
        Assert.Empty(Rules.Validate(Input() with { Markdown = new string('é', 51200) }));
        Assert.NotEmpty(Rules.Validate(Input() with { Tags = ["INVALID"] }));
        Assert.NotEmpty(Rules.Validate(Input() with { Tags = ["a", "b", "c", "d", "e", "f"] }));
        var article = new Article("id", "title", "text", [], "owner", "Owner", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Assert.True(Rules.CanEdit(article, "owner", "Editor"));
        Assert.True(Rules.CanEdit(article, "other", "Admin"));
        Assert.False(Rules.CanEdit(article, "other", "Editor"));
        Assert.False(Rules.CanEdit(article, "owner", null));
        Assert.False(Rules.CanWrite("Visitor"));
    }
    [Fact]
    public async Task HttpAuthorizationCrudAndProblemDetails()
    {
        using var host = new Host(); using var visitor = host.As(); using var reader = host.As("reader"); using var owner = host.As("editor"); using var other = host.As("other"); using var admin = host.As("admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PostAsJsonAsync("/api/articles", Input())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/articles", Input())).StatusCode);
        var bad = await owner.PostAsJsonAsync("/api/articles", Input() with { Title = "" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); Assert.Equal("application/problem+json", bad.Content.Headers.ContentType?.MediaType);
        var created = await owner.PostAsJsonAsync("/api/articles", Input()); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var article = (await created.Content.ReadFromJsonAsync<Article>())!; var url = "/api/articles/" + article.Id;
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync(url, Input("Stolen"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(url, Input("Owner edit"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(url, Input("Admin edit"))).StatusCode);
        Assert.Equal("local|editor", (await visitor.GetFromJsonAsync<Article>(url))!.AuthorId);
        Assert.Equal(1, (await owner.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me/articles")).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync(url, Input())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/me/articles")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/articles", new StringContent("{bad", Encoding.UTF8, "application/json"))).StatusCode);
    }
    [Fact]
    public async Task BootstrapAndSecureApplicationWorkflow()
    {
        using var host = new Host(); using var visitor = host.As(); using var reader = host.As("reader"); using var editor = host.As("editor"); using var admin = host.As("admin");
        var me = await admin.GetStringAsync("/api/me"); Assert.Contains("Admin", me);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/admin/editor-applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync("/api/admin/editor-applications")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.PostAsJsonAsync("/api/editor-applications", new ApplicationInput("", "bad", "", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await visitor.PostAsJsonAsync("/api/editor-applications", new ApplicationInput("Reader", "reader@example.test", "I write about .NET.", null))).StatusCode);
        var application = Assert.Single((await admin.GetFromJsonAsync<EditorialApplication[]>("/api/admin/editor-applications"))!);
        var url = $"/api/admin/editor-applications/{application.Id}/approve";
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsJsonAsync(url, new ApprovalInput("local|reader"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(url, new ApprovalInput("reader@example.test"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(url, new ApprovalInput("other-tenant|reader"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(url, new ApprovalInput("local|reader"))).StatusCode);
        Assert.Contains("Editor", await reader.GetStringAsync("/api/me"));
        Assert.Equal(HttpStatusCode.Created, (await reader.PostAsJsonAsync("/api/articles", Input())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/editor-applications/{application.Id}/reject", new { })).StatusCode);
        await visitor.PostAsJsonAsync("/api/editor-applications", new ApplicationInput("Another", "a@example.test", "Learning", null));
        var pending = (await admin.GetFromJsonAsync<EditorialApplication[]>("/api/admin/editor-applications"))!.Single(a => a.Status == "Pending");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/editor-applications/{pending.Id}/reject", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/editor-applications/{pending.Id}/approve", new ApprovalInput("local|other"))).StatusCode);
    }
    [Fact]
    public async Task PaginationSearchTagsAndBounds()
    {
        using var host = new Host(); using var editor = host.As("editor");
        for (var i = 0; i < 12; i++) await editor.PostAsJsonAsync("/api/articles", Input("Article " + i));
        var page = await editor.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/articles?page=2");
        Assert.Equal(12, page.GetProperty("total").GetInt32()); Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        var search = await editor.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/articles?search=ARTICLE%201&tag=dotnet"); Assert.Equal(3, search.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.GetAsync("/api/articles?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/articles", Input() with { Markdown = new string('é', 51201) })).StatusCode);
    }
    [Fact]
    public async Task HoneypotRateLimitAndNoPublicApplicationData()
    {
        using var host = new Host(); using var visitor = host.As(); using var admin = host.As("admin");
        var input = new ApplicationInput("Bot", "bot@example.test", "Spam", "https://bot.test");
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Accepted, (await visitor.PostAsJsonAsync("/api/editor-applications", input)).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<EditorialApplication[]>("/api/admin/editor-applications"))!);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await visitor.PostAsJsonAsync("/api/editor-applications", input)).StatusCode);
    }
    [Fact]
    public async Task LocalPersistenceSurvivesAdapterRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try { await new LocalStore(path).Put("roles", "issuer|subject", new RoleGrant("issuer|subject", "Editor"), default); Assert.Equal("Editor", (await new LocalStore(path).Get<RoleGrant>("roles", "issuer|subject", default))!.Role); }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task BrowsingAndAuthorPagesAreIndependentOfTheSearchWindow()
    {
        var path = Path.Combine(Path.GetTempPath(), "dk-window-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new LocalStore(path);
            for (var i = 0; i < 205; i++)
            {
                var date = DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddMinutes(i);
                await store.Put("articles", i.ToString(), new Article(i.ToString(), "Article " + i, "shared text", [], "owner", "Owner", date, date), default);
            }
            var browse = await store.Browse(21, null, null, null, default);
            Assert.Equal(205, browse.Total); Assert.Equal(5, browse.Items.Count); Assert.False(browse.Limited);
            var mine = await store.Browse(21, null, null, "owner", default); Assert.Equal(5, mine.Items.Count);
            var search = await store.Browse(1, "shared", null, null, default); Assert.Equal(200, search.Total); Assert.True(search.Limited);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}



