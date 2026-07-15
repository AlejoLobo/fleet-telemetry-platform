import { expect, test } from "@playwright/test";

test.describe("Dashboard demo", () => {
  test("muestra el panel de control operativo", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByText("Centro de control operativo")).toBeVisible();
  });
});

test.describe("Selector de actualización 5/10/15/20", () => {
  test("opciones exactas y valor predeterminado 5", async ({ page }) => {
    await page.goto("/");
    const select = page.locator("#monitor-refresh-rate");
    await expect(select).toBeVisible();
    await expect(select).toHaveValue("5");
    const labels = await select.locator("option").allTextContents();
    expect(labels).toEqual([
      "Cada 5 segundos",
      "Cada 10 segundos",
      "Cada 15 segundos",
      "Cada 20 segundos",
    ]);
    expect(labels.join(" ")).not.toContain("Tiempo real");
    expect(labels.join(" ")).not.toContain("1 minuto");
  });

  test("persiste 15 segundos tras recargar", async ({ page }) => {
    await page.goto("/");
    const select = page.locator("#monitor-refresh-rate");
    await select.selectOption("15");
    await expect
      .poll(async () => page.evaluate(() => localStorage.getItem("fleet-monitor-refresh-rate")))
      .toBe("15");
    await page.reload();
    await expect(page.locator("#monitor-refresh-rate")).toHaveValue("15");
  });

  test("migra realtime legado a 5", async ({ page }) => {
    await page.goto("/");
    await page.evaluate(() => localStorage.setItem("fleet-monitor-refresh-rate", "realtime"));
    await page.reload();
    await expect(page.locator("#monitor-refresh-rate")).toHaveValue("5");
  });

  test("Actualizar responde en modo Demo con intervalo 20", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Demo" }).click();
    await page.locator("#monitor-refresh-rate").selectOption("20");
    await page.getByRole("button", { name: "Actualizar" }).click();
    await expect(page.getByText("Centro de control operativo")).toBeVisible();
  });

  test("selector usable por teclado", async ({ page }) => {
    await page.goto("/");
    const select = page.locator("#monitor-refresh-rate");
    await select.focus();
    await select.selectOption("10");
    await expect(select).toHaveValue("10");
  });
});
