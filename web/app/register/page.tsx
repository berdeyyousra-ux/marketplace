"use client";

import { useState } from "react";
import { describe } from "@/lib/client";

export default function RegisterPage() {
  const [error, setError] = useState<string | null>(null);
  const [sentTo, setSentTo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(form: FormData) {
    setBusy(true);
    setError(null);
    const email = String(form.get("email"));
    const response = await fetch("/api/session/register", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password: form.get("password"), displayName: form.get("displayName") }),
    });
    if (response.ok) {
      setSentTo(email);
    } else {
      setError(describe(await response.json().catch(() => ({}))));
    }

    setBusy(false);
  }

  if (sentTo) {
    return (
      <div className="stack">
        <h1>Check your email</h1>
        <p className="notice">
          We sent a confirmation link to {sentTo}. Open it to activate the account, then sign in.
          Locally, the email is in Mailpit at http://localhost:8025.
        </p>
      </div>
    );
  }

  return (
    <div className="stack">
      <h1>Create an account</h1>
      <form className="stack" action={submit}>
        {error && <p className="error">{error}</p>}
        <div>
          <label htmlFor="displayName">Name</label>
          <input id="displayName" name="displayName" autoComplete="name" required maxLength={80} />
        </div>
        <div>
          <label htmlFor="email">Email</label>
          <input id="email" name="email" type="email" autoComplete="email" required />
        </div>
        <div>
          <label htmlFor="password">Password</label>
          <input id="password" name="password" type="password" autoComplete="new-password" required minLength={10} maxLength={128} />
          <p className="small muted">At least 10 characters. Common passwords are refused.</p>
        </div>
        <button type="submit" disabled={busy}>{busy ? "Creating…" : "Create account"}</button>
      </form>
    </div>
  );
}
