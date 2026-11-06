"use client";

import Link from "next/link";
import { useRef } from "react";

const sorts = [
  ["", "Newest"],
  ["PriceAsc", "Price, low to high"],
  ["PriceDesc", "Price, high to low"],
] as const;

// Price and sort controls for the catalogue filter rail. Applies on change instead of a submit button: the sort
// select fires immediately, the price inputs debounce briefly so a full value can be typed first.
export function FilterForm({ q, category, min, max, sort, filtered }: {
  q?: string; category?: string; min?: string; max?: string; sort?: string; filtered: boolean;
}) {
  const formRef = useRef<HTMLFormElement>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  function submitNow() {
    formRef.current?.requestSubmit();
  }

  function submitDebounced() {
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(submitNow, 500);
  }

  return (
    <form ref={formRef} action="/" className="flex flex-col gap-3">
      {q && <input type="hidden" name="q" value={q} />}
      {category && <input type="hidden" name="category" value={category} />}
      <h2 className="type-label text-muted">Price, EUR</h2>
      <div className="flex items-center gap-2">
        <label className="sr-only" htmlFor="min">Minimum price</label>
        <input id="min" name="min" type="number" min="0" step="1" placeholder="Min" defaultValue={min} onChange={submitDebounced}
          className="w-full rounded-control border border-border-strong bg-surface px-2 py-2 type-body text-text" />
        <span className="text-muted">–</span>
        <label className="sr-only" htmlFor="max">Maximum price</label>
        <input id="max" name="max" type="number" min="0" step="1" placeholder="Max" defaultValue={max} onChange={submitDebounced}
          className="w-full rounded-control border border-border-strong bg-surface px-2 py-2 type-body text-text" />
      </div>
      <label htmlFor="sort" className="type-label text-muted">Sort</label>
      <select id="sort" name="sort" defaultValue={sort ?? ""} onChange={submitNow}
        className="w-full rounded-control border border-border-strong bg-surface px-2 py-2 type-body text-text">
        {sorts.map(([value, label]) => <option key={value} value={value}>{q && value === "" ? "Most relevant" : label}</option>)}
      </select>
      <button type="submit" className="sr-only">Apply filters</button>
      {filtered && <Link href="/" className="type-label text-accent-strong">Clear filters</Link>}
    </form>
  );
}
