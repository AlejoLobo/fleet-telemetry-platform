/** Resolución de URL SSE con ticket efímero (FT-001). */

export type SseAuthStatus = {
  enabled: boolean;
};

export type SseTicketResponse = {
  ticket: string;
  expiresInSeconds: number;
};

export function appendSseTicket(baseStreamUrl: string, ticket: string): string {
  const url = new URL(baseStreamUrl);
  url.searchParams.set("ticket", ticket);
  return url.toString();
}

export function shouldRequestSseTicket(authStatus: SseAuthStatus, hasToken: boolean): boolean {
  return authStatus.enabled && hasToken;
}

export async function resolveSseStreamUrl(options: {
  baseUrl: string;
  authStatus: SseAuthStatus;
  hasToken: boolean;
  fetchTicket: () => Promise<SseTicketResponse>;
}): Promise<string> {
  const streamUrl = `${options.baseUrl.replace(/\/$/, "")}/api/events/stream`;

  if (!shouldRequestSseTicket(options.authStatus, options.hasToken)) {
    return streamUrl;
  }

  const ticketResponse = await options.fetchTicket();
  return appendSseTicket(streamUrl, ticketResponse.ticket);
}
