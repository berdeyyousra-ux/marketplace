"use client";

import { useState } from "react";
import { call, describe } from "@/lib/client";

// EP-50 gives a five-minute signed URL; EP-60 asks for a refund (10 to 2000 characters).
export function LibraryActions({ orderItemId, canDownload, canRequestRefund }: { orderItemId: string; canDownload: boolean; canRequestRefund: boolean }) {
  const [error, setError] = useState<string | null>(null);
  const [refunding, setRefunding] = useState(false);
  const [busy, setBusy] = useState(false);

  async function download() {
    setBusy(true);
    setError(null);
    const result = await call<{ url: string }>(`/downloads/${orderItemId}/link`, { method: "POST" });
    if (result.ok) {
      window.location.href = result.data.url;
      setTimeout(() => window.location.reload(), 1500);
    } else {
      setError(describe(result.problem));
    }

    setBusy(false);
  }

  async function requestRefund(form: FormData) {
    setBusy(true);
    setError(null);
    const result = await call(`/refund-requests`, { method: "POST", body: { orderItemId, reason: form.get("reason") }, idempotent: true });
    if (result.ok) {
      window.location.reload();
      return;
    }

    setError(describe(result.problem));
    setBusy(false);
  }

  return (
    <div className="stack" style={{ minWidth: 220 }}>
      {error && <p className="error small">{error}</p>}
      <div className="row">
        {canDownload && <button onClick={download} disabled={busy}>Download</button>}
        {canRequestRefund && !refunding && <button className="secondary" onClick={() => setRefunding(true)}>Request refund</button>}
      </div>
      {refunding && (
        <form className="stack" action={requestRefund}>
          <label htmlFor={`reason-${orderItemId}`}>What went wrong?</label>
          <textarea id={`reason-${orderItemId}`} name="reason" rows={3} minLength={10} maxLength={2000} required />
          <div className="row">
            <button type="submit" disabled={busy}>Send request</button>
            <button type="button" className="secondary" onClick={() => setRefunding(false)}>Cancel</button>
          </div>
        </form>
      )}
    </div>
  );
}
