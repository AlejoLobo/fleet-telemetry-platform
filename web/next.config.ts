import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  output: "standalone",
  /** Oculta el botón flotante "N" de herramientas de desarrollo de Next.js (solo en `npm run dev`). */
  devIndicators: false,
};

export default nextConfig;
