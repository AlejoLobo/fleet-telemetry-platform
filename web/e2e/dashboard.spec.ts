import { expect, test } from "@playwright/test";

test.describe("Dashboard demo", () => {
  test("muestra el panel de control operativo", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByText("Centro de control operativo")).toBeVisible();
  });
});
