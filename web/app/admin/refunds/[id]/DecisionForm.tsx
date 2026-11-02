"use client";

import { useState } from "react";
import { call, describe } from "@/lib/client";

// EP-64 for a Submitted request, EP-65 for a Failed one.
export function DecisionForm({ id, status, needsOverride }: { id: string; status: string; needsOverride: boolean }) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function decide(form: FormData) {
    setBusy(true);
    setError(null);
    const result = await call(`/admin/refund-requests/${id}/decision`, {
      method: "POST",
      idempotent: true,
      body: { decision: form.get("decision"), note: form.get("note"), overrideReason: form.get("overrideReason") || null },
    });
    if (result.ok) {
      window.location.reload();
      return;
    }

    setError(describe(result.problem));
    setBusy(false);
  }

  async function retry() {
    setBusy(true);
    setError(null);
    const result = await call(`/admin/refund-requests/${id}/retry`, { method: "POST" });
    if (result.ok) {
      window.location.reload();
      return;
    }

    setError(describe(result.problem));
    setBusy(false);
  }

  if (status === "Failed") {
    return (
      <div className="card stack">
        {error && <p className="error">{error}</p>}
        <button onClick={retry} disabled={busy}>Retry the refund</button>
      </div>
    );
  }

  if (status !== "Submitted") {
    return null;
  }

  return (
    <form className="card stack" action={decide} style={{ maxWidth: "none" }}>
      <h2 style={{ marginTop: 0 }}>Decide</h2>
      {error && <p className="error">{error}</p>}
      <div className="row">
        <label className="row" style={{ fontWeight: 400 }}><input type="radio" name="decision" value="Approved" required style={{ width: "auto" }} /> Approve</label>
        <label className="row" style={{ fontWeight: 400 }}><input type="radio" name="decision" value="Rejected" style={{ width: "auto" }} /> Reject</label>
      </div>
      <div>
        <label htmlFor="note">Note to the buyer</label>
        <textarea id="note" name="note" rows={3} required maxLength={1000} />
      </div>
      {needsOverride && (
        <div>
          <label htmlFor="overrideReason">Override reason (required to approve: the request is outside the refund window)</label>
          <textarea id="overrideReason" name="overrideReason" rows={2} maxLength={1000} />
        </div>
      )}
      <button type="submit" disabled={busy}>{busy ? "Saving…" : "Save decision"}</button>
    </form>
  );
}
