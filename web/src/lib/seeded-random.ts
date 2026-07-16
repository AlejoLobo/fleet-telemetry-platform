/** Generador pseudoaleatorio con semilla (Mulberry32). */

export type RandomSource = () => number;

export function createSeededRandom(seed: number): RandomSource {
  let state = seed >>> 0;

  return () => {
    state += 0x6d2b79f5;
    let value = state;
    value = Math.imul(value ^ (value >>> 15), value | 1);
    value ^= value + Math.imul(value ^ (value >>> 7), value | 61);
    return ((value ^ (value >>> 14)) >>> 0) / 4294967296;
  };
}

export function randomBetween(min: number, max: number, random: RandomSource): number {
  return min + random() * (max - min);
}

export function randomInt(min: number, max: number, random: RandomSource): number {
  return Math.floor(randomBetween(min, max + 1, random));
}

/** UUID v4 sintético a partir de la fuente (válido en forma). */
export function randomUuid(random: RandomSource): string {
  const bytes = new Array<number>(16);
  for (let i = 0; i < 16; i += 1) {
    bytes[i] = Math.floor(random() * 256);
  }
  bytes[6] = (bytes[6]! & 0x0f) | 0x40;
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;
  const hex = bytes.map((b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
