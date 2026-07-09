"use client";

import { useState } from "react";
import { apiClient } from "@/lib/api-client";
import type { AiQueryResponse } from "@/types/fleet";

type ChatMessage = {
  role: "user" | "assistant";
  content: string;
  sources?: string[];
};

export function useAiChat() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const sendMessage = async (question: string) => {
    if (!question.trim()) return;

    setMessages((prev) => [...prev, { role: "user", content: question.trim() }]);
    setLoading(true);
    setError(null);

    try {
      const response: AiQueryResponse = await apiClient.queryAi(question.trim());
      setMessages((prev) => [
        ...prev,
        { role: "assistant", content: response.answer, sources: response.sources },
      ]);
    } catch {
      setError("No se pudo consultar al agente IA.");
      setMessages((prev) => [
        ...prev,
        {
          role: "assistant",
          content: "Error al consultar el agente. Verifica que el backend esté activo.",
        },
      ]);
    } finally {
      setLoading(false);
    }
  };

  return { messages, loading, error, sendMessage };
}
