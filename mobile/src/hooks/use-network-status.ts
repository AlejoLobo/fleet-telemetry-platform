// Hook para detectar el estado de conexión de red
import { useEffect, useState } from "react";
import NetInfo from "@react-native-community/netinfo";
import type { NetworkStatus } from "@/types/telemetry";

export function useNetworkStatus() {
  const [status, setStatus] = useState<NetworkStatus>("unknown");

  useEffect(() => {
    // Escucha cambios de conectividad en tiempo real
    const unsubscribe = NetInfo.addEventListener((state) => {
      if (state.isConnected === null) {
        setStatus("unknown");
      } else {
        setStatus(state.isConnected ? "online" : "offline");
      }
    });

    // Consulta el estado inicial al montar
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
