import { label } from "./format";

// The AI's recommendation in words that read as advice. Rendered in the neutral tone on purpose: green or red would
// make a suggestion look like a decision already taken (docs/frontend-design-plan.md, Admin refund queue).
const actions: Record<string, string> = {
  approve: "Suggests approving",
  reject: "Suggests rejecting",
  needs_more_info: "Suggests asking for more",
};

export function aiActionLabel(action: string | null | undefined) {
  return action ? actions[action] ?? label(action) : null;
}

// A decision contradicts the suggestion when the AI leaned the other way; "needs more info" contradicts neither.
export function contradicts(decision: "Approved" | "Rejected", action: string | null | undefined) {
  return (decision === "Approved" && action === "reject") || (decision === "Rejected" && action === "approve");
}

// Why no suggestion is shown, from the suggestion's validation status (section 12.6).
const absent: Record<string, string> = {
  InvalidOutput: "the model's answer didn't match the expected format",
  ProviderError: "the AI provider could not be reached",
  Skipped: "analysis was skipped for this request",
};

export function absenceReason(status: string) {
  return absent[status] ?? label(status).toLowerCase();
}
