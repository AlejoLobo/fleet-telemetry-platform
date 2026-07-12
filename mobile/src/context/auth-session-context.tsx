import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import {
  getAuthSessionSnapshot,
  initializeAuthSession,
  login,
  logout,
  subscribeAuthSession,
  type AuthSessionSnapshot,
} from "@/services/auth-service";

type AuthSessionContextValue = AuthSessionSnapshot & {
  login: (username: string, password: string) => Promise<AuthSessionSnapshot>;
  logout: () => Promise<AuthSessionSnapshot>;
  refresh: () => Promise<AuthSessionSnapshot>;
  isAuthenticated: boolean;
  canSync: boolean;
};

const AuthSessionContext = createContext<AuthSessionContextValue | null>(null);

export function AuthSessionProvider({ children }: { children: ReactNode }) {
  const [snapshot, setSnapshot] = useState<AuthSessionSnapshot>(getAuthSessionSnapshot());

  useEffect(() => {
    initializeAuthSession().catch(() => undefined);
    return subscribeAuthSession(setSnapshot);
  }, []);

  const value = useMemo<AuthSessionContextValue>(() => ({
    ...snapshot,
    login,
    logout,
    refresh: initializeAuthSession,
    isAuthenticated: snapshot.status === "authenticated",
    canSync: snapshot.status === "authenticated" || snapshot.status === "auth_disabled",
  }), [snapshot]);

  return <AuthSessionContext.Provider value={value}>{children}</AuthSessionContext.Provider>;
}

export function useAuthSession(): AuthSessionContextValue {
  const context = useContext(AuthSessionContext);
  if (!context) throw new Error("useAuthSession debe usarse dentro de AuthSessionProvider");
  return context;
}
