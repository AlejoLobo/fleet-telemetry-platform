/** Panel de chat con el agente IA operativo. */
"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import { Bot, Send, Sparkles, User } from "lucide-react";
import { useAiChat } from "@/hooks/use-ai-chat";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { etiquetaHerramientaIa } from "@/lib/labels";
import { cn } from "@/lib/utils";

const SUGGESTED_QUESTIONS = [
  "¿Qué vehículos tienen alertas críticas?",
  "Resumen de la flota",
  "¿Cuáles están detenidos?",
  "Vehículos por encima de 80 km/h",
];

/** Interfaz de chat con sugerencias y fuentes de respuesta. */
export function AiChatPanel({ useDemoResponses = false }: { useDemoResponses?: boolean }) {
  const { messages, loading, error, sendMessage } = useAiChat(useDemoResponses);
  const [question, setQuestion] = useState("");
  const messagesViewportRef = useRef<HTMLDivElement>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  // Scroll interno al último mensaje; no mueve la página ni agranda la card.
  useEffect(() => {
    const viewport = messagesViewportRef.current;
    if (!viewport) return;
    viewport.scrollTo({ top: viewport.scrollHeight, behavior: "smooth" });
  }, [messages, loading, error]);

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const value = question;
    setQuestion("");
    await sendMessage(value);
  };

  const askSuggestion = (q: string) => {
    setQuestion("");
    void sendMessage(q);
  };

  return (
    <Card className="flex h-[32rem] max-h-[32rem] flex-col overflow-hidden">
      <CardHeader className="shrink-0 border-b border-border bg-gradient-to-r from-violet-50 via-white to-sky-50">
        <CardTitle className="flex items-center gap-2 text-lg">
          <div className="flex h-8 w-8 items-center justify-center rounded-xl bg-gradient-to-br from-violet-500 to-primary text-white shadow-soft">
            <Sparkles className="h-4 w-4" />
          </div>
          Agente IA operativo
        </CardTitle>
        <CardDescription>
          Consultas en lenguaje natural sobre tu flota · respuestas en español
        </CardDescription>
      </CardHeader>
      <CardContent className="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden p-5">
        <div
          ref={messagesViewportRef}
          className="custom-scrollbar min-h-0 flex-1 space-y-4 overflow-y-auto overscroll-contain rounded-xl border border-border bg-slate-50 p-4"
        >
          {messages.length === 0 && (
            <div className="flex h-full min-h-[12rem] flex-col items-center justify-center py-6 text-center">
              <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-gradient-to-br from-violet-100 to-sky-100">
                <Bot className="h-8 w-8 text-violet-500" />
              </div>
              <p className="text-sm font-medium text-slate-700">¿En qué puedo ayudarte?</p>
              <p className="mt-1 max-w-xs text-xs text-muted-foreground">
                Pregunta sobre alertas, estado de vehículos, velocidad o resumen operativo
              </p>
              <div className="mt-5 flex flex-wrap justify-center gap-2">
                {SUGGESTED_QUESTIONS.map((q) => (
                  <button
                    key={q}
                    type="button"
                    onClick={() => askSuggestion(q)}
                    disabled={loading}
                    className="rounded-full border border-violet-200 bg-white px-3 py-1.5 text-xs text-violet-700 transition-colors hover:border-violet-300 hover:bg-violet-50 disabled:opacity-50"
                  >
                    {q}
                  </button>
                ))}
              </div>
            </div>
          )}
          {messages.map((message, index) => (
            <div
              key={index}
              className={cn(
                "flex gap-3 animate-fade-up",
                message.role === "user" ? "flex-row-reverse" : "flex-row",
              )}
            >
              <div
                className={cn(
                  "flex h-8 w-8 shrink-0 items-center justify-center rounded-xl",
                  message.role === "user"
                    ? "bg-primary/10 text-primary"
                    : "bg-violet-100 text-violet-600",
                )}
              >
                {message.role === "user" ? (
                  <User className="h-4 w-4" />
                ) : (
                  <Bot className="h-4 w-4" />
                )}
              </div>
              <div
                className={cn(
                  "max-w-[85%] rounded-2xl px-4 py-3 text-sm leading-relaxed shadow-soft",
                  message.role === "user"
                    ? "bg-primary text-primary-foreground"
                    : "border border-border bg-white text-slate-700",
                )}
              >
                <p className="whitespace-pre-wrap">{message.content}</p>
                {message.sources && message.sources.length > 0 && (
                  <div className="mt-2 flex flex-wrap gap-1 border-t border-border pt-2">
                    {message.sources.map((s) => (
                      <span
                        key={s}
                        className="rounded-md bg-slate-100 px-2 py-0.5 text-[10px] font-medium text-slate-500"
                      >
                        {etiquetaHerramientaIa(s)}
                      </span>
                    ))}
                  </div>
                )}
              </div>
            </div>
          ))}
          {loading && (
            <div className="flex gap-3">
              <div className="flex h-8 w-8 items-center justify-center rounded-xl bg-violet-100">
                <Bot className="h-4 w-4 text-violet-600" />
              </div>
              <div className="rounded-2xl border border-border bg-white px-4 py-3">
                <div className="flex gap-1">
                  <span className="h-2 w-2 animate-bounce rounded-full bg-slate-300 [animation-delay:0ms]" />
                  <span className="h-2 w-2 animate-bounce rounded-full bg-slate-300 [animation-delay:150ms]" />
                  <span className="h-2 w-2 animate-bounce rounded-full bg-slate-300 [animation-delay:300ms]" />
                </div>
              </div>
            </div>
          )}
          <div ref={messagesEndRef} className="h-px w-full shrink-0" aria-hidden />
        </div>

        {error && (
          <p className="shrink-0 rounded-lg border border-red-100 bg-red-50 px-3 py-2 text-sm text-red-600">
            {error}
          </p>
        )}

        <form onSubmit={onSubmit} className="flex shrink-0 gap-2">
          <Input
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
            placeholder="Escribe tu consulta sobre la flota…"
            disabled={loading}
            className="h-11 rounded-xl border-slate-200 bg-white focus-visible:ring-primary"
          />
          <Button type="submit" disabled={loading || !question.trim()} size="icon" className="h-11 w-11 shrink-0">
            <Send className="h-4 w-4" />
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
