// The one status-to-tone mapping (docs/frontend-design-plan.md). Every badge uses it, so a status never renders in
// two colours on two screens; anything unmapped is neutral, so a new status still reads.
export type Tone = "neutral" | "success" | "warning" | "danger";

const tones: Record<string, Tone> = {
  Paid: "success", Fulfilled: "success", Completed: "success", Published: "success", Verified: "success",
  Active: "success", Approved: "success", Valid: "success", Eligible: "success", Succeeded: "success",
  Pending: "warning", Executing: "warning", RequiresPayment: "warning", RefundRequested: "warning", Draft: "warning",
  Submitted: "warning", PartiallyRefunded: "warning", EligibleDownloaded: "warning", InvalidOutput: "warning", Skipped: "warning",
  Cancelled: "danger", Expired: "danger", Failed: "danger", DeadLettered: "danger", Rejected: "danger",
  Revoked: "danger", Limit: "danger", Refunded: "danger", IneligibleOutsideWindow: "danger", ProviderError: "danger",
  Unpublished: "neutral", Removed: "danger",
};

export function toneFor(status: string | null | undefined): Tone {
  return (status && tones[status]) || "neutral";
}

// "PartiallyRefunded" to "Partially refunded", "needs_more_info" to "Needs more info".
export function statusLabel(status: string) {
  const spaced = status.replace(/_/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}
