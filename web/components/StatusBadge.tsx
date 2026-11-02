const tones: Record<string, string> = {
  Paid: "good", Completed: "good", Active: "good", Approved: "good", Valid: "good", Eligible: "good",
  Pending: "warn", Submitted: "warn", Executing: "warn", EligibleDownloaded: "warn", PartiallyRefunded: "warn",
  needs_more_info: "warn", InvalidOutput: "warn", Skipped: "warn",
  Failed: "bad", Rejected: "bad", Cancelled: "bad", Expired: "bad", Revoked: "bad", Limit: "bad", Refunded: "bad",
  IneligibleOutsideWindow: "bad", ProviderError: "bad", reject: "bad", approve: "good",
};

export function StatusBadge({ value }: { value: string | null | undefined }) {
  if (!value) {
    return <span className="badge">none</span>;
  }

  return <span className={`badge ${tones[value] ?? ""}`}>{value.replace(/_/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2")}</span>;
}
