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

// An order's last eight hex digits, to tell orders apart and quote to support. Ids are UUIDv7, whose leading digits
// are a timestamp shared by orders placed within the same minute; the trailing ones are random.
export function shortId(id: string) {
  return id.replace(/-/g, "").slice(-8).toUpperCase();
}
