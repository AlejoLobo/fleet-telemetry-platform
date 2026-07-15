import { expect, test } from "@playwright/test";

async function waitForDemoReady(page: import("@playwright/test").Page) {
  await page.getByRole("button", { name: "Demo" }).click();
  const select = page.locator("#monitor-refresh-rate");
  await expect(select).toBeEnabled();
  // El snap OSRM es async; esperar a que desaparezca el indicador.
  await page
    .getByText("Ajustando a calles")
    .waitFor({ state: "hidden", timeout: 20_000 })
    .catch(() => undefined);
}

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
    await expect(select).toBeEnabled();
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
    await expect(select).toBeEnabled();
    await select.selectOption("15");
    await expect
      .poll(async () => page.evaluate(() => localStorage.getItem("fleet-monitor-refresh-rate")))
      .toBe("15");
    await page.reload();
    await expect(page.locator("#monitor-refresh-rate")).toHaveValue("15");
  });

  test("migra realtime legado a 5 y reescribe localStorage", async ({ page }) => {
    await page.goto("/");
    await page.evaluate(() => localStorage.setItem("fleet-monitor-refresh-rate", "realtime"));
    await page.reload();
    await expect(page.locator("#monitor-refresh-rate")).toHaveValue("5");
    await expect
      .poll(async () => page.evaluate(() => localStorage.getItem("fleet-monitor-refresh-rate")))
      .toBe("5");
  });

  test("Actualizar fuerza actualización inmediata con intervalo 20", async ({ page }) => {
    await page.goto("/");
    await waitForDemoReady(page);
    const select = page.locator("#monitor-refresh-rate");
    await select.selectOption("20");
    await expect(select).toHaveValue("20");

    const speed = page.locator("main").getByText(/\d+\s*km\/h/).first();
    await expect(speed).toBeVisible();
    const before = await speed.textContent();

    await page.waitForTimeout(2_500);
    expect(await speed.textContent()).toBe(before);

    await page.getByRole("button", { name: "Actualizar" }).click();
    await expect
      .poll(async () => speed.textContent(), { timeout: 5_000 })
      .not.toBe(before);
    await expect(select).toHaveValue("20");
  });

  test("ciclo de 5 segundos regenera demo tras el intervalo", async ({ page }) => {
    await page.goto("/");
    await waitForDemoReady(page);
    await page.locator("#monitor-refresh-rate").selectOption("5");
    const speed = page.locator("main").getByText(/\d+\s*km\/h/).first();
    const before = await speed.textContent();
    await page.waitForTimeout(2_000);
    expect(await speed.textContent()).toBe(before);
    await expect
      .poll(async () => speed.textContent(), { timeout: 8_000 })
      .not.toBe(before);
  });

  test("selector usable por teclado", async ({ page }) => {
    await page.goto("/");
    const select = page.locator("#monitor-refresh-rate");
    await expect(select).toBeEnabled();
    await select.focus();
    // Chromium: flecha cambia la opción del <select> nativo y dispara change en React.
    await select.press("ArrowDown");
    await expect(select).toHaveValue("10");
  });

  test("alertas de demo aparecen sin esperar el ciclo de 20s", async ({ page }) => {
    await page.goto("/");
    await waitForDemoReady(page);
    await page.locator("#monitor-refresh-rate").selectOption("20");
    await page.getByRole("button", { name: /Ver .* alertas/i }).click();
    await expect(page.getByRole("dialog")).toBeVisible({ timeout: 3_000 });
    await expect(page.getByText(/Alertas activas/i)).toBeVisible();
  });
});
