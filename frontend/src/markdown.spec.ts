import { describe, expect, it } from "vitest";
import { articleError, renderMarkdown } from "./markdown";
describe("safe article content", () => {
  it("rejects empty, oversized UTF-8 and invalid tags", () => {
    expect(articleError("", "text", [])).toContain("Title");
    expect(articleError("Title", "é".repeat(51201), [])).toContain("100 KiB");
    expect(articleError("Title", "text", ["BAD"])).toContain("tags");
    expect(articleError("Title", "é".repeat(51200), ["dotnet"])).toBe("");
  });
  it("removes raw HTML, scripts, embedded media and dangerous links", () => {
    const html = renderMarkdown(
      "# Hello\n\n<script>alert(1)</script>\n\n<img src=x onerror=alert(1)>\n\n[click](javascript:alert%281%29)\n\n![tracking](https://evil.example/pixel)",
    );
    expect(html).toContain("<h1>Hello</h1>");
    expect(html).not.toMatch(/<script|<img|javascript:|onerror|evil\.example/);
  });
  it("highlights code while escaping executable content", () => {
    const html = renderMarkdown('```csharp\nvar value = "<script>";\n```');
    expect(html).toContain("hljs-keyword");
    expect(html).not.toContain("<script>");
  });
});
