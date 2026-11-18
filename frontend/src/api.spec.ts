import "@angular/compiler";
import { describe, expect, it } from "vitest";
import { Api, Article } from "./api";
describe("permissions", () => {
  const article = { authorId: "issuer|owner" } as Article;
  it("shows write and edit actions only to permitted identities", () => {
    const api = new Api({} as never);
    expect(api.canWrite()).toBe(false);
    expect(api.canEdit(article)).toBe(false);
    api.me.set({ subject: "issuer|owner", name: "Owner", role: null });
    expect(api.canWrite()).toBe(false);
    api.me.set({ subject: "issuer|owner", name: "Owner", role: "Editor" });
    expect(api.canEdit(article)).toBe(true);
    api.me.set({ subject: "issuer|other", name: "Other", role: "Editor" });
    expect(api.canEdit(article)).toBe(false);
    api.me.set({ subject: "issuer|admin", name: "Admin", role: "Admin" });
    expect(api.canEdit(article)).toBe(true);
  });
});
