"use client";

import { usePathname, useSearchParams } from "next/navigation";

// Catalogue search in the top bar. Submits on Enter to the catalogue, keeping the other filters out (a new search
// starts from the first page), and keeps its term visible after navigation.
export function SearchField() {
  const pathname = usePathname();
  const term = useSearchParams().get("q") ?? "";
  if (pathname !== "/" && !pathname.startsWith("/products")) {
    return null;
  }

  return (
    <form action="/" role="search" className="relative w-full min-w-0 max-w-xl">
      <label htmlFor="top-search" className="sr-only">Search products</label>
      <svg aria-hidden="true" viewBox="0 0 20 20" fill="none" className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-muted">
        <circle cx="9" cy="9" r="6.5" stroke="currentColor" strokeWidth="1.5" />
        <path d="M18 18l-3.8-3.8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      </svg>
      <input
        id="top-search"
        key={term}
        name="q"
        type="search"
        defaultValue={term}
        placeholder="Search products"
        className="w-full min-w-0 rounded-control border border-border-strong bg-surface py-2.5 pl-10 pr-3 type-body text-text placeholder:text-muted focus-visible:border-accent"
      />
    </form>
  );
}
