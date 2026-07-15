import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import {
  canSyncTelemetryForDevice,
  enrollDevice,
  getAuthSessionSnapshot,
  initializeAuthSession,
  login,
  logout,
  subscribeAuthSession,
  type AuthSessionSnapshot,
} from "@/services/auth-service";

type AuthSessionContextValue = AuthSessionSnapshot & {
  /** Enrola el DeviceId local y guarda JWT de dispositivo. */
  enrollDevice: (
    deviceId: string,
    username: string,
    password: string,
  ) => Promise<AuthSessionSnapshot>;
  /** Login de operador (portal); no habilita sync de telemetría. */
  login: (username: string, password: string) => Promise<AuthSessionSnapshot>;
  logout: () => Promise<AuthSessionSnapshot>;
  refresh: () => Promise<AuthSessionSnapshot>;
  isAuthenticated: boolean;
  /** Compat: false si el token no es de dispositivo. Preferir canSyncForDevice. */
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
      isAuthenticated: snapshot.status === "authenticated",
      // Sin DeviceId local aún: solo auth_disabled permite sync anónima.
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
