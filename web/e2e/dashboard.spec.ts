import { expect, test } from "@playwright/test";

const E2E_DEVICE_ID = "00000000-0000-4000-8000-000000000001";

async function prepareDemo(page: import("@playwright/test").Page) {
  await page.goto("/");
  await page.getByRole("button", { name: "Demo" }).click();
  await expect(page.locator("#monitor-refresh-rate")).toBeEnabled();
  await page
    .getByText("Ajustando a calles")
    .waitFor({ state: "hidden", timeout: 20_000 })
    .catch(() => undefined);
  await expect
    .poll(async () => page.evaluate(() => Boolean(window.__FLEET_E2E__)))
    .toBe(true);
}

test.describe("Dashboard demo", () => {
  test("muestra el panel de control operativo", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByText("Centro de control operativo")).toBeVisible();
  });

  test("demo incluye motocicleta VH-005", async ({ page }) => {
    await prepareDemo(page);
    // Filas del panel (button.group); el marcador Leaflet también es role=button con VH-005.
    const row = page.locator("button.group").filter({ hasText: "VH-005" });
    await expect(row).toBeVisible();
    await expect(row.getByText("Motocicleta", { exact: true })).toBeVisible();
    await expect(row.getByLabel("Tipo de vehículo: Motocicleta")).toBeVisible();
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
    expect(labels.join(" ")).not.toContain("Cada 30 segundos");
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

  for (const legacy of ["realtime", "30", "60", "bogus"]) {
    test(`normaliza legado ${legacy} a 5 y reescribe localStorage`, async ({ page }) => {
      await page.goto("/");
      await page.evaluate(
        (value) => localStorage.setItem("fleet-monitor-refresh-rate", value),
        legacy,
      );
      await page.reload();
      await expect(page.locator("#monitor-refresh-rate")).toHaveValue("5");
      await expect
        .poll(async () => page.evaluate(() => localStorage.getItem("fleet-monitor-refresh-rate")))
        .toBe("5");
    });
  }

  test("buffer 20s: actualización no aparece hasta Actualizar", async ({ page }) => {
    await prepareDemo(page);
    const select = page.locator("#monitor-refresh-rate");
    await select.selectOption("20");
    await expect(select).toHaveValue("20");

    await page.evaluate((deviceId) => {
      window.__FLEET_E2E__!.emitVehicleUpdate({
        deviceId,
        vehicleName: "Vehículo E2E",
        vehicleType: "car",
        status: "online",
        lastSeenAt: "2099-01-01T00:00:00Z",
        lastSpeedKmh: 137,
        lastLatitude: 4.65,
        lastLongitude: -74.08,
      });
    }, E2E_DEVICE_ID);

    await expect
      .poll(async () => page.evaluate(() => window.__FLEET_E2E__!.getPendingVehicleCount()))
      .toBeGreaterThan(0);
    await page.waitForTimeout(2_500);
    await expect(page.getByText("137 km/h")).toHaveCount(0);

    await page.getByRole("button", { name: "Actualizar" }).click();
    await expect(page.getByText("137 km/h").first()).toBeVisible({ timeout: 5_000 });
    await expect(select).toHaveValue("20");
  });

  test("ciclo automático 5s aplica buffer tras el intervalo", async ({ page }) => {
    await prepareDemo(page);
    await page.locator("#monitor-refresh-rate").selectOption("5");

    await page.evaluate((deviceId) => {
      window.__FLEET_E2E__!.emitVehicleUpdate({
        deviceId,
        vehicleName: "Vehículo E2E",
        vehicleType: "car",
        status: "online",
        lastSeenAt: "2099-01-01T00:00:01Z",
        lastSpeedKmh: 141,
        lastLatitude: 4.65,
        lastLongitude: -74.08,
      });
    }, E2E_DEVICE_ID);

    await expect(page.getByText("141 km/h")).toHaveCount(0);
    await expect(page.getByText("141 km/h").first()).toBeVisible({ timeout: 9_000 });
  });

  test("alerta nueva aparece de inmediato con intervalo 20", async ({ page }) => {
    await prepareDemo(page);
    const select = page.locator("#monitor-refresh-rate");
    await select.selectOption("20");

    await page.evaluate((deviceId) => {
      window.__FLEET_E2E__!.emitAlert({
        alertId: "alert-e2e-immediate",
        deviceId,
        alertType: "overspeed",
        severity: "critical",
        message: "Alerta E2E inmediata",
        createdAt: new Date().toISOString(),
        isAcknowledged: false,
      });
    }, E2E_DEVICE_ID);

    await page.getByRole("button", { name: /Ver .* alertas/i }).click();
    await expect(page.getByRole("dialog")).toBeVisible({ timeout: 3_000 });
    await expect(page.getByText("Alerta E2E inmediata")).toBeVisible({ timeout: 3_000 });
    await expect(select).toHaveValue("20");
  });

  test("selector usable por teclado y persiste", async ({ page }) => {
    await page.goto("/");
    const select = page.locator("#monitor-refresh-rate");
    await expect(select).toBeEnabled();
    await select.focus();
    await select.press("ArrowDown");
    await expect(select).toHaveValue("10");
    await expect
      .poll(async () => page.evaluate(() => localStorage.getItem("fleet-monitor-refresh-rate")))
      .toBe("10");
  });
});
