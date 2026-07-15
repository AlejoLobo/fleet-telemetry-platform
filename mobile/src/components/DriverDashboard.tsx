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
import { useAuthSession } from "@/hooks/use-auth-session";
import { useDriverTelemetry } from "@/hooks/use-driver-telemetry";
import { loadDriverProfile, saveDriverProfile, type DriverProfile } from "@/services/driver-profile";

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

export function DriverDashboard() {
  const [profile, setProfile] = useState<DriverProfile | null>(null);
  const [vehicleName, setVehicleName] = useState("");
  const [driverName, setDriverName] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [loginError, setLoginError] = useState<string | null>(null);
  const [profileError, setProfileError] = useState<string | null>(null);

  const auth = useAuthSession();
  const connected = profile !== null;
  const deviceId = profile?.deviceId ?? "";

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
    deviceId,
    driverName.trim(),
    auth.canSync && Boolean(deviceId),
    vehicleName.trim(),
  );

  const profileEditable = !tracking;

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const loaded = await loadDriverProfile();
        if (cancelled) return;
        setProfile(loaded);
        setVehicleName(loaded.vehicleName);
        setDriverName(loaded.driverName);
      } catch (e) {
        if (!cancelled) {
          setProfileError(e instanceof Error ? e.message : "No se pudo cargar el perfil del dispositivo");
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const persistProfile = async (): Promise<DriverProfile> => {
    const saved = await saveDriverProfile({
      vehicleName,
      driverName,
    });
    setProfile(saved);
    setVehicleName(saved.vehicleName);
    setDriverName(saved.driverName);
    return saved;
  };

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setProfileError(null);
    try {
      await action();
    } finally {
      setBusy(false);
    }
  };

  const ensureProfileBeforeConnect = async () => {
    if (!vehicleName.trim() || !driverName.trim()) {
      throw new Error("Indica nombre del vehículo y del conductor antes de conectarte.");
    }
    await persistProfile();
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
  const locationBadge =
    lastReading?.source === "gps"
      ? { label: "GPS", tone: "ok" as const }
      : lastReading?.source === "simulated"
        ? { label: "Simulado", tone: "neutral" as const }
        : { label: "Sin lectura", tone: "neutral" as const };

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
        <Text style={styles.section}>Perfil del dispositivo</Text>
        <Text style={styles.label}>ID de dispositivo (estable)</Text>
        <TextInput
          style={[styles.input, styles.inputLocked]}
          value={connected ? deviceId : "Cargando..."}
          editable={false}
        />
        <Text style={styles.label}>Nombre del vehículo</Text>
        <TextInput
          style={[styles.input, !profileEditable && styles.inputLocked]}
          value={vehicleName}
          onChangeText={setVehicleName}
          editable={profileEditable && connected}
          placeholder="Ej. Camión norte"
        />
        <Text style={styles.label}>Nombre del conductor</Text>
        <TextInput
          style={[styles.input, !profileEditable && styles.inputLocked]}
          value={driverName}
          onChangeText={setDriverName}
          editable={profileEditable && connected}
          placeholder="Ej. Juan Pérez"
        />
        <Text style={styles.hint}>
          Puedes editar vehículo y conductor solo con el tracking detenido (antes de conectarte al servidor).
          El ID de dispositivo no cambia: al reconectar, el portal reúne el mismo vehículo en línea.
        </Text>
        {profileEditable && (
          <Button
            title="Guardar perfil"
            onPress={() => run(async () => { await persistProfile(); })}
            disabled={busy || !connected || !vehicleName.trim() || !driverName.trim()}
          />
        )}
      </View>

      <View style={styles.row}>
        <Badge label={`Red: ${networkStatus}`} tone={isOnline ? "ok" : "warn"} />
        <Badge label={`Pendientes: ${pendingCount}`} tone={pendingCount > 0 ? "warn" : "ok"} />
        <Badge label={locationBadge.label} tone={locationBadge.tone} />
        <Badge label={tracking ? "Conectado" : "Desconectado"} tone={tracking ? "ok" : "neutral"} />
      </View>

      {(error || profileError) && <Text style={styles.error}>{profileError ?? error}</Text>}

      <View style={styles.actions}>
        {!tracking ? (
          <Button
            title="Iniciar tracking"
            onPress={() => run(async () => {
              await ensureProfileBeforeConnect();
              await startTracking();
            })}
            disabled={busy || !connected}
          />
        ) : (
          <Button title="Detener tracking" onPress={() => run(async () => stopTracking())} variant="danger" disabled={busy} />
        )}
        <Button
          title="Capturar ahora"
          onPress={() => run(async () => {
            await ensureProfileBeforeConnect();
            await captureOnce();
          })}
          disabled={busy || !connected}
        />
        <Button
          title="Sincronizar cola"
          onPress={() => run(async () => { await syncNow(); })}
          disabled={busy || !isOnline || !auth.canSync || !connected}
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
  input: { borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 8, paddingHorizontal: 12, paddingVertical: 10, marginBottom: 8, backgroundColor: "#fff", color: "#0f172a" },
  inputLocked: { backgroundColor: "#f1f5f9", color: "#475569" },
  hint: { fontSize: 12, color: "#64748b", lineHeight: 18 },
  row: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
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
