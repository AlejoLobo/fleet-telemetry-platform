import { isStartTrackingDisabled } from "@/components/DriverDashboard";

describe("DriverDashboard gating de tracking", () => {
  it("deshabilita Iniciar tracking hasta intervalReady", () => {
    expect(
      isStartTrackingDisabled({
        busy: false,
        intervalReady: false,
        deviceIdReady: true,
        deviceId: "abc",
      }),
    ).toBe(true);
  });

  it("deshabilita Iniciar tracking hasta deviceIdReady", () => {
    expect(
      isStartTrackingDisabled({
        busy: false,
        intervalReady: true,
        deviceIdReady: false,
        deviceId: "abc",
      }),
    ).toBe(true);
  });

  it("deshabilita cuando deviceId es null", () => {
    expect(
      isStartTrackingDisabled({
        busy: false,
        intervalReady: true,
        deviceIdReady: true,
        deviceId: null,
      }),
    ).toBe(true);
  });

  it("habilita cuando configuración y deviceId están listos", () => {
    expect(
      isStartTrackingDisabled({
        busy: false,
        intervalReady: true,
        deviceIdReady: true,
        deviceId: "stable-id",
      }),
    ).toBe(false);
  });
});
