// Utilidades para generar identificadores únicos de eventos
import * as Crypto from "expo-crypto";

// Genera un UUID aleatorio para un evento de telemetría
export async function generateEventId(): Promise<string> {
  return Crypto.randomUUID();
}
