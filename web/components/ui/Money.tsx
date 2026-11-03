import { cn } from "@/lib/cn";

export type MoneyValue = { amount: string; currency: string };

// The only place a price is formatted. Tabular figures so columns align and digits do not jitter.
export function Money({ value, className }: { value: MoneyValue | null | undefined; className?: string }) {
  if (!value) {
    return <span className={cn("tabular-nums", className)}>–</span>;
  }

  const text = new Intl.NumberFormat("en-IE", { style: "currency", currency: value.currency }).format(Number(value.amount));
  return <span className={cn("tabular-nums", className)}>{text}</span>;
}
