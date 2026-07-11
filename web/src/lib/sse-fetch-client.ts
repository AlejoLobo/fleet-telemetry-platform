/** Parser SSE conforme al estándar (event:, data:, líneas múltiples, separador vacío). */
export type SseParsedEvent = {
  event: string;
  data: string;
};

export class SseParser {
  private buffer = "";
  private eventName = "message";
  private dataLines: string[] = [];

  feed(chunk: string): SseParsedEvent[] {
    this.buffer += chunk;
    return this.drainEvents();
  }

  flush(): SseParsedEvent[] {
    const events: SseParsedEvent[] = [];
    if (this.dataLines.length > 0) {
      events.push({
        event: this.eventName,
        data: this.dataLines.join("\n"),
      });
      this.eventName = "message";
      this.dataLines = [];
    }
    if (this.buffer.length > 0) {
      this.buffer += "\n";
      events.push(...this.drainEvents());
    }
    return events;
  }

  private drainEvents(): SseParsedEvent[] {
    const events: SseParsedEvent[] = [];

    while (true) {
      const newlineIndex = this.buffer.indexOf("\n");
      if (newlineIndex < 0) break;

      let line = this.buffer.slice(0, newlineIndex);
      this.buffer = this.buffer.slice(newlineIndex + 1);
      if (line.endsWith("\r")) line = line.slice(0, -1);

      if (line === "") {
        if (this.dataLines.length > 0) {
          events.push({
            event: this.eventName,
            data: this.dataLines.join("\n"),
          });
        }
        this.eventName = "message";
        this.dataLines = [];
        continue;
      }

      if (line.startsWith(":")) continue;

      const colonIndex = line.indexOf(":");
      const field = colonIndex === -1 ? line : line.slice(0, colonIndex);
      let value = colonIndex === -1 ? "" : line.slice(colonIndex + 1);
      if (value.startsWith(" ")) value = value.slice(1);

      if (field === "event") this.eventName = value;
      else if (field === "data") this.dataLines.push(value);
    }

    return events;
  }
}

export class SseAuthError extends Error {
  readonly status: number;

  constructor(status: number) {
    super(`SSE rechazado con HTTP ${status}`);
    this.name = "SseAuthError";
    this.status = status;
  }
}

export function isSseAuthError(error: unknown): error is SseAuthError {
  return error instanceof SseAuthError;
}

export type SseFetchHandlers = {
  onEvent: (event: SseParsedEvent) => void;
  onOpen?: () => void;
};

/** Conecta al stream SSE vía fetch con soporte de Authorization y AbortSignal. */
export async function consumeSseFetchStream(
  url: string,
  init: {
    headers?: Record<string, string>;
    signal: AbortSignal;
  },
  handlers: SseFetchHandlers,
): Promise<void> {
  const response = await fetch(url, {
    method: "GET",
    headers: {
      Accept: "text/event-stream",
      ...init.headers,
    },
    signal: init.signal,
  });

  if (response.status === 401 || response.status === 403) {
    throw new SseAuthError(response.status);
  }

  if (!response.ok) {
    throw new Error(`SSE falló con HTTP ${response.status}`);
  }

  if (!response.body) {
    throw new Error("SSE sin cuerpo de respuesta");
  }

  handlers.onOpen?.();

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  const parser = new SseParser();

  while (true) {
    const { done, value } = await reader.read();
    if (done) {
      for (const event of parser.flush()) {
        handlers.onEvent(event);
      }
      break;
    }
    const chunk = decoder.decode(value, { stream: true });
    for (const event of parser.feed(chunk)) {
      handlers.onEvent(event);
    }
  }
}

export function buildSseHeaders(authEnabled: boolean, token: string | null): Record<string, string> {
  if (!authEnabled || !token) return {};
  return { Authorization: `Bearer ${token}` };
}

export function computeReconnectDelayMs(attempt: number, maxMs = 30_000): number {
  const base = Math.min(1_000 * 2 ** attempt, maxMs);
  return base + Math.floor(Math.random() * base * 0.2);
}
