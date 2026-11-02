"use client";

import { useState } from "react";
import { call, describe } from "@/lib/client";

// EP-27. The Seller role is in the next access token, so the session is refreshed before reloading.
export function BecomeSellerForm({ defaultName }: { defaultName: string }) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(form: FormData) {
    setBusy(true);
    setError(null);
    const result = await call("/sellers", {
      method: "POST",
      body: { displayName: form.get("displayName"), bio: form.get("bio") || null, country: String(form.get("country")).toUpperCase() },
    });
    if (result.ok) {
      await fetch("/api/session/refresh", { method: "POST" });
      window.location.reload();
      return;
    }

    setError(describe(result.problem));
    setBusy(false);
  }

  return (
    <form className="stack" action={submit}>
      {error && <p className="error">{error}</p>}
      <div>
        <label htmlFor="displayName">Shop name</label>
        <input id="displayName" name="displayName" defaultValue={defaultName} required maxLength={80} />
      </div>
      <div>
        <label htmlFor="bio">About (optional)</label>
        <textarea id="bio" name="bio" rows={3} maxLength={1000} />
      </div>
      <div>
        <label htmlFor="country">Country code</label>
        <input id="country" name="country" required minLength={2} maxLength={2} placeholder="ES" />
      </div>
      <button type="submit" disabled={busy}>{busy ? "Creating…" : "Become a seller"}</button>
    </form>
  );
}
