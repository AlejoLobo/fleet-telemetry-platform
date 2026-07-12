import {
  configureAuthTokenStore,
  handleUnauthorizedFromApi,
  initializeAuthSession,
  login,
  logout,
  markForbiddenFromApi,
  resetAuthServiceForTests,
  getAuthSessionSnapshot,
} from "@/services/auth-service";
import { InMemoryAuthTokenStore } from "@/services/auth-token-store";
import { getAuthRuntimeSnapshot, setAuthRuntimeSnapshot } from "@/services/auth-runtime";

const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

describe("auth-service", () => {
  beforeEach(() => {
    resetAuthServiceForTests();
    configureAuthTokenStore(new InMemoryAuthTokenStore());
    mockFetch.mockReset();
    setAuthRuntimeSnapshot({ enabled: false, token: null, tokenExpired: false });
  });

  it("Auth_deshabilitada_permite_sincronizacion_sin_Authorization", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: false }) });
    const snapshot = await initializeAuthSession();
    expect(snapshot.status).toBe("auth_disabled");
    expect(getAuthRuntimeSnapshot().enabled).toBe(false);
  });

  it("Login_correcto_persiste_token_y_expiracion_en_SecureStore", async () => {
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    mockFetch
      .mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ token: "jwt-token", expiresInMinutes: 30 }),
      });

    await initializeAuthSession();
    await login("admin", "admin123");
    const persisted = await store.load();
    expect(persisted?.token).toBe("jwt-token");
    expect(persisted?.expiresAtIso).toBeTruthy();
    expect(getAuthSessionSnapshot().status).toBe("authenticated");
  });

  it("Logout_elimina_token_pero_no_modifica_cola", async () => {
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    await store.save({ token: "jwt", expiresAtIso: new Date(Date.now() + 60_000).toISOString() });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();
    await logout();
    expect(await store.load()).toBeNull();
  });

  it("Token_expirado_produce_auth_required", async () => {
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    await store.save({ token: "jwt", expiresAtIso: new Date(Date.now() - 60_000).toISOString() });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    const snapshot = await initializeAuthSession();
    expect(snapshot.status).toBe("session_expired");
  });

  it("Respuesta_401_elimina_token", async () => {
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    await store.save({ token: "jwt", expiresAtIso: new Date(Date.now() + 60_000).toISOString() });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();
    await handleUnauthorizedFromApi();
    expect(await store.load()).toBeNull();
    expect(getAuthSessionSnapshot().status).toBe("auth_required");
  });

  it("Respuesta_403_conserva_token_y_cola", async () => {
    const store = new InMemoryAuthTokenStore();
    configureAuthTokenStore(store);
    await store.save({ token: "jwt", expiresAtIso: new Date(Date.now() + 60_000).toISOString() });
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => ({ enabled: true }) });
    await initializeAuthSession();
    markForbiddenFromApi("forbidden");
    expect(await store.load()).not.toBeNull();
    expect(getAuthSessionSnapshot().status).toBe("forbidden");
  });
});
