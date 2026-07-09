"use client";

import { useState } from "react";
import { KeyRound, LogOut } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { apiClient } from "@/lib/api-client";

type LoginPanelProps = {
  hasToken: boolean;
  onAuthChange: () => void;
};

export function LoginPanel({ hasToken, onAuthChange }: LoginPanelProps) {
  const [username, setUsername] = useState("admin");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleLogin = async () => {
    setLoading(true);
    setError(null);
    try {
      await apiClient.login(username, password);
      onAuthChange();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo iniciar sesión");
    } finally {
      setLoading(false);
    }
  };

  const handleLogout = () => {
    apiClient.setAuthToken(null);
    onAuthChange();
    setPassword("");
    setError(null);
  };

  return (
    <Card className="border-primary/20 shadow-soft">
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2 text-base">
          <KeyRound className="h-4 w-4 text-primary" />
          Autenticación JWT
        </CardTitle>
        <CardDescription>
          El backend tiene Auth habilitado. Inicia sesión para ingesta y confirmación de alertas.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {hasToken ? (
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm text-emerald-700">Sesión activa</span>
            <Button variant="outline" size="sm" onClick={handleLogout}>
              <LogOut className="h-4 w-4" />
              Cerrar sesión
            </Button>
          </div>
        ) : (
          <>
            <div className="grid gap-2 sm:grid-cols-2">
              <Input
                placeholder="Usuario"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoComplete="username"
              />
              <Input
                type="password"
                placeholder="Contraseña"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
              />
            </div>
            {error && <p className="text-sm text-red-600">{error}</p>}
            <Button size="sm" onClick={handleLogin} disabled={loading || !password}>
              {loading ? "Ingresando…" : "Iniciar sesión"}
            </Button>
          </>
        )}
      </CardContent>
    </Card>
  );
}
