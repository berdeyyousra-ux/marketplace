"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { call, describe, fieldErrors } from "@/lib/client";
import { date } from "@/lib/format";
import type { LibraryItem, MyRefundRequest } from "@/lib/types";
import { Badge, StatusBadge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { Textarea } from "@/components/ui/Field";
import { Thumbnail } from "@/components/ui/Thumbnail";
import { useToast } from "@/components/ui/Toast";
import { FormAlert } from "@/components/FormAlert";

// What a refund request's status means to the buyer.
const refundBadges: Record<string, { label: string; tone: "warning" | "success" | "danger" }> = {
  Submitted: { label: "Refund requested", tone: "warning" },
  Approved: { label: "Refund approved", tone: "success" },
  Executing: { label: "Refund approved", tone: "success" },
  Completed: { label: "Refunded", tone: "success" },
  Rejected: { label: "Refund declined", tone: "danger" },
  Failed: { label: "Refund delayed", tone: "warning" },
};

// Why a grant can no longer be downloaded (EP-51 status).
const unavailable: Record<string, string> = {
  Revoked: "Access ended with the refund.",
  Expired: "The download period has ended.",
};

export function LibraryRow({ item, refund }: { item: LibraryItem; refund: MyRefundRequest | null }) {
  const router = useRouter();
  const toast = useToast();
  const [downloading, setDownloading] = useState(false);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [reasonError, setReasonError] = useState<string | undefined>();
  const [sendError, setSendError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);

  async function download() {
    setDownloading(true);
    const result = await call<{ url: string }>(`/downloads/${item.orderItemId}/link`, { method: "POST" });
    setDownloading(false);
    if (!result.ok) {
      toast("error", describe(result.problem));
      return;
    }

    window.location.href = result.data.url;
    // The count changes server-side; show it once the browser has started the download.
    setTimeout(() => router.refresh(), 1500);
  }

  function checkReason(value: string) {
    const trimmed = value.trim();
    const message = trimmed.length < 10 ? "Tell us a little more: at least 10 characters." : trimmed.length > 2000 ? "Use at most 2000 characters." : undefined;
    setReasonError(message);
    return !message;
  }

  async function requestRefund(event: React.FormEvent) {
    event.preventDefault();
    setSendError(null);
    if (!checkReason(reason)) return;

    setSending(true);
    const result = await call(`/refund-requests`, { method: "POST", body: { orderItemId: item.orderItemId, reason: reason.trim() }, idempotent: true });
    setSending(false);
    if (!result.ok) {
      const errors = fieldErrors(result.problem);
      if (errors.reason) setReasonError(errors.reason);
      else setSendError(describe(result.problem));
      return;
    }

    setDialogOpen(false);
    toast("success", "Refund request sent. You'll get an email when it's decided.");
    router.refresh();
  }

  const badge = refund ? refundBadges[refund.status] : null;
  const canDownload = item.status === "Active";
  const canRequestRefund = !refund && item.status !== "Revoked";

  return (
    <li className="flex flex-wrap items-center gap-4 border-b border-border px-4 py-4 last:border-b-0 sm:flex-nowrap">
      <Thumbnail id={item.productId} title={item.title} url={item.coverImageUrl} />
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <Link href={`/products/${item.productId}`} className="type-card-title truncate text-text hover:text-accent-strong">{item.title}</Link>
        <p className="m-0 type-label text-muted">
          Bought {date(item.purchasedAt)}
          {canDownload && item.downloadCount > 0 && <> · Downloaded {item.downloadCount} {item.downloadCount === 1 ? "time" : "times"}</>}
          {!canDownload && unavailable[item.status] && <> · {unavailable[item.status]}</>}
        </p>
        {refund?.decisionNote && <p className="m-0 type-label text-muted">Note from the team: {refund.decisionNote}</p>}
      </div>
      <div className="flex w-full items-center justify-end gap-4 sm:w-auto">
        {badge ? <Badge tone={badge.tone}>{badge.label}</Badge> : !canDownload && <StatusBadge status={item.status} />}
        {canDownload && <Button onClick={download} loading={downloading}>Download</Button>}
      </div>
      {canRequestRefund && (
        <div className="flex w-full justify-end">
          <button type="button" onClick={() => { setDialogOpen(true); setSendError(null); }} className="cursor-pointer border-0 bg-transparent p-0 type-label text-muted underline-offset-2 hover:text-text hover:underline">
            Request refund
          </button>
        </div>
      )}

      <Dialog open={dialogOpen} onClose={() => setDialogOpen(false)} title={`Request a refund for ${item.title}`}>
        <form className="flex flex-col gap-4" onSubmit={requestRefund} noValidate>
          <p className="m-0 type-body text-muted">
            A member of our team reviews every request and decides; you&apos;ll get an email either way. An approved refund returns what you paid for this item to your
            payment method, and access to the download ends.
          </p>
          {sendError && <FormAlert>{sendError}</FormAlert>}
          <Textarea
            label="What went wrong?"
            hint="10 to 2000 characters."
            rows={4}
            maxLength={2000}
            value={reason}
            error={reasonError}
            onChange={(event) => { setReason(event.target.value); if (reasonError) checkReason(event.target.value); }}
            onBlur={() => reason && checkReason(reason)}
          />
          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={() => setDialogOpen(false)}>Cancel</Button>
            <Button type="submit" loading={sending}>Send request</Button>
          </div>
        </form>
      </Dialog>
    </li>
  );
}
