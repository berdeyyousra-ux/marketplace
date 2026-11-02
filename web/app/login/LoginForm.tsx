"use client";

import Link from "next/link";
import { useState } from "react";
import { describe } from "@/lib/client";

export function LoginForm({ next }: { next: string }) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(form: FormData) {
    setBusy(true);
    setError(null);
    const response = await fetch("/api/session/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: form.get("email"), password: form.get("password") }),
    });
    if (response.ok) {
      window.location.href = next;
      return;
    }

    setError(describe(await response.json().catch(() => ({}))));
    setBusy(false);
  }

  return (
    <form className="stack" action={submit}>
      {error && <p className="error">{error}</p>}
      <div>
        <label htmlFor="email">Email</label>
        <input id="email" name="email" type="email" autoComplete="email" required />
      </div>
      <div>
        <label htmlFor="password">Password</label>
        <input id="password" name="password" type="password" autoComplete="current-password" required />
      </div>
      <button type="submit" disabled={busy}>{busy ? "Signing in…" : "Sign in"}</button>
      <p className="small muted">No account yet? <Link href="/register">Create one</Link>.</p>
    </form>
  );
}
