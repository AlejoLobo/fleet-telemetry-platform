/** Layout raíz: fuente, metadatos y estructura HTML. */
import type { Metadata } from "next";
import { Plus_Jakarta_Sans } from "next/font/google";
import "./globals.css";

const jakarta = Plus_Jakarta_Sans({
  subsets: ["latin"],
  variable: "--font-jakarta",
});

export const metadata: Metadata = {
  title: "Fleet Telemetry — Centro de Control",
  description: "Plataforma de monitoreo y telemetría de flotas en tiempo real",
};

/** Envuelve todas las páginas con estilos globales. */
export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="es">
      <body className={`${jakarta.className} min-h-screen`}>{children}</body>
    </html>
  );
}
