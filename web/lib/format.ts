import type { Money } from "./types";

export function money(value: Money | null | undefined) {
  if (!value) {
    return "–";
  }

  return new Intl.NumberFormat("en-IE", { style: "currency", currency: value.currency }).format(Number(value.amount));
}

export function date(value: string | null | undefined) {
  return value ? new Date(value).toLocaleString("en-GB", { dateStyle: "medium", timeStyle: "short" }) : "–";
}

export function label(value: string | null | undefined) {
  return value ? value.replace(/_/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2") : "–";
}
