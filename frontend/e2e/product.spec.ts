import { test, expect, Page } from "@playwright/test";
const identity = async (page: Page, user: string) => {
  await page.getByLabel("Local identity").selectOption(user);
  await expect(page.getByLabel("Local identity")).toHaveValue(user);
};
const write = async (page: Page, title: string, content: string) => {
  await page.getByRole("link", { name: "+ Write an article" }).click();
  await page.getByLabel("Title", { exact: true }).fill(title);
  await page.getByLabel("Tags").fill("testing, dotnet");
  await page.getByLabel("Markdown", { exact: true }).fill(content);
  await page
    .getByRole("button", { name: "Publish article", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: title, exact: true }),
  ).toBeVisible();
};
test("public navigation, search, tag filter, detail and responsive layout", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: /Good ideas/ })).toBeVisible();
  await page
    .getByRole("link", { name: "Build knowledge that lasts", exact: true })
    .click();
  await expect(page.locator(".prose h1")).toHaveText("Welcome to DevKnowledge");
  await expect(
    page.getByRole("link", { name: "Edit article", exact: true }),
  ).toHaveCount(0);
  await page.getByRole("link", { name: "Back to the library" }).click();
  await page.getByLabel("Search articles").fill("does-not-exist-unique");
  await page.getByRole("button", { name: "Search", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "No articles found" }),
  ).toBeVisible();
  await page.getByLabel("Search articles").fill("Welcome");
  await page.getByLabel("Tag", { exact: true }).fill("dotnet");
  await page.getByRole("button", { name: "Search", exact: true }).click();
  await expect(
    page.getByRole("link", { name: "Build knowledge that lasts", exact: true }),
  ).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(
    page.getByRole("link", { name: "Join the team", exact: true }),
  ).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});
