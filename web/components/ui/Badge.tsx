import { cn } from "@/lib/cn";
import { statusLabel, toneFor, type Tone } from "@/lib/status";

const tones: Record<Tone, string> = {
  neutral: "text-muted border-border-strong",
  success: "text-success border-success",
  warning: "text-warning border-warning",
  danger: "text-danger border-danger",
};

// Always the word with the colour, never colour alone.
export function Badge({ tone = "neutral", children }: { tone?: Tone; children: React.ReactNode }) {
  return <span className={cn("inline-flex items-center rounded-full border px-2 py-1 type-micro", tones[tone])}>{children}</span>;
}

export function StatusBadge({ status }: { status: string | null | undefined }) {
  return status ? <Badge tone={toneFor(status)}>{statusLabel(status)}</Badge> : <Badge>None</Badge>;
}
