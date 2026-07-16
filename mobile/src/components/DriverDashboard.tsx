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
import { getDefaultDriverId } from "@/config/env";
import { useAuthSession } from "@/hooks/use-auth-session";
import { useDriverTelemetry } from "@/hooks/use-driver-telemetry";
import { loadOrCreateDeviceId, DeviceIdentityStorageError } from "@/services/device-id-store";
import { loadCachedVehicleName, loadCachedVehicleType } from "@/services/device-profile-store";
import { ensureDeviceRegistered, updateVehicleProfile } from "@/services/device-registry";
import {
  DEFAULT_VEHICLE_TYPE,
  VEHICLE_TYPES,
  vehicleTypeLabel,
  type VehicleType,
} from "@/types/vehicle";

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

/** Reglas de habilitación del botón de tracking (comprobables en tests). */
export function isStartTrackingDisabled(options: {
  busy: boolean;
  deviceIdReady: boolean;
  deviceId: string | null;
}): boolean {
  return options.busy || !options.deviceIdReady || !options.deviceId;
}

export function DriverDashboard() {
  const [driverId, setDriverId] = useState(getDefaultDriverId());
  const [vehicleName, setVehicleName] = useState("");
  const [vehicleNameDraft, setVehicleNameDraft] = useState("");
  const [vehicleType, setVehicleType] = useState<VehicleType>(DEFAULT_VEHICLE_TYPE);
  const [vehicleTypeDraft, setVehicleTypeDraft] = useState<VehicleType>(DEFAULT_VEHICLE_TYPE);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [loginError, setLoginError] = useState<string | null>(null);
  const [nameError, setNameError] = useState<string | null>(null);
  const [profileError, setProfileError] = useState<string | null>(null);
  const [deviceId, setDeviceId] = useState<string | null>(null);
  const [deviceIdReady, setDeviceIdReady] = useState(false);
  const [identityError, setIdentityError] = useState<string | null>(null);

  const auth = useAuthSession();
  const canSync = auth.canSyncForDevice(deviceId);
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
    deviceId ?? "",
    driverId.trim(),
    canSync,
  );

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const [loadedDeviceId, cachedName, cachedType] = await Promise.all([
          loadOrCreateDeviceId(),
          loadCachedVehicleName(),
          loadCachedVehicleType(),
        ]);
        if (cancelled) return;
        setDeviceId(loadedDeviceId);
        setDeviceIdReady(true);
        setIdentityError(null);
        setVehicleType(cachedType);
        setVehicleTypeDraft(cachedType);
        if (cachedName) {
          setVehicleName(cachedName);
          setVehicleNameDraft(cachedName);
        }
      } catch (error) {
        if (cancelled) return;
        // Sin identidad persistente: no registrar ni sincronizar.
        setDeviceId(null);
        setDeviceIdReady(true);
        const message =
          error instanceof DeviceIdentityStorageError
            ? error.message
            : error instanceof Error
              ? error.message
              : "No se pudo cargar la identidad del dispositivo";
        setIdentityError(message);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!deviceIdReady || !deviceId) return;
    void auth.validateSessionForLocalDevice(deviceId);
  }, [deviceIdReady, deviceId, auth.validateSessionForLocalDevice]);

  // Registro remoto cuando hay red y sync permitido; el backend asigna VH-###.
  useEffect(() => {
    if (!deviceId || identityError || !canSync || !isOnline) return;
    let cancelled = false;
    void (async () => {
      try {
        const profile = await ensureDeviceRegistered(deviceId, vehicleType);
        if (!cancelled) {
          setVehicleName(profile.vehicleName);
          setVehicleNameDraft((prev) => (prev.trim() ? prev : profile.vehicleName));
          setVehicleType(profile.vehicleType);
          setVehicleTypeDraft(profile.vehicleType);
          setNameError(null);
          setProfileError(null);
        }
      } catch (e) {
        if (!cancelled) {
          setNameError(e instanceof Error ? e.message : "No se pudo registrar el dispositivo");
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [deviceId, identityError, canSync, isOnline, vehicleType]);

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
    if (!deviceId) {
      setLoginError("DeviceId no disponible para enrolamiento");
      return;
    }
    setBusy(true);
    try {
      await auth.enrollDevice(deviceId, username.trim(), password);
      setPassword("");
    } catch (e) {
      setLoginError(e instanceof Error ? e.message : "Error de enrolamiento");
    } finally {
      setBusy(false);
    }
  };

  const handleSaveProfile = async () => {
    if (!deviceId) return;
    setProfileError(null);
    setNameError(null);
    setBusy(true);
    try {
      const profile = await updateVehicleProfile(deviceId, {
        vehicleName: vehicleNameDraft,
        vehicleType: vehicleTypeDraft,
      });
      setVehicleName(profile.vehicleName);
      setVehicleNameDraft(profile.vehicleName);
      setVehicleType(profile.vehicleType);
      setVehicleTypeDraft(profile.vehicleType);
    } catch (e) {
      setProfileError(e instanceof Error ? e.message : "No se pudo guardar el perfil");
    } finally {
      setBusy(false);
    }
  };

  const syncPausedByAuth = auth.enabled && !canSync;
  const deviceConfigLoading = !deviceIdReady;
  const startDisabled = isStartTrackingDisabled({
    busy,
    deviceIdReady,
    deviceId,
  });
  const nameDirty = vehicleNameDraft.trim() !== vehicleName.trim();
  const typeDirty = vehicleTypeDraft !== vehicleType;
  const profileDirty = nameDirty || typeDirty;
  const canEditProfile = Boolean(deviceId) && canSync && isOnline && !tracking;

  return (
    <ScrollView contentContainerStyle={styles.container}>
      <Text style={styles.title}>Fleet Telemetry — Conductor</Text>
      <Text style={styles.subtitle}>Cola offline-first con SQLite y sync batch</Text>
      <Text style={styles.meta}>Captura automática cada 5 segundos.</Text>

      <View style={styles.card}>
        <Text style={styles.section}>Autenticación</Text>
        <Text style={styles.meta}>{AUTH_STATUS_LABELS[auth.status] ?? auth.status}</Text>
        {auth.statusMessage && <Text style={styles.meta}>{auth.statusMessage}</Text>}
        {syncPausedByAuth && <Text style={styles.warn}>Sincronización pausada por autenticación</Text>}

        {auth.enabled && !canSync && auth.status !== "checking" && auth.status !== "status_error" ? (
          <>
            <Text style={styles.hint}>
              {auth.statusMessage
                ?? "Enrolamiento: emite un token ligado a este DeviceId (no usa el token de operador)."}
            </Text>
            <Text style={styles.label}>Usuario</Text>
            <TextInput style={styles.input} value={username} onChangeText={setUsername} autoCapitalize="none" />
            <Text style={styles.label}>Contraseña</Text>
            <TextInput style={styles.input} value={password} onChangeText={setPassword} secureTextEntry />
            <Button
              title="Enrolar dispositivo"
              onPress={handleLogin}
              disabled={busy || !username || !password || !deviceId}
            />
            {loginError && <Text style={styles.error}>{loginError}</Text>}
          </>
        ) : null}

        {auth.isAuthenticated && (
          <Button title="Cerrar sesión" onPress={() => run(async () => { await auth.logout(); })} disabled={busy} variant="danger" />
        )}
      </View>

      <View style={styles.card}>
        <Text style={styles.section}>Identidad del dispositivo</Text>
        <Text style={styles.label}>DeviceId (inmutable)</Text>
        <Text style={styles.metaReadonly} selectable>
          {identityError
            ? "Identidad no disponible"
            : deviceIdReady && deviceId
              ? deviceId
              : "Cargando…"}
        </Text>
        {identityError && <Text style={styles.error}>{identityError}</Text>}
        <Text style={styles.hint}>
          La identidad técnica no cambia al renombrar. El nombre visible lo asigna el backend.
        </Text>
        <Text style={styles.label}>Nombre del vehículo</Text>
        <TextInput
          style={styles.input}
          value={vehicleNameDraft}
          onChangeText={setVehicleNameDraft}
          placeholder={vehicleName || "Se asigna al registrar"}
          editable={canEditProfile && !busy}
        />
        <Text style={styles.label}>Tipo de vehículo</Text>
        <View style={styles.typeGrid}>
          {VEHICLE_TYPES.map((type) => {
            const selected = vehicleTypeDraft === type;
            return (
              <Pressable
                key={type}
                accessibilityRole="button"
                accessibilityState={{ selected, disabled: !canEditProfile || busy }}
                accessibilityLabel={`Tipo de vehículo: ${vehicleTypeLabel(type)}`}
                disabled={!canEditProfile || busy}
                onPress={() => setVehicleTypeDraft(type)}
                style={[
                  styles.typeChip,
                  selected && styles.typeChipSelected,
                  (!canEditProfile || busy) && styles.typeChipDisabled,
                ]}
              >
                <Text style={[styles.typeChipText, selected && styles.typeChipTextSelected]}>
                  {vehicleTypeLabel(type)}
                </Text>
              </Pressable>
            );
          })}
        </View>
        <Text style={styles.hint}>
          Tipo actual: {vehicleTypeLabel(vehicleType)}. El código enviado es canónico ({vehicleTypeDraft}).
        </Text>
        <Button
          title="Guardar perfil"
          onPress={() => void handleSaveProfile()}
          disabled={
            busy || !canEditProfile || !profileDirty || vehicleNameDraft.trim().length < 2
          }
        />
        {(profileError || nameError) && (
          <Text style={styles.error}>{profileError || nameError}</Text>
        )}
        <Text style={styles.label}>Conductor</Text>
        <TextInput
          style={styles.input}
          value={driverId}
          onChangeText={setDriverId}
          editable={!tracking}
        />
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
          <Button title="Iniciar tracking" onPress={() => run(startTracking)} disabled={startDisabled} />
        ) : (
          <Button title="Detener tracking" onPress={() => run(async () => { await stopTracking(); })} variant="danger" disabled={busy} />
        )}
        <Button
          title="Capturar ahora"
          onPress={() => run(captureOnce)}
          disabled={busy || deviceConfigLoading || !deviceId}
        />
        <Button
          title="Sincronizar cola"
          onPress={() => run(async () => { await syncNow(); })}
          disabled={busy || !isOnline || !canSync || !deviceId}
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
  metaReadonly: {
    fontSize: 12,
    color: "#475569",
    backgroundColor: "#f1f5f9",
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 10,
    fontFamily: "monospace",
  },
  hint: { fontSize: 12, color: "#64748b", marginBottom: 4 },
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
  typeGrid: { flexDirection: "row", flexWrap: "wrap", gap: 8, marginBottom: 4 },
  typeChip: {
    borderWidth: 1,
    borderColor: "#cbd5e1",
    borderRadius: 8,
    paddingHorizontal: 10,
    paddingVertical: 8,
    backgroundColor: "#f8fafc",
  },
  typeChipSelected: { borderColor: "#2563eb", backgroundColor: "#dbeafe" },
  typeChipDisabled: { opacity: 0.5 },
  typeChipText: { fontSize: 12, fontWeight: "600", color: "#334155" },
  typeChipTextSelected: { color: "#1d4ed8" },
  meta: { fontSize: 13, color: "#475569" },
  warn: { fontSize: 13, color: "#92400e", backgroundColor: "#fef3c7", padding: 8, borderRadius: 8 },
  error: { color: "#b91c1c", backgroundColor: "#fee2e2", padding: 12, borderRadius: 8, fontSize: 13 },
});
