using System.Text;
using System.Text.RegularExpressions;

namespace DevKnowledge.Domain;

public record Article(string Id, string Title, string Markdown, string[] Tags, string AuthorId, string AuthorName, DateTimeOffset PublishedAt, DateTimeOffset UpdatedAt);
public record ArticleInput(string Title, string Markdown, string[] Tags);
public record ApplicationInput(string Name, string Email, string Motivation, string? Website);
public record EditorialApplication(string Id, string Name, string Email, string Motivation, string Status, DateTimeOffset CreatedAt, string? ApprovedSubject = null);
public record RoleGrant(string Id, string Role);
public record ApprovalInput(string Subject);
public record ArticlePage(IReadOnlyList<Article> Items, int Total, bool Limited = false);
public static partial class Rules
{
    public const int MaxMarkdownBytes = 100 * 1024;
    public static Dictionary<string, string[]> Validate(ArticleInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 160) errors["title"] = ["Title must contain 1–160 characters."];
        if (string.IsNullOrWhiteSpace(input.Markdown) || Encoding.UTF8.GetByteCount(input.Markdown) > MaxMarkdownBytes) errors["markdown"] = ["Markdown is required and must not exceed 100 KiB UTF-8."];
        if (input.Tags is null || input.Tags.Length > 5 || input.Tags.Any(t => t is null || !TagPattern().IsMatch(t))) errors["tags"] = ["Use at most five lowercase tags, each 1–24 letters, digits or hyphens."];
        return errors;
    }
    public static Dictionary<string, string[]> Validate(ApplicationInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100) errors["name"] = ["Name must contain 1–100 characters."];
        if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(input.Email, out var address) || address.Address != input.Email) errors["email"] = ["Enter a valid email address."];
        if (string.IsNullOrWhiteSpace(input.Motivation) || input.Motivation.Length > 2000) errors["motivation"] = ["Motivation must contain 1–2000 characters."];
        return errors;
    }
    public static bool CanWrite(string? role) => role is "Editor" or "Admin";
    public static bool CanEdit(Article article, string subject, string? role) => role == "Admin" || role == "Editor" && article.AuthorId == subject;
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,23}$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
