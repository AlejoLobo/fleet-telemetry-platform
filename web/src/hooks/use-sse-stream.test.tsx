/** @vitest-environment jsdom */
import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useSseStream } from "@/hooks/use-sse-stream";
import * as sseClient from "@/lib/sse-fetch-client";

vi.mock("@/lib/api-client", () => ({
  apiClient: {
    getSseUrl: () => "http://localhost:5000/api/events/stream",
    getAuthToken: vi.fn(() => null),
    fetchAuthStatus: vi.fn(async () => ({ enabled: false })),
  },
}));

describe("useSseStream FT-001", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it("cancela la conexión al desmontarse mediante AbortController", async () => {
    const abortSignals: AbortSignal[] = [];
    const consumeSpy = vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, init) => {
      abortSignals.push(init.signal);
      return new Promise(() => {
        /* stream abierto hasta abort */
      });
    });

    const { unmount } = renderHook(() => useSseStream({ enabled: true }));
    await waitFor(() => expect(abortSignals).toHaveLength(1));

    unmount();
    expect(abortSignals[0]?.aborted).toBe(true);
    expect(consumeSpy).toHaveBeenCalled();
  });

  it("no reconecta indefinidamente después de 401", async () => {
    const consumeSpy = vi.spyOn(sseClient, "consumeSseFetchStream").mockRejectedValue(
      new sseClient.SseAuthError(401),
    );

    renderHook(() => useSseStream({ enabled: true, authToken: "bad-token" }));
    await waitFor(() => expect(consumeSpy).toHaveBeenCalledTimes(1));

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 100));
    });

    expect(consumeSpy).toHaveBeenCalledTimes(1);
  });

  it("no reconecta indefinidamente después de 403", async () => {
    const consumeSpy = vi.spyOn(sseClient, "consumeSseFetchStream").mockRejectedValue(
      new sseClient.SseAuthError(403),
    );

    renderHook(() => useSseStream({ enabled: true, authToken: "forbidden-token" }));
    await waitFor(() => expect(consumeSpy).toHaveBeenCalledTimes(1));

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 100));
    });

    expect(consumeSpy).toHaveBeenCalledTimes(1);
  });

  it("reconecta después de un error temporal de red", async () => {
    vi.spyOn(sseClient, "computeReconnectDelayMs").mockReturnValue(50);
    const consumeSpy = vi.spyOn(sseClient, "consumeSseFetchStream")
      .mockRejectedValueOnce(new Error("network down"))
      .mockImplementation(async () => undefined);

    renderHook(() => useSseStream({ enabled: true }));
    await waitFor(() => expect(consumeSpy).toHaveBeenCalledTimes(1));

    await waitFor(
      () => expect(consumeSpy).toHaveBeenCalledTimes(2),
      { timeout: 2_000 },
    );
  });

  it("vuelve a conectar cuando cambia el token", async () => {
    const consumeSpy = vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, init) => {
      await new Promise<void>((resolve) => {
        init.signal.addEventListener("abort", () => resolve(), { once: true });
      });
    });

    const { rerender } = renderHook(
      ({ token }: { token: string | null }) => useSseStream({ enabled: true, authToken: token }),
      { initialProps: { token: "token-a" as string | null } },
    );

    await waitFor(() => expect(consumeSpy).toHaveBeenCalled());
    const callsBeforeRerender = consumeSpy.mock.calls.length;

    rerender({ token: "token-b" });
    await waitFor(() => expect(consumeSpy.mock.calls.length).toBeGreaterThan(callsBeforeRerender));
  });

  it("procesa fleet-update y alert desde eventos SSE", async () => {
    vi.spyOn(sseClient, "consumeSseFetchStream").mockImplementation(async (_url, _init, handlers) => {
      handlers.onEvent({
        event: "fleet-update",
        data: JSON.stringify([{ vehicleId: "VH-001", status: "online" }]),
      });
      handlers.onEvent({
        event: "alert",
        data: JSON.stringify({
          alertId: "a1",
          vehicleId: "VH-001",
          alertType: "overspeed",
          severity: "critical",
          message: "x",
          createdAt: "2026-07-11T00:00:00Z",
          isAcknowledged: false,
        }),
      });
    });

    const onFleetUpdate = vi.fn();
    const onAlert = vi.fn();
    renderHook(() => useSseStream({ enabled: true, onFleetUpdate, onAlert }));

    await waitFor(() => {
      expect(onFleetUpdate).toHaveBeenCalled();
      expect(onAlert).toHaveBeenCalled();
    });
  });
});
