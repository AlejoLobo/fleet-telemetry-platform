import { expect, test } from "@playwright/test";

test.describe("Dashboard demo", () => {
  test("muestra el panel de control operativo", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByText("Centro de control operativo")).toBeVisible();
  });
});

test.describe("Selector de actualización", () => {
  test("persiste la tasa en localStorage y tras recargar", async ({ page }) => {
    await page.goto("/");
    const select = page.locator("#monitor-refresh-rate");
    await expect(select).toBeVisible();
    await select.selectOption("10");
    await expect
      .poll(async () => page.evaluate(() => localStorage.getItem("fleet-monitor-refresh-rate")))
      .toBe("10");

    await page.reload();
    await expect(page.locator("#monitor-refresh-rate")).toHaveValue("10");
  });

  test("modo Demo y actualización manual responden", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Demo" }).click();
    await page.locator("#monitor-refresh-rate").selectOption("5");
    await expect(page.locator("#monitor-refresh-rate")).toHaveValue("5");
    await page.getByRole("button", { name: "Actualizar" }).click();
    await expect(page.getByText("Centro de control operativo")).toBeVisible();
  });

  test("selector usable por teclado", async ({ page }) => {
    await page.goto("/");
    const select = page.locator("#monitor-refresh-rate");
    await select.focus();
    await page.keyboard.press("ArrowDown");
    await expect(select).toBeVisible();
    await expect(page.getByText("Actualización")).toBeVisible();
  });
});
