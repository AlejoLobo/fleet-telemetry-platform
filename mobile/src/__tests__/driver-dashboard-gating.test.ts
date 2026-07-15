import { isStartTrackingDisabled } from "../components/DriverDashboard";

describe("DriverDashboard gating de tracking", () => {
  it("deshabilita Iniciar tracking hasta deviceIdReady", () => {
    expect(
      isStartTrackingDisabled({
        busy: false,
        deviceIdReady: false,
        deviceId: "abc",
      }),
    ).toBe(true);
  });

  it("deshabilita cuando deviceId es null", () => {
    expect(
      isStartTrackingDisabled({
        busy: false,
        deviceIdReady: true,
        deviceId: null,
      }),
    ).toBe(true);
  });

  it("habilita cuando deviceId está listo", () => {
    expect(
      isStartTrackingDisabled({
        busy: false,
        deviceIdReady: true,
        deviceId: "stable-id",
      }),
    ).toBe(false);
  });

  it("deshabilita mientras busy", () => {
    expect(
      isStartTrackingDisabled({
        busy: true,
        deviceIdReady: true,
        deviceId: "stable-id",
      }),
    ).toBe(true);
  });
});
