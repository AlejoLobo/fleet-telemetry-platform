import { describe, expect, it, vi } from "vitest";
import {
  appendSseTicket,
  resolveSseStreamUrl,
  shouldRequestSseTicket,
} from "@/lib/sse-auth";

describe("sse-auth FT-001", () => {
  it("auth deshabilitada: no solicita ticket y usa URL base", async () => {
    const fetchTicket = vi.fn();

    const url = await resolveSseStreamUrl({
      baseUrl: "http://localhost:5000",
      authStatus: { enabled: false },
      hasToken: false,
      fetchTicket,
    });

    expect(url).toBe("http://localhost:5000/api/events/stream");
    expect(fetchTicket).not.toHaveBeenCalled();
  });

  it("auth habilitada con token: solicita ticket efímero y lo agrega a la URL", async () => {
    const fetchTicket = vi.fn().mockResolvedValue({ ticket: "abc123", expiresInSeconds: 120 });

    const url = await resolveSseStreamUrl({
      baseUrl: "http://localhost:5000",
      authStatus: { enabled: true },
      hasToken: true,
      fetchTicket,
    });

    expect(fetchTicket).toHaveBeenCalledOnce();
    expect(url).toBe("http://localhost:5000/api/events/stream?ticket=abc123");
  });

  it("auth habilitada sin token: no solicita ticket (fallará en backend)", async () => {
    const fetchTicket = vi.fn();

    const url = await resolveSseStreamUrl({
      baseUrl: "http://localhost:5000",
      authStatus: { enabled: true },
      hasToken: false,
      fetchTicket,
    });

    expect(url).toBe("http://localhost:5000/api/events/stream");
    expect(fetchTicket).not.toHaveBeenCalled();
  });

  it("reconexión: cada resolución puede obtener un ticket nuevo", async () => {
    const fetchTicket = vi
      .fn()
      .mockResolvedValueOnce({ ticket: "ticket-1", expiresInSeconds: 120 })
      .mockResolvedValueOnce({ ticket: "ticket-2", expiresInSeconds: 120 });

    const first = await resolveSseStreamUrl({
      baseUrl: "http://localhost:5000",
      authStatus: { enabled: true },
      hasToken: true,
      fetchTicket,
    });
    const second = await resolveSseStreamUrl({
      baseUrl: "http://localhost:5000",
      authStatus: { enabled: true },
      hasToken: true,
      fetchTicket,
    });

    expect(first).toContain("ticket=ticket-1");
    expect(second).toContain("ticket=ticket-2");
    expect(fetchTicket).toHaveBeenCalledTimes(2);
  });

  it("appendSseTicket no expone JWT en la URL", () => {
    const url = appendSseTicket("http://localhost:5000/api/events/stream", "short-lived-ticket");
    expect(url).not.toContain("Bearer");
    expect(url).not.toContain("eyJ");
    expect(url).toContain("ticket=short-lived-ticket");
  });

  it("shouldRequestSseTicket solo con auth activa y token presente", () => {
    expect(shouldRequestSseTicket({ enabled: false }, true)).toBe(false);
    expect(shouldRequestSseTicket({ enabled: true }, false)).toBe(false);
    expect(shouldRequestSseTicket({ enabled: true }, true)).toBe(true);
  });
});
