"use client";

import { createContext, useCallback, useContext, useState } from "react";
import { cn } from "@/lib/cn";

type ToastItem = { id: number; tone: "success" | "error"; message: string };
const ToastContext = createContext<(tone: ToastItem["tone"], message: string) => void>(() => {});

// Announced through aria-live; dismissed after five seconds or on click. Never alert().
export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const show = useCallback((tone: ToastItem["tone"], message: string) => {
    const id = Date.now() + Math.random();
    setToasts((current) => [...current, { id, tone, message }]);
    setTimeout(() => setToasts((current) => current.filter((toast) => toast.id !== id)), 5000);
  }, []);

  return (
    <ToastContext.Provider value={show}>
      {children}
      <div aria-live="polite" className="pointer-events-none fixed inset-x-0 bottom-6 z-50 flex flex-col items-center gap-2 px-6">
        {toasts.map((toast) => (
          <button
            key={toast.id}
            type="button"
            onClick={() => setToasts((current) => current.filter((item) => item.id !== toast.id))}
            className={cn(
              "pointer-events-auto rounded-control border bg-surface px-4 py-3 type-label shadow-raised",
              toast.tone === "success" ? "border-success text-success" : "border-danger text-danger",
            )}
          >
            {toast.message}
          </button>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  return useContext(ToastContext);
}

// A toast shown as it would appear, for the design-system page.
export function ToastPreview({ tone, message }: { tone: ToastItem["tone"]; message: string }) {
  return (
    <div className={cn("inline-block rounded-control border bg-surface px-4 py-3 type-label shadow-raised", tone === "success" ? "border-success text-success" : "border-danger text-danger")}>
      {message}
    </div>
  );
}
