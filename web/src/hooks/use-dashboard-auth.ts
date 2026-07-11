"use client";

import { useCallback, useEffect, useState } from "react";
import { apiClient } from "@/lib/api-client";

export function useDashboardAuth() {
  const [authEnabled, setAuthEnabled] = useState(false);
  const [hasToken, setHasToken] = useState(false);
  const [authNotice, setAuthNotice] = useState<string | null>(null);

  const refreshAuthState = useCallback(async () => {
    try {
      const status = await apiClient.fetchAuthStatus();
      setAuthEnabled(status.enabled);
      setHasToken(apiClient.hasAuthToken());
    } catch {
      setAuthEnabled(false);
    }
  }, []);

  useEffect(() => {
    void refreshAuthState();
  }, [refreshAuthState]);

  const onAuthChange = useCallback(() => {
    setHasToken(apiClient.hasAuthToken());
    setAuthNotice(null);
  }, []);

  return {
    authEnabled,
    hasToken,
    authNotice,
    setAuthNotice,
    onAuthChange,
    refreshAuthState,
  };
}
