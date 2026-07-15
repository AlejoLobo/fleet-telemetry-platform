// Panel principal del conductor: captura y sincroniza telemetría
import { useEffect, useState } from "react";
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";
import { getDefaultDriverId, getDefaultVehicleId } from "@/config/env";
import {
  DEFAULT_TELEMETRY_CAPTURE_INTERVAL_SECONDS,
  TELEMETRY_CAPTURE_INTERVAL_OPTIONS_SECONDS,
  type TelemetryCaptureIntervalSeconds,
} from "@/config/telemetry-capture-rate";
import { useAuthSession } from "@/hooks/use-auth-session";
import { useDriverTelemetry } from "@/hooks/use-driver-telemetry";
import {
  loadCaptureIntervalSeconds,
  saveCaptureIntervalSeconds,
} from "@/services/capture-interval-store";

const AUTH_STATUS_LABELS: Record<string, string> = {
  checking: "Comprobando autenticación...",
  auth_disabled: "Autenticación deshabilitada",
  auth_required: "Login requerido",
  authenticated: "Autenticado",
  session_expired: "Sesión vencida",
  forbidden: "Permiso insuficiente",
  status_error: "Error temporal al consultar auth status",
  auth_status_error: "Auth status desconocido — sincronización bloqueada",
};

const CAPTURE_INTERVAL_LABELS: Record<TelemetryCaptureIntervalSeconds, string> = {
  3: "Cada 3 segundos",
  5: "Cada 5 segundos",
  10: "Cada 10 segundos",
  15: "Cada 15 segundos",
};

