import { describe, expect, it } from "vitest";
import {
  buildSseHeaders,
  computeReconnectDelayMs,
  consumeSseFetchStream,
  isSseAuthError,
  SseAuthError,
  SseParser,
} from "@/lib/sse-fetch-client";

describe("sse-fetch-client FT-001", () => {
  it("buildSseHeaders agrega Authorization cuando auth está habilitada y hay token", () => {
    expect(buildSseHeaders(true, "jwt-token")).toEqual({
      Authorization: "Bearer jwt-token",
    });
  });

  it("buildSseHeaders no agrega Authorization cuando auth está deshabilitada", () => {
    expect(buildSseHeaders(false, "jwt-token")).toEqual({});
    expect(buildSseHeaders(true, null)).toEqual({});
  });

  it("buildSseHeaders incluye Last-Event-ID cuando hay cursor", () => {
    expect(buildSseHeaders(false, null, "42")).toEqual({
      "Last-Event-ID": "42",
    });
  });

  it("Parser_extrae_id_SSE", () => {
    const parser = new SseParser();
    const events = parser.feed("id: 99\nevent: vehicle-update\ndata: {}\n\n");
    expect(events[0]?.id).toBe("99");
    expect(events[0]?.event).toBe("vehicle-update");
  });

  it("Parser_soporta_id_fragmentado_entre_chunks", () => {
    const parser = new SseParser();
    parser.feed("id: 12");
    const events = parser.feed("34\nevent: alert\ndata: {}\n\n");
    expect(events[0]?.id).toBe("1234");
  });

  it("Parser_soporta_retry", () => {
    const parser = new SseParser();
    const events = parser.feed("retry: 5000\nevent: heartbeat\ndata: {}\n\n");
    expect(events[0]?.retry).toBe(5000);
  });

  it("SseParser interpreta connected, fleet-update, alert y heartbeat", () => {
    const parser = new SseParser();
    const payload = [
      "event: connected",
      'data: {"status":"connected"}',
      "",
      "event: fleet-update",
      'data: [{"vehicleId":"VH-001","status":"online"}]',
      "",
      "event: alert",
      'data: {"alertId":"a1","vehicleId":"VH-001","alertType":"overspeed","severity":"critical","message":"x","createdAt":"2026-07-11T00:00:00Z","isAcknowledged":false}',
      "",
      "event: heartbeat",
      'data: {"status":"ok"}',
      "",
    ].join("\n");

    const events = [...parser.feed(payload), ...parser.flush()];
    expect(events.map((e) => e.event)).toEqual(["connected", "fleet-update", "alert", "heartbeat"]);
  });

  it("SseParser concatena múltiples líneas data:", () => {
    const parser = new SseParser();
    const events = parser.feed("event: alert\ndata: line-1\ndata: line-2\n\n");
    expect(events).toHaveLength(1);
    expect(events[0].data).toBe("line-1\nline-2");
  });

  it("consumeSseFetchStream envía Authorization y Accept en fetch", async () => {
    const calls: RequestInit[] = [];
    const encoder = new TextEncoder();
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(encoder.encode("event: connected\ndata: {}\n\n"));
        controller.close();
      },
    });

    global.fetch = (async (_input, init) => {
      calls.push(init ?? {});
      return new Response(stream, {
        status: 200,
        headers: { "Content-Type": "text/event-stream" },
      });
    }) as typeof fetch;

    const events: string[] = [];
    const controller = new AbortController();
    await consumeSseFetchStream(
      "http://localhost:5000/api/events/stream",
      {
        headers: { Authorization: "Bearer test-jwt" },
        signal: controller.signal,
      },
      {
        onEvent: ({ event }) => events.push(event),
      },
    );

    expect(calls[0]?.headers).toMatchObject({
      Accept: "text/event-stream",
      Authorization: "Bearer test-jwt",
    });
    expect(events).toEqual(["connected"]);
  });

  it("consumeSseFetchStream lanza SseAuthError en 401 sin leer el stream", async () => {
    global.fetch = (async () =>
      new Response("unauthorized", { status: 401 })) as typeof fetch;

    const controller = new AbortController();
    await expect(
      consumeSseFetchStream(
        "http://localhost:5000/api/events/stream",
        { signal: controller.signal },
        { onEvent: () => undefined },
      ),
    ).rejects.toSatisfy((error: unknown) => isSseAuthError(error) && (error as SseAuthError).status === 401);
  });

  it("computeReconnectDelayMs incrementa el backoff", () => {
    expect(computeReconnectDelayMs(0)).toBeGreaterThanOrEqual(1_000);
    expect(computeReconnectDelayMs(3)).toBeGreaterThan(computeReconnectDelayMs(0));
  });
});

describe("SseParser.flush FT-001", () => {
  it("publica un único evento cuando el stream termina sin salto de línea final", () => {
    const parser = new SseParser();
    parser.feed("event: alert\ndata: line-1\ndata: line-2");
    const events = parser.flush();

    expect(events).toHaveLength(1);
    expect(events[0]).toEqual({ event: "alert", data: "line-1\nline-2" });
  });

  it("publica múltiples líneas data sin separador final como un solo evento", () => {
    const parser = new SseParser();
    parser.feed("event: heartbeat\ndata: a\ndata: b\ndata: c");
    const events = parser.flush();

    expect(events).toHaveLength(1);
    expect(events[0].data).toBe("a\nb\nc");
  });

  it("incorpora línea final dividida entre dos chunks", () => {
    const parser = new SseParser();
    parser.feed("event: alert\ndata: line-");
    parser.feed("1\ndata: line-2");
    const events = parser.flush();

    expect(events).toHaveLength(1);
    expect(events[0]).toEqual({ event: "alert", data: "line-1\nline-2" });
  });

  it("ignora comentarios SSE iniciados con :", () => {
    const parser = new SseParser();
    parser.feed(": comentario\nevent: connected\ndata: ok");
    const events = parser.flush();

    expect(events).toHaveLength(1);
    expect(events[0]).toEqual({ event: "connected", data: "ok" });
  });

  it("procesa líneas CRLF correctamente", () => {
    const parser = new SseParser();
    const events = [...parser.feed("event: connected\r\ndata: {}\r\n\r\n"), ...parser.flush()];

    expect(events).toHaveLength(1);
    expect(events[0]).toEqual({ event: "connected", data: "{}" });
  });

  it("flush repetido no genera eventos duplicados", () => {
    const parser = new SseParser();
    parser.feed("event: heartbeat\ndata: ping");
    const first = parser.flush();
    const second = parser.flush();

    expect(first).toHaveLength(1);
    expect(second).toHaveLength(0);
  });
});

