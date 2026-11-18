import { Injectable, signal } from "@angular/core";
import { OAuthService } from "angular-oauth2-oidc";
export interface Config {
  apiUrl: string;
  developmentAuth: boolean;
  issuer: string;
  clientId: string;
  scope: string;
  commit?: string;
  sourceRef?: string;
}
export interface Me {
  subject: string;
  name: string;
  role: "Admin" | "Editor" | null;
}
export interface Article {
  id: string;
  title: string;
  markdown: string;
  tags: string[];
  authorId: string;
  authorName: string;
  publishedAt: string;
}
export interface Application {
  id: string;
  name: string;
  email: string;
  motivation: string;
  status: string;
}
export interface Listing {
  items: Article[];
  total: number;
  page: number;
  pageSize: number;
  limited: boolean;
}
@Injectable({ providedIn: "root" })
export class Api {
  config!: Config;
  me = signal<Me | null>(null);
  user = signal(sessionStorage.getItem("dev-user") || "");
  constructor(private oauth: OAuthService) {}
  async init() {
    this.config = await (
      await fetch("/config.json", { cache: "no-store" })
    ).json();
    if (!this.config.developmentAuth) {
      this.oauth.configure({
        issuer: this.config.issuer,
        clientId: this.config.clientId,
        redirectUri: location.origin + "/",
        responseType: "code",
        scope: this.config.scope,
        strictDiscoveryDocumentValidation: true,
        requireHttps: true,
        showDebugInformation: false,
      });
      await this.oauth.loadDiscoveryDocumentAndTryLogin();
      this.oauth.setupAutomaticSilentRefresh();
    }
    await this.refresh();
  }
  async refresh() {
    if (
      this.config.developmentAuth
        ? !this.user()
        : !this.oauth.hasValidAccessToken()
    ) {
      this.me.set(null);
      return;
    }
    this.me.set(await this.request<Me>("/api/me"));
  }
  async selectUser(user: string) {
    sessionStorage.setItem("dev-user", user);
    this.user.set(user);
    await this.refresh();
  }
  login() {
    this.oauth.initCodeFlow();
  }
  logout() {
    this.me.set(null);
    this.oauth.logOut();
  }
  canWrite() {
    return ["Admin", "Editor"].includes(this.me()?.role || "");
  }
  canEdit(article: Article) {
    return (
      this.me()?.role === "Admin" ||
      (this.me()?.role === "Editor" && this.me()?.subject === article.authorId)
    );
  }
  async request<T>(path: string, method = "GET", body?: unknown): Promise<T> {
    const headers: Record<string, string> = {
      "Content-Type": "application/json",
    };
    if (this.config.developmentAuth && this.user())
      headers["X-Dev-User"] = this.user();
    else if (this.oauth.hasValidAccessToken())
      headers["Authorization"] = "Bearer " + this.oauth.getAccessToken();
    const response = await fetch(this.config.apiUrl + path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) {
      const error = await response.json().catch(() => ({}));
      throw new Error(
        error.errors
          ? Object.values(error.errors).flat().join(" ")
          : response.status === 403
            ? "Access denied. You do not have permission for this action."
            : response.status === 401
              ? "Sign in to continue."
              : error.title || "The request failed. Please try again.",
      );
    }
    return response.status === 204 ? (undefined as T) : await response.json();
  }
}
