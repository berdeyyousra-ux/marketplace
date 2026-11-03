"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { call, describe, fieldErrors } from "@/lib/client";
import { aiActionLabel, contradicts } from "@/lib/ai";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { Textarea } from "@/components/ui/Field";
import { useToast } from "@/components/ui/Toast";
import { FormAlert } from "@/components/FormAlert";

type Decision = "Approved" | "Rejected";

// EP-64 for a Submitted request, EP-65 for a Failed one. Approve and Reject carry equal weight and neither is chosen
// in advance, whatever the AI suggested; each opens a confirmation. The override reason is asked for when the
// decision goes against the AI, and is required to approve outside the refund window (US-31).
export function DecisionPanel({ id, status, policyVerdict, aiAction }: { id: string; status: string; policyVerdict: string; aiAction: string | null }) {
  const router = useRouter();
  const toast = useToast();
  const [note, setNote] = useState("");
  const [noteError, setNoteError] = useState<string | undefined>();
  const [pending, setPending] = useState<Decision | null>(null);
  const [overrideReason, setOverrideReason] = useState("");
  const [overrideError, setOverrideError] = useState<string | undefined>();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const outsideWindow = policyVerdict === "IneligibleOutsideWindow";
  const overrideRequired = pending === "Approved" && outsideWindow;
  const asksOverride = pending !== null && (overrideRequired || contradicts(pending, aiAction));

  function open(decision: Decision) {
    setError(null);
    if (!note.trim()) {
      setNoteError("Write the note the buyer will receive.");
      return;
    }

    setOverrideError(undefined);
    setPending(decision);
  }

  async function confirm(event: React.FormEvent) {
    event.preventDefault();
    if (!pending) return;
    if (overrideRequired && !overrideReason.trim()) {
      setOverrideError("Required: this request is outside the refund window.");
      return;
    }

    setBusy(true);
    const result = await call(`/admin/refund-requests/${id}/decision`, {
      method: "POST",
      idempotent: true,
      body: { decision: pending, note: note.trim(), overrideReason: asksOverride && overrideReason.trim() ? overrideReason.trim() : null },
    });
    setBusy(false);
    if (!result.ok) {
      const fields = fieldErrors(result.problem);
      if (fields.overrideReason) setOverrideError(fields.overrideReason);
      else if (result.problem.code === "override_reason_required") setOverrideError("Required: this request is outside the refund window.");
      else {
        setPending(null);
        setError(result.status === 409 ? "Someone else decided this request first. Reload to see their decision." : describe(result.problem));
      }
      return;
    }

    setPending(null);
    toast("success", pending === "Approved" ? "Approved. The refund is on its way and the buyer will be emailed." : "Rejected. The buyer will be emailed your note.");
    router.refresh();
  }

  async function retry() {
    setBusy(true);
    setError(null);
    const result = await call(`/admin/refund-requests/${id}/retry`, { method: "POST" });
    setBusy(false);
    if (!result.ok) {
      setError(describe(result.problem));
      return;
    }

    toast("success", "Retrying the refund.");
    router.refresh();
  }

  if (status === "Failed") {
    return (
      <section className="flex flex-col gap-3 rounded-card border border-border bg-surface p-6">
        <h2 className="type-section m-0 text-text">The refund failed</h2>
        <p className="m-0 type-body text-muted">The approval stands; the payment provider didn&apos;t complete the refund. Retrying sends it again.</p>
        {error && <FormAlert>{error}</FormAlert>}
        <div><Button onClick={retry} loading={busy}>Retry the refund</Button></div>
      </section>
    );
  }

  if (status !== "Submitted") return null;

  return (
    <section aria-labelledby="decide" className="flex flex-col gap-4 rounded-card border border-border bg-surface p-6">
      <h2 id="decide" className="type-section m-0 text-text">Your decision</h2>
      {error && <FormAlert>{error}</FormAlert>}
      <Textarea
        label="Note to the buyer"
        hint="Sent with the decision email. Up to 1000 characters."
        rows={3}
        maxLength={1000}
        value={note}
        error={noteError}
        onChange={(event) => { setNote(event.target.value); if (noteError && event.target.value.trim()) setNoteError(undefined); }}
      />
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Button onClick={() => open("Approved")} fullWidth>Approve refund</Button>
        <Button variant="secondary" onClick={() => open("Rejected")} fullWidth>Reject</Button>
      </div>

      <Dialog open={pending !== null} onClose={() => setPending(null)} title={pending === "Approved" ? "Approve this refund?" : "Reject this refund?"}>
        <form className="flex flex-col gap-4" onSubmit={confirm} noValidate>
          <p className="m-0 type-body text-text">
            {pending === "Approved"
              ? "The buyer gets back what they paid for this item, their download access ends, and they're emailed your note. This can't be undone."
              : "The buyer keeps their access and is emailed your note. This can't be undone."}
          </p>
          <p className="m-0 rounded-control bg-bg p-3 type-label text-text">“{note.trim()}”</p>
          {asksOverride && (
            <Textarea
              label={overrideRequired ? "Override reason" : "Why you're deciding against the AI suggestion"}
              hint={overrideRequired
                ? "Required: the request is outside the refund window. Kept in the record, not sent to the buyer."
                : `The AI ${aiActionLabel(aiAction)?.toLowerCase()}. Optional, kept in the record, not sent to the buyer.`}
              rows={2}
              maxLength={1000}
              value={overrideReason}
              error={overrideError}
              onChange={(event) => { setOverrideReason(event.target.value); setOverrideError(undefined); }}
            />
          )}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={() => setPending(null)}>Cancel</Button>
            <Button type="submit" variant={pending === "Rejected" ? "danger" : "primary"} loading={busy}>
              {pending === "Approved" ? "Approve refund" : "Reject"}
            </Button>
          </div>
        </form>
      </Dialog>
    </section>
  );
}