test("editor paste, safe Markdown, preview, owner edit and delete", async ({
  page,
}) => {
  await page.goto("/");
  await identity(page, "editor");
  const title = "Paste journey " + Date.now();
  await write(
    page,
    title,
    "# Safe heading\n\n<script>window.pwned=1</script>\n\n[bad](javascript:alert%281%29)\n\n```csharp\nvar x = 42;\n```",
  );
  await expect(page.locator(".prose script")).toHaveCount(0);
  await expect(page.locator('.prose [href^="javascript:"]')).toHaveCount(0);
  expect(await page.evaluate(() => "pwned" in window)).toBe(false);
  await expect(page.locator(".prose .hljs-keyword")).toHaveCount(1);
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.locator(".prose .hljs-keyword").click();
  await expect(page.getByRole("status")).toContainText("Code copied.");
  expect(await page.evaluate(() => navigator.clipboard.readText())).toContain(
    "var x = 42;",
  );
  await page.getByRole("button", { name: "Copy first code block" }).click();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toContain(
    "var x = 42;",
  );
  await page.getByRole("link", { name: "Edit article", exact: true }).click();
  await page.getByLabel("Title", { exact: true }).fill(title + " updated");
  await page.getByRole("button", { name: "Preview", exact: true }).click();
  await expect(page.locator(".preview h1")).toHaveText("Safe heading");
  await page
    .getByRole("button", { name: "Publish article", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: title + " updated", exact: true }),
  ).toBeVisible();
  page.once("dialog", (dialog) => dialog.dismiss());
  await page
    .getByRole("button", { name: "Delete article", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: title + " updated", exact: true }),
  ).toBeVisible();
  page.once("dialog", (dialog) => dialog.accept());
  await page
    .getByRole("button", { name: "Delete article", exact: true })
    .click();
  await expect(
    page.getByText("Article deleted.", { exact: true }),
  ).toBeVisible();
});
test("Markdown import, access denied, admin edits and deletes another author", async ({
  page,
  request,
}) => {
  await page.goto("/");
  await identity(page, "other");
  await page.getByRole("link", { name: "+ Write an article" }).click();
  const title = "Imported journey " + Date.now();
  await page.getByLabel("Title", { exact: true }).fill(title);
  await page.getByLabel("Import Markdown file").setInputFiles({
    name: "wrong.txt",
    mimeType: "text/plain",
    buffer: Buffer.from("content"),
  });
  await expect(page.getByRole("alert")).toContainText("Choose a .md file");
  await page.getByLabel("Import Markdown file").setInputFiles({
    name: "guide.md",
    mimeType: "text/markdown",
    buffer: Buffer.from("# Imported content\n\nA local UTF-8 file. ✓"),
  });
  await expect(page.getByLabel("Markdown", { exact: true })).toHaveValue(
    /Imported content/,
  );
  await page
    .getByRole("button", { name: "Publish article", exact: true })
    .click();
  await expect(page.locator(".prose h1")).toHaveText("Imported content");
  const url = page.url();
  const id = url.split("/").pop();
  await identity(page, "editor");
  await expect(
    page.getByRole("link", { name: "Edit article", exact: true }),
  ).toHaveCount(0);
  const denied = await request.delete(
    "http://localhost:5080/api/articles/" + id,
    { headers: { "X-Dev-User": "editor" } },
  );
  expect(denied.status()).toBe(403);
  await page.goto("/edit/" + id);
  await expect(page.getByRole("alert")).toContainText("Access denied");
  await page.goto(url);
  await identity(page, "admin");
  await page.getByRole("link", { name: "Edit article", exact: true }).click();
  await page.getByLabel("Title", { exact: true }).fill(title + " moderated");
  await page
    .getByRole("button", { name: "Publish article", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: title + " moderated", exact: true }),
  ).toBeVisible();
  page.once("dialog", (dialog) => dialog.accept());
  await page
    .getByRole("button", { name: "Delete article", exact: true })
    .click();
  await expect(
    page.getByText("Article deleted.", { exact: true }),
  ).toBeVisible();
});
test("application submission, admin approval/rejection, and new editor publishing", async ({
  page,
  request,
}) => {
  await page.goto("/");
  await identity(page, "reader");
  await page.goto("/admin");
  await expect(page.getByRole("alert")).toContainText("Access denied");
  await page.getByRole("link", { name: "Join the team", exact: true }).click();
  const name = "Applicant " + Date.now();
  await page.getByLabel("Your name").fill(name);
  await page.getByLabel("Email address").fill("reader@example.test");
  await page
    .getByLabel("Why would you like to contribute?")
    .fill("I can explain practical testing.");
  await page.getByRole("button", { name: "Send application" }).click();
  await expect(page.getByRole("status")).toContainText("received");
  await identity(page, "admin");
  await page.getByRole("link", { name: "Applications", exact: true }).click();
  await page.getByLabel("Verified issuer|subject").fill("local|reader");
  const application = page
    .locator(".application")
    .filter({ has: page.getByRole("heading", { name: new RegExp(name) }) });
  await application
    .getByRole("button", { name: "Approve verified identity" })
    .click();
  await expect(application).toContainText("Approved");
  await request.post("http://localhost:5080/api/editor-applications", {
    data: {
      name: "Reject test",
      email: "reject@example.test",
      motivation: "Test rejection",
    },
  });
  await page.reload();
  const rejected = page
    .locator(".application")
    .filter({ has: page.getByRole("heading", { name: /Reject test/ }) })
    .first();
  await rejected.getByRole("button", { name: "Reject", exact: true }).click();
  await expect(rejected).toContainText("Rejected");
  await identity(page, "reader");
  await expect(
    page.getByRole("link", { name: "+ Write an article" }),
  ).toBeVisible();
  await write(page, "Approved author " + Date.now(), "# First contribution");
  page.once("dialog", (dialog) => dialog.accept());
  await page
    .getByRole("button", { name: "Delete article", exact: true })
    .click();
});
test("public pagination and author navigation", async ({ page, request }) => {
  const prefix = "Paging " + Date.now();
  const ids: string[] = [];
  try {
    for (let i = 0; i < 12; i++) {
      const response = await request.post(
        "http://localhost:5080/api/articles",
        {
          headers: { "X-Dev-User": "editor" },
          data: {
            title: prefix + " " + i,
            markdown: "# Page test",
            tags: ["pagination"],
          },
        },
      );
      expect(response.status()).toBe(201);
      ids.push((await response.json()).id);
    }
    await page.goto("/");
    await page.getByLabel("Search articles").fill(prefix);
    await page.getByRole("button", { name: "Search", exact: true }).click();
    await expect(page.locator(".card")).toHaveCount(10);
    await page.getByRole("button", { name: "Next", exact: true }).click();
    await expect(page.locator(".card")).toHaveCount(2);
    await page.getByRole("button", { name: "Previous", exact: true }).click();
    await expect(page.locator(".card")).toHaveCount(10);
    await identity(page, "editor");
    await page.getByRole("link", { name: "My articles", exact: true }).click();
    await expect(
      page.getByRole("heading", { name: "My articles", exact: true }),
    ).toBeVisible();
    await expect(page.locator(".row")).toHaveCount(10);
  } finally {
    for (const id of ids)
      await request.delete("http://localhost:5080/api/articles/" + id, {
        headers: { "X-Dev-User": "editor" },
      });
  }
});