export function DriverDashboard() {
  const [vehicleId, setVehicleId] = useState(getDefaultVehicleId());
  const [driverId, setDriverId] = useState(getDefaultDriverId());
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [loginError, setLoginError] = useState<string | null>(null);
  const [captureIntervalSeconds, setCaptureIntervalSeconds] =
    useState<TelemetryCaptureIntervalSeconds>(DEFAULT_TELEMETRY_CAPTURE_INTERVAL_SECONDS);
  const [intervalReady, setIntervalReady] = useState(false);

  const auth = useAuthSession();
  const {
    tracking,
    pendingCount,
    lastReading,
    lastCapturedAt,
    lastSync,
    error,
    networkStatus,
    isOnline,
    startTracking,
    stopTracking,
    captureOnce,
    syncNow,
  } = useDriverTelemetry(
    vehicleId.trim(),
    driverId.trim(),
    auth.canSync,
    captureIntervalSeconds,
  );

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const loaded = await loadCaptureIntervalSeconds();
      if (!cancelled) {
        setCaptureIntervalSeconds(loaded);
        setIntervalReady(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const handleCaptureIntervalChange = async (seconds: TelemetryCaptureIntervalSeconds) => {
    if (tracking) return;
    setCaptureIntervalSeconds(seconds);
    await saveCaptureIntervalSeconds(seconds);
  };

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    try {
      await action();
    } finally {
      setBusy(false);
    }
  };

  const handleLogin = async () => {
    setLoginError(null);
    setBusy(true);
    try {
      await auth.login(username.trim(), password);
      setPassword("");
    } catch (e) {
      setLoginError(e instanceof Error ? e.message : "Error de login");
    } finally {
      setBusy(false);
    }
  };

  const syncPausedByAuth = auth.enabled && !auth.canSync;

  return (
    <ScrollView contentContainerStyle={styles.container}>
      <Text style={styles.title}>Fleet Telemetry — Conductor</Text>
      <Text style={styles.subtitle}>Cola offline-first con SQLite y sync batch</Text>

      <View style={styles.card}>
        <Text style={styles.section}>Autenticación</Text>
        <Text style={styles.meta}>{AUTH_STATUS_LABELS[auth.status] ?? auth.status}</Text>
        {auth.statusMessage && <Text style={styles.meta}>{auth.statusMessage}</Text>}
        {syncPausedByAuth && <Text style={styles.warn}>Sincronización pausada por autenticación</Text>}

        {auth.status === "auth_required" || auth.status === "session_expired" ? (
          <>
            <Text style={styles.label}>Usuario</Text>
            <TextInput style={styles.input} value={username} onChangeText={setUsername} autoCapitalize="none" />
            <Text style={styles.label}>Contraseña</Text>
            <TextInput style={styles.input} value={password} onChangeText={setPassword} secureTextEntry />
            <Button title="Iniciar sesión" onPress={handleLogin} disabled={busy || !username || !password} />
            {loginError && <Text style={styles.error}>{loginError}</Text>}
          </>
        ) : null}

        {auth.isAuthenticated && (
          <Button title="Cerrar sesión" onPress={() => run(async () => { await auth.logout(); })} disabled={busy} variant="danger" />
        )}
      </View>

      <View style={styles.card}>
        <Text style={styles.label}>Vehículo</Text>
        <TextInput style={styles.input} value={vehicleId} onChangeText={setVehicleId} autoCapitalize="characters" />
        <Text style={styles.label}>Conductor</Text>
        <TextInput style={styles.input} value={driverId} onChangeText={setDriverId} />
      </View>

      <View style={styles.card}>
        <Text style={styles.section}>Frecuencia de registro</Text>
        <Text style={styles.meta}>
          Seleccionado: {CAPTURE_INTERVAL_LABELS[captureIntervalSeconds]}
        </Text>
        {!intervalReady && <Text style={styles.meta}>Cargando preferencia…</Text>}
        <View style={styles.intervalRow}>
          {TELEMETRY_CAPTURE_INTERVAL_OPTIONS_SECONDS.map((seconds) => {
            const selected = seconds === captureIntervalSeconds;
            return (
              <Pressable
                key={seconds}
                disabled={tracking || busy}
                onPress={() => {
                  void handleCaptureIntervalChange(seconds);
                }}
                style={[
                  styles.intervalChip,
                  selected && styles.intervalChipSelected,
                  (tracking || busy) && styles.intervalChipDisabled,
                ]}
              >
                <Text style={[styles.intervalChipText, selected && styles.intervalChipTextSelected]}>
                  {CAPTURE_INTERVAL_LABELS[seconds]}
                </Text>
              </Pressable>
            );
          })}
        </View>
        {tracking && (
          <Text style={styles.warn}>Detén el tracking para cambiar la frecuencia de registro.</Text>
        )}
      </View>

      <View style={styles.row}>
        <Badge label={`Red: ${networkStatus}`} tone={isOnline ? "ok" : "warn"} />
        <Badge label={`Pendientes: ${pendingCount}`} tone={pendingCount > 0 ? "warn" : "ok"} />
        <Badge
          label={lastReading?.source === "gps" ? "GPS" : "Simulado"}
          tone={lastReading?.source === "gps" ? "ok" : "neutral"}
        />
      </View>

      {error && <Text style={styles.error}>{error}</Text>}

      <View style={styles.actions}>
        {!tracking ? (
          <Button title="Iniciar tracking" onPress={() => run(startTracking)} disabled={busy} />
        ) : (
          <Button title="Detener tracking" onPress={() => run(async () => { await stopTracking(); })} variant="danger" disabled={busy} />
        )}
        <Button title="Capturar ahora" onPress={() => run(captureOnce)} disabled={busy} />
        <Button
          title="Sincronizar cola"
          onPress={() => run(async () => { await syncNow(); })}
          disabled={busy || !isOnline || !auth.canSync}
        />
      </View>

      {busy && <ActivityIndicator style={styles.loader} />}

      <View style={styles.card}>
        <Text style={styles.section}>Última lectura</Text>
        {lastReading ? (
          <>
            <Text style={styles.meta}>
              {lastReading.latitude.toFixed(5)}, {lastReading.longitude.toFixed(5)}
            </Text>
            <Text style={styles.meta}>Velocidad: {lastReading.speedKmh} km/h</Text>
            <Text style={styles.meta}>Fuente: {lastReading.source}</Text>
          </>
        ) : (
          <Text style={styles.meta}>Sin lecturas aún</Text>
        )}
        {lastCapturedAt && <Text style={styles.meta}>Capturado: {lastCapturedAt}</Text>}
      </View>

      {lastSync && (
        <View style={styles.card}>
          <Text style={styles.section}>Última sincronización</Text>
          <Text style={styles.meta}>Estado: {lastSync.status}</Text>
          <Text style={styles.meta}>Enviados: {lastSync.synced}</Text>
          <Text style={styles.meta}>Fallidos: {lastSync.failed}</Text>
          <Text style={styles.meta}>Restantes: {lastSync.remaining}</Text>
          <Text style={styles.meta}>Reintentos: {lastSync.retried}</Text>
          <Text style={styles.meta}>Fallos permanentes: {lastSync.permanentFailures}</Text>
          {lastSync.retryAt && <Text style={styles.meta}>Próximo intento: {lastSync.retryAt}</Text>}
        </View>
      )}
    </ScrollView>
  );
}

function Badge({ label, tone }: { label: string; tone: "ok" | "warn" | "neutral" }) {
  const bg = tone === "ok" ? "#dcfce7" : tone === "warn" ? "#fef3c7" : "#e2e8f0";
  const color = tone === "ok" ? "#166534" : tone === "warn" ? "#92400e" : "#334155";

  return (
    <View style={[styles.badge, { backgroundColor: bg }]}>
      <Text style={[styles.badgeText, { color }]}>{label}</Text>
    </View>
  );
}

function Button({
  title,
  onPress,
  disabled,
  variant = "primary",
}: {
  title: string;
  onPress: () => void;
  disabled?: boolean;
  variant?: "primary" | "danger";
}) {
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      style={[styles.button, variant === "danger" && styles.buttonDanger, disabled && styles.buttonDisabled]}
    >
      <Text style={styles.buttonText}>{title}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  container: { padding: 20, paddingTop: 56, gap: 16, backgroundColor: "#f8fafc", minHeight: "100%" },
  title: { fontSize: 22, fontWeight: "700", color: "#0f172a" },
  subtitle: { fontSize: 14, color: "#64748b", marginBottom: 4 },
  card: { backgroundColor: "#fff", borderRadius: 12, padding: 16, borderWidth: 1, borderColor: "#e2e8f0", gap: 8 },
  label: { fontSize: 13, fontWeight: "600", color: "#334155" },
  input: { borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 8, paddingHorizontal: 12, paddingVertical: 10, marginBottom: 8, backgroundColor: "#fff" },
  row: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
  intervalRow: { gap: 8 },
  intervalChip: {
    borderWidth: 1,
    borderColor: "#cbd5e1",
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    backgroundColor: "#f8fafc",
  },
  intervalChipSelected: { borderColor: "#2563eb", backgroundColor: "#eff6ff" },
  intervalChipDisabled: { opacity: 0.5 },
  intervalChipText: { fontSize: 13, fontWeight: "600", color: "#334155" },
  intervalChipTextSelected: { color: "#1d4ed8" },
  badge: { borderRadius: 999, paddingHorizontal: 10, paddingVertical: 6 },
  badgeText: { fontSize: 12, fontWeight: "600" },
  actions: { gap: 10 },
  button: { backgroundColor: "#2563eb", borderRadius: 10, paddingVertical: 14, alignItems: "center" },
  buttonDanger: { backgroundColor: "#dc2626" },
  buttonDisabled: { opacity: 0.5 },
  buttonText: { color: "#fff", fontWeight: "600", fontSize: 15 },
  loader: { marginTop: 4 },
  section: { fontSize: 15, fontWeight: "700", color: "#0f172a" },
  meta: { fontSize: 13, color: "#475569" },
  warn: { fontSize: 13, color: "#92400e", backgroundColor: "#fef3c7", padding: 8, borderRadius: 8 },
  error: { color: "#b91c1c", backgroundColor: "#fee2e2", padding: 12, borderRadius: 8, fontSize: 13 },
});
