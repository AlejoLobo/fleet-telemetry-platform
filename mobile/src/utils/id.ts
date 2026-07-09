import * as Crypto from "expo-crypto";

export async function generateEventId(): Promise<string> {
  return Crypto.randomUUID();
}
