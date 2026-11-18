import { ChangeDetectorRef, Component, inject, signal } from "@angular/core";
import { bootstrapApplication } from "@angular/platform-browser";
import { provideHttpClient } from "@angular/common/http";
import { provideOAuthClient } from "angular-oauth2-oidc";
import {
  provideRouter,
  Router,
  RouterLink,
  NavigationEnd,
} from "@angular/router";
import { FormsModule } from "@angular/forms";
import { DatePipe } from "@angular/common";
import { Api, Article, Application, Listing } from "./api";
import { articleError, renderMarkdown } from "./markdown";

@Component({ selector: "app-page", standalone: true, template: "" })
class Page {}
@Component({
  selector: "app-root",
  standalone: true,
  imports: [RouterLink, FormsModule, DatePipe],
  templateUrl: "./app.html",
})
export class App {
  private changeDetector = inject(ChangeDetectorRef);
  api = inject(Api);
  router = inject(Router);
  ready = signal(false);
  loading = signal(false);
  error = signal("");
  success = signal("");
  view = signal("home");
  articles = signal<Article[]>([]);
  article = signal<Article | null>(null);
  applications = signal<Application[]>([]);
  total = signal(0);
  page = 1;
  search = "";
  tag = "";
  limited = false;
  title = "";
  markdown = "";
  tags = "";
  name = "";
  email = "";
  motivation = "";
  website = "";
  subject = "";
  editingId = "";
  preview = signal(false);
  html = signal("");
  constructor() {
    this.router.events.subscribe((event) => {
      if (event instanceof NavigationEnd && this.ready()) void this.load();
    });
    void this.api
      .init()
      .then(() => {
        this.ready.set(true);
        return this.load();
      })
      .catch((e) => this.error.set(String(e.message)));
  }
  async load() {
    this.error.set("");
    this.success.set("");
    this.loading.set(true);
    this.article.set(null);
    const path = this.router.url.split("?")[0];
    try {
      if (path === "/write" || /^\/edit\//.test(path)) {
        this.view.set("write");
        if (!this.api.canWrite())
          throw new Error(
            "Access denied. Approved editors and administrators can publish articles.",
          );
        this.editingId = path.startsWith("/edit/") ? path.split("/")[2] : "";
        this.preview.set(false);
        this.title = "";
        this.markdown = "";
        this.tags = "";
        if (this.editingId) {
          const a = await this.api.request<Article>(
            "/api/articles/" + this.editingId,
          );
          if (!this.api.canEdit(a))
            throw new Error(
              "Access denied. You can only edit your own articles.",
            );
          this.title = a.title;
          this.markdown = a.markdown;
          this.tags = a.tags.join(", ");
        }
      } else if (path.startsWith("/articles/")) {
        this.view.set("detail");
        const a = await this.api.request<Article>(
          path.replace("/articles/", "/api/articles/"),
        );
        this.article.set(a);
        this.html.set(renderMarkdown(a.markdown));
      } else if (path === "/join") this.view.set("join");
      else if (path === "/admin") {
        this.view.set("admin");
        this.applications.set(
          await this.api.request<Application[]>(
            "/api/admin/editor-applications",
          ),
        );
      } else if (path === "/mine") {
        this.view.set("mine");
        this.page = 1;
        await this.mine();
      } else {
        this.view.set("home");
        await this.list();
      }
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.loading.set(false);
    }
  }
  async list(reset = false) {
    if (reset) this.page = 1;
    this.loading.set(true);
    this.error.set("");
    try {
      const result = await this.api.request<Listing>(
        "/api/articles?" +
          new URLSearchParams({
            search: this.search,
            tag: this.tag,
            page: String(this.page),
          }),
      );
      this.articles.set(result.items);
      this.total.set(result.total);
      this.limited = result.limited;
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.loading.set(false);
    }
  }
  async mine() {
    this.loading.set(true);
    try {
      const result = await this.api.request<Listing>(
        "/api/me/articles?page=" + this.page,
      );
      this.articles.set(result.items);
      this.total.set(result.total);
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.loading.set(false);
    }
  }
  async identity(user: string) {
    try {
      await this.api.selectUser(user);
      await this.load();
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }
  async save() {
    const tags = this.tags
      .split(",")
      .map((t) => t.trim())
      .filter(Boolean);
    const error = articleError(this.title, this.markdown, tags);
    if (error) {
      this.error.set(error);
      return;
    }
    this.loading.set(true);
    this.error.set("");
    try {
      const a = await this.api.request<Article>(
        "/api/articles" + (this.editingId ? "/" + this.editingId : ""),
        this.editingId ? "PUT" : "POST",
        { title: this.title, markdown: this.markdown, tags },
      );
      await this.router.navigateByUrl("/articles/" + a.id);
      this.success.set("Article published.");
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.loading.set(false);
    }
  }
  async importFile(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    try {
      if (!file.name.toLowerCase().endsWith(".md") || file.size > 102400)
        throw new Error("Choose a .md file no larger than 100 KiB.");
      this.markdown = new TextDecoder("utf-8", { fatal: true }).decode(
        await file.arrayBuffer(),
      );
      this.error.set("");
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      input.value = "";
      this.changeDetector.markForCheck();
    }
  }
  showPreview() {
    this.html.set(renderMarkdown(this.markdown));
    this.preview.update((x) => !x);
  }
  async remove(a: Article) {
    if (!confirm("Delete “" + a.title + "”? This cannot be undone.")) return;
    try {
      await this.api.request("/api/articles/" + a.id, "DELETE");
      await this.router.navigateByUrl("/");
      this.success.set("Article deleted.");
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }
  async apply() {
    this.loading.set(true);
    this.error.set("");
    try {
      await this.api.request("/api/editor-applications", "POST", {
        name: this.name,
        email: this.email,
        motivation: this.motivation,
        website: this.website,
      });
      this.success.set(
        "Thank you. Your application has been received for review.",
      );
      this.name = "";
      this.email = "";
      this.motivation = "";
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.loading.set(false);
    }
  }
  async moderate(a: Application, action: string) {
    this.error.set("");
    try {
      await this.api.request(
        "/api/admin/editor-applications/" + a.id + "/" + action,
        "POST",
        action === "approve" ? { subject: this.subject } : {},
      );
      await this.load();
      this.success.set(
        "Application " + (action === "approve" ? "approved." : "rejected."),
      );
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }
  async copyCode(event: Event) {
    const element = event.target as HTMLElement;
    const code = element.closest("pre")?.querySelector("code");
    if (code) {
      try {
        await navigator.clipboard.writeText(code.textContent || "");
        this.success.set("Code copied.");
      } catch {
        this.error.set(
          "Clipboard access unavailable. Select and copy the code manually.",
        );
      }
    }
  }
  async copyFirstCode() {
    const code = document.querySelector(".prose pre code");
    if (code) {
      try {
        await navigator.clipboard.writeText(code.textContent || "");
        this.success.set("Code copied.");
      } catch {
        this.error.set("Clipboard access unavailable.");
      }
    }
  }
}
bootstrapApplication(App, {
  providers: [
    provideHttpClient(),
    provideOAuthClient(),
    provideRouter([{ path: "**", component: Page }]),
  ],
}).catch(console.error);
