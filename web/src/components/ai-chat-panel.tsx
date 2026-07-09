"use client";

import { FormEvent, useState } from "react";
import { useAiChat } from "@/hooks/use-ai-chat";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";

export function AiChatPanel() {
  const { messages, loading, error, sendMessage } = useAiChat();
  const [question, setQuestion] = useState("");

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const value = question;
    setQuestion("");
    await sendMessage(value);
  };

  return (
    <Card className="flex h-full flex-col">
      <CardHeader>
        <CardTitle>Agente IA operativo</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col gap-3">
        <div className="min-h-48 flex-1 space-y-3 overflow-y-auto rounded-md border border-border p-3">
          {messages.length === 0 && (
            <p className="text-sm text-muted-foreground">
              Pregunta, por ejemplo: &quot;¿Qué vehículos tienen alertas críticas?&quot;
            </p>
          )}
          {messages.map((message, index) => (
            <div
              key={index}
              className={`rounded-md p-2 text-sm ${
                message.role === "user" ? "bg-primary/10 ml-8" : "bg-muted mr-8"
              }`}
            >
              <p className="whitespace-pre-wrap">{message.content}</p>
              {message.sources && (
                <p className="mt-1 text-xs text-muted-foreground">
                  Tools: {message.sources.join(", ")}
                </p>
              )}
            </div>
          ))}
        </div>
        {error && <p className="text-sm text-red-600">{error}</p>}
        <form onSubmit={onSubmit} className="flex gap-2">
          <Input
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
            placeholder="Consulta operativa sobre la flota..."
            disabled={loading}
          />
          <Button type="submit" disabled={loading}>
            {loading ? "..." : "Enviar"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
