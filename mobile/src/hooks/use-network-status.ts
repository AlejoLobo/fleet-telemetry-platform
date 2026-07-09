import { useEffect, useState } from "react";
import NetInfo from "@react-native-community/netinfo";
import type { NetworkStatus } from "@/types/telemetry";

export function useNetworkStatus() {
  const [status, setStatus] = useState<NetworkStatus>("unknown");

  useEffect(() => {
    const unsubscribe = NetInfo.addEventListener((state) => {
      if (state.isConnected === null) {
        setStatus("unknown");
      } else {
        setStatus(state.isConnected ? "online" : "offline");
      }
    });

    NetInfo.fetch().then((state) => {
      setStatus(state.isConnected ? "online" : "offline");
    });

    return unsubscribe;
  }, []);

  return {
    status,
    isOnline: status === "online",
  };
}
