using System.Text.Json;
using Azure.Identity;
using DevKnowledge.Domain;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace DevKnowledge.Infrastructure;

public interface IStore
{
    Task<ArticlePage> Browse(int page, string? search, string? tag, string? authorId, CancellationToken ct);
    Task<T?> Get<T>(string kind, string id, CancellationToken ct);
    Task<IReadOnlyList<T>> List<T>(string kind, int limit, CancellationToken ct);
    Task Put<T>(string kind, string id, T value, CancellationToken ct);
    Task Delete(string kind, string id, CancellationToken ct);
}

// Development-only durable adapter. Serialize every mutation and atomically replace the file.
public sealed class LocalStore(string path) : IStore
{
    public async Task<ArticlePage> Browse(int page, string? search, string? tag, string? authorId, CancellationToken ct) => ArticleQueries.Browse(await List<Article>("articles", int.MaxValue, ct), page, search, tag, authorId);
    private readonly SemaphoreSlim gate = new(1);
    private Dictionary<string, JsonElement> data = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path))! : [];
    public async Task<T?> Get<T>(string kind, string id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return data.TryGetValue(kind + ":" + id, out var value) ? value.Deserialize<T>() : default; }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<T>> List<T>(string kind, int limit, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return data.Where(x => x.Key.StartsWith(kind + ":", StringComparison.Ordinal)).Select(x => x.Value.Deserialize<T>()!).OrderByDescending(x => x is Article a ? a.PublishedAt : x is EditorialApplication e ? e.CreatedAt : DateTimeOffset.MinValue).Take(limit).ToArray(); }
        finally { gate.Release(); }
    }
    public async Task Put<T>(string kind, string id, T value, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { var next = new Dictionary<string, JsonElement>(data) { [kind + ":" + id] = JsonSerializer.SerializeToElement(value) }; await Save(next, ct); data = next; }
        finally { gate.Release(); }
    }
    public async Task Delete(string kind, string id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { var next = new Dictionary<string, JsonElement>(data); next.Remove(kind + ":" + id); await Save(next, ct); data = next; }
        finally { gate.Release(); }
    }
    private async Task Save(Dictionary<string, JsonElement> next, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(next), ct);
        File.Move(path + ".tmp", path, true);
    }
}

// One container per environment; kind partitions isolate articles, applications and grants.
public sealed class CosmosStore : IStore, IDisposable
{
    public async Task<ArticlePage> Browse(int page, string? search, string? tag, string? authorId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(search)) return ArticleQueries.Browse(await List<Article>("articles", 201, ct), page, search, tag, authorId);
        var where = " WHERE (IS_NULL(@tag) OR ARRAY_CONTAINS(c.value.tags, @tag)) AND (IS_NULL(@author) OR c.value.authorId = @author)";
        QueryDefinition Query(string sql) => new QueryDefinition(sql).WithParameter("@tag", string.IsNullOrEmpty(tag) ? null : tag).WithParameter("@author", authorId);
        var options = new QueryRequestOptions { PartitionKey = new PartitionKey("articles"), MaxItemCount = 10 };
        using var count = container.GetItemQueryIterator<int>(Query("SELECT VALUE COUNT(1) FROM c" + where), requestOptions: options);
        var total = (await count.ReadNextAsync(ct)).Single();
        using var iterator = container.GetItemQueryIterator<Document<Article>>(Query("SELECT * FROM c" + where + " ORDER BY c.sortDate DESC OFFSET @offset LIMIT 10").WithParameter("@offset", (page - 1) * 10), requestOptions: options);
        var articles = new List<Article>();
        while (iterator.HasMoreResults) articles.AddRange((await iterator.ReadNextAsync(ct)).Select(d => d.value));
        return new ArticlePage(articles, total);
    }
    private static string Key(string id) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)));
    private readonly CosmosClient client;
    private readonly Container container;
    public CosmosStore(string endpoint, string database)
    {
        client = new CosmosClient(endpoint, new DefaultAzureCredential(), new CosmosClientOptions { UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) });
        container = client.GetContainer(database, "content");
    }
    private record Document<T>(string id, string kind, T value, DateTimeOffset sortDate);
    public async Task<T?> Get<T>(string kind, string id, CancellationToken ct)
    {
        try { return (await container.ReadItemAsync<Document<T>>(Key(id), new PartitionKey(kind), cancellationToken: ct)).Resource.value; }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return default; }
    }
    public async Task<IReadOnlyList<T>> List<T>(string kind, int limit, CancellationToken ct)
    {
        var result = new List<T>();
        using var iterator = container.GetItemQueryIterator<Document<T>>(new QueryDefinition("SELECT TOP @limit * FROM c ORDER BY c.sortDate DESC").WithParameter("@limit", limit), requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(kind), MaxItemCount = limit });
        while (iterator.HasMoreResults && result.Count < limit) result.AddRange((await iterator.ReadNextAsync(ct)).Select(x => x.value));
        return result.Take(limit).ToArray();
    }
    public async Task Put<T>(string kind, string id, T value, CancellationToken ct) => await container.UpsertItemAsync(new Document<T>(Key(id), kind, value, value is Article a ? a.PublishedAt : value is EditorialApplication e ? e.CreatedAt : DateTimeOffset.UtcNow), new PartitionKey(kind), cancellationToken: ct);
    public async Task Delete(string kind, string id, CancellationToken ct) { try { await container.DeleteItemAsync<object>(Key(id), new PartitionKey(kind), cancellationToken: ct); } catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { } }
    public void Dispose() => client.Dispose();
}

internal static class ArticleQueries
{
    public static ArticlePage Browse(IReadOnlyList<Article> all, int page, string? search, string? tag, string? authorId)
    {
        var searching = !string.IsNullOrWhiteSpace(search);
        var candidates = searching ? all.Take(200) : all;
        var result = candidates.Where(a => (authorId is null || a.AuthorId == authorId) && (string.IsNullOrEmpty(tag) || a.Tags.Contains(tag)) && (!searching || a.Title.Contains(search!, StringComparison.OrdinalIgnoreCase) || a.Markdown.Contains(search!, StringComparison.OrdinalIgnoreCase))).ToArray();
        return new ArticlePage(result.Skip((page - 1) * 10).Take(10).ToArray(), result.Length, searching && all.Count > 200);
    }
}
