import { marked } from "marked";
import DOMPurify from "dompurify";
import hljs from "highlight.js/lib/core";
import csharp from "highlight.js/lib/languages/csharp";
import typescript from "highlight.js/lib/languages/typescript";
import json from "highlight.js/lib/languages/json";
import bash from "highlight.js/lib/languages/bash";
hljs.registerLanguage("csharp", csharp);
hljs.registerLanguage("typescript", typescript);
hljs.registerLanguage("json", json);
hljs.registerLanguage("bash", bash);
export function renderMarkdown(markdown: string): string {
  const renderer = new marked.Renderer();
  renderer.html = () => ""; // Author HTML never reaches the DOM.
  renderer.image = ({ text }) => text; // Embedded media is outside the product scope.
  const safe = DOMPurify.sanitize(
    marked.parse(markdown, { async: false, renderer }) as string,
    {
      USE_PROFILES: { html: true },
      FORBID_TAGS: ["img", "style", "iframe", "form", "input", "button"],
    },
  );
  const doc = new DOMParser().parseFromString(safe, "text/html");
  doc.querySelectorAll("pre code").forEach((element) => {
    const language = element.className.replace("language-", "");
    if (hljs.getLanguage(language))
      element.innerHTML = hljs.highlight(element.textContent || "", {
        language,
      }).value;
  });
  return doc.body.innerHTML;
}
export function articleError(
  title: string,
  markdown: string,
  tags: string[],
): string {
  if (!title.trim() || title.trim().length > 160)
    return "Title must contain 1–160 characters.";
  if (!markdown.trim() || new TextEncoder().encode(markdown).length > 102400)
    return "Markdown is required and must not exceed 100 KiB UTF-8.";
  if (
    tags.length > 5 ||
    tags.some((tag) => !/^[a-z0-9][a-z0-9-]{0,23}$/.test(tag))
  )
    return "Use at most five lowercase tags, each 1–24 letters, digits or hyphens.";
  return "";
}
