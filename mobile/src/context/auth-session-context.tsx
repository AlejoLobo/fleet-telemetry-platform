import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import {
  canSyncTelemetryForDevice,
  enrollDevice,
  getAuthSessionSnapshot,
  initializeAuthSession,
  login,
  logout,
  subscribeAuthSession,
  validateSessionForLocalDevice,
  type AuthSessionSnapshot,
} from "@/services/auth-service";

type AuthSessionContextValue = AuthSessionSnapshot & {
  enrollDevice: (
    deviceId: string,
    username: string,
    password: string,
  ) => Promise<AuthSessionSnapshot>;
  login: (username: string, password: string) => Promise<AuthSessionSnapshot>;
  logout: () => Promise<AuthSessionSnapshot>;
  refresh: () => Promise<AuthSessionSnapshot>;
  validateSessionForLocalDevice: (deviceId: string | null) => Promise<AuthSessionSnapshot>;
  isAuthenticated: boolean;
  canSync: boolean;
  canSyncForDevice: (deviceId: string | null) => boolean;
};

const AuthSessionContext = createContext<AuthSessionContextValue | null>(null);

export function AuthSessionProvider({ children }: { children: ReactNode }) {
  const [snapshot, setSnapshot] = useState<AuthSessionSnapshot>(getAuthSessionSnapshot());

  useEffect(() => {
    initializeAuthSession().catch(() => undefined);
    return subscribeAuthSession(setSnapshot);
  }, []);

  const value = useMemo<AuthSessionContextValue>(() => {
    const canSyncForDevice = (deviceId: string | null) =>
      canSyncTelemetryForDevice(deviceId);

    return {
      ...snapshot,
      enrollDevice,
      login,
      logout,
      refresh: initializeAuthSession,
      validateSessionForLocalDevice,
      isAuthenticated: snapshot.status === "authenticated",
      canSync:
        snapshot.status === "auth_disabled"
        || (
          snapshot.status === "authenticated"
          && snapshot.sessionKind === "device"
          && snapshot.permissions.includes("telemetry:write")
          && Boolean(snapshot.deviceId)
        ),
      canSyncForDevice,
    };
  }, [snapshot]);

  return <AuthSessionContext.Provider value={value}>{children}</AuthSessionContext.Provider>;
}

export function useAuthSession(): AuthSessionContextValue {
  const context = useContext(AuthSessionContext);
  if (!context) throw new Error("useAuthSession debe usarse dentro de AuthSessionProvider");
  return context;
}
