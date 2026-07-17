import * as SecureStore from "expo-secure-store";
import type { StoredAuthToken } from "@/types/auth";

const TOKEN_KEY = "fleet.auth.token";
const EXPIRES_KEY = "fleet.auth.expiresAt";

export interface AuthTokenStore {
  load(): Promise<StoredAuthToken | null>;
  save(token: StoredAuthToken): Promise<void>;
  clear(): Promise<void>;
}

export class SecureAuthTokenStore implements AuthTokenStore {
  async load(): Promise<StoredAuthToken | null> {
    const token = await SecureStore.getItemAsync(TOKEN_KEY);
    const expiresAtIso = await SecureStore.getItemAsync(EXPIRES_KEY);
    if (!token || !expiresAtIso) return null;
    return { token, expiresAtIso };
  }

  async save(stored: StoredAuthToken): Promise<void> {
    await SecureStore.setItemAsync(TOKEN_KEY, stored.token);
    await SecureStore.setItemAsync(EXPIRES_KEY, stored.expiresAtIso);
  }

  async clear(): Promise<void> {
    await SecureStore.deleteItemAsync(TOKEN_KEY);
    await SecureStore.deleteItemAsync(EXPIRES_KEY);
  }
}

export class InMemoryAuthTokenStore implements AuthTokenStore {
  private stored: StoredAuthToken | null = null;

  async load(): Promise<StoredAuthToken | null> {
    return this.stored;
  }

  async save(token: StoredAuthToken): Promise<void> {
    this.stored = token;
  }

  async clear(): Promise<void> {
    this.stored = null;
  }
}
