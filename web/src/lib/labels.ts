/** Etiquetas en español y utilidades de localización. */

export function etiquetaEstadoVehiculo(status: string): string {
  switch (status.toLowerCase()) {
    case "online":
      return "En línea";
    case "offline":
      return "Desconectado";
    default:
      return status;
  }
}

/** Traduce la severidad de alerta al español. */
export function etiquetaSeveridad(severity: string): string {
  switch (severity.toLowerCase()) {
    case "critical":
      return "Crítica";
    case "warning":
      return "Advertencia";
    default:
      return severity;
  }
}

/** Traduce el tipo de alerta al español. */
export function etiquetaTipoAlerta(alertType: string): string {
  switch (alertType.toLowerCase()) {
    case "overspeed":
      return "Exceso de velocidad";
    case "low_fuel":
      return "Combustible bajo";
    case "low_battery":
      return "Batería baja";
    default:
      return alertType.replaceAll("_", " ");
  }
}

export function etiquetaFuenteAnalitica(source: string): string {
  if (/mock|demostraci/i.test(source)) return "Datos de demostración";
  if (/timescale/i.test(source)) return "TimescaleDB";
  return source;
}

const HERRAMIENTAS_IA: Record<string, string> = {
  GetStoppedVehicles: "Vehículos detenidos",
  GetVehiclesWithCriticalAlerts: "Alertas críticas",
  GetLatestVehicleStatus: "Estado del vehículo",
  GetVehiclesAboveSpeed: "Exceso de velocidad",
  GetAnalyticsSummary: "Resumen analítico",
  GetFleetOverview: "Resumen de flota",
};

export function etiquetaHerramientaIa(tool: string): string {
  return HERRAMIENTAS_IA[tool] ?? tool;
}

/** Indica si el vehículo está en línea. */
export function esVehiculoEnLinea(status: string): boolean {
  return status.toLowerCase() === "online";
}

/** Indica si la alerta es de severidad crítica. */
export function esSeveridadCritica(severity: string): boolean {
  return severity.toLowerCase() === "critical";
}

/** Traduce mensajes de alerta del inglés al español. */
export function traducirMensajeAlerta(alert: {
  vehicleId: string;
  alertType: string;
  message: string;
}): string {
  const exceso =
    /exceeded speed limit:\s*([\d.,]+)\s*km\/h/i.exec(alert.message) ??
    /superó el límite de velocidad:\s*([\d.,]+)\s*km\/h/i.exec(alert.message);
  if (exceso) {
    const speed = exceso[1].replace(",", ".");
    return `El vehículo ${alert.vehicleId} superó el límite de velocidad: ${speed} km/h`;
  }

  const combustible =
    /has low fuel:\s*([\d.,]+)%/i.exec(alert.message) ??
    /combustible bajo:\s*([\d.,]+)%/i.exec(alert.message);
  if (combustible) {
    const level = combustible[1].replace(",", ".");
    return `El vehículo ${alert.vehicleId} tiene combustible bajo: ${level}%`;
  }

  const bateria =
    /has low battery:\s*([\d.,]+)%/i.exec(alert.message) ??
    /batería baja:\s*([\d.,]+)%/i.exec(alert.message);
  if (bateria) {
    const level = bateria[1].replace(",", ".");
    return `El vehículo ${alert.vehicleId} tiene batería baja: ${level}%`;
  }

  return alert.message;
}

/** Ajusta respuestas del agente IA que aún traigan términos en inglés */
export function localizarRespuestaIa(answer: string): string {
  return answer
    .replace(/\bOnline:\s*/gi, "En línea: ")
    .replace(/\bOffline:\s*/gi, "Desconectado: ")
    .replace(/- Estado:\s*online\b/gi, "- Estado: En línea")
    .replace(/- Estado:\s*offline\b/gi, "- Estado: Desconectado")
    .replace(/\boverspeed\b/gi, "exceso de velocidad")
    .replace(/\blow_fuel\b/gi, "combustible bajo")
    .replace(/\blow_battery\b/gi, "batería baja")
    .replace(/Vehicle\s+(VH-\d+)\s+exceeded speed limit:\s*([\d.,]+)\s*km\/h/gi, (_, id, speed) =>
      `El vehículo ${id} superó el límite de velocidad: ${String(speed).replace(",", ".")} km/h`,
    )
    .replace(/Vehicle\s+(VH-\d+)\s+has low fuel:\s*([\d.,]+)%/gi, (_, id, level) =>
      `El vehículo ${id} tiene combustible bajo: ${String(level).replace(",", ".")}%`,
    )
    .replace(/Vehicle\s+(VH-\d+)\s+has low battery:\s*([\d.,]+)%/gi, (_, id, level) =>
      `El vehículo ${id} tiene batería baja: ${String(level).replace(",", ".")}%`,
    );
}
