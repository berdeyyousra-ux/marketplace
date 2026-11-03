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
    <form action="/" role="search" className="w-full min-w-0 max-w-md">
      <label htmlFor="top-search" className="sr-only">Search products</label>
      <input
        id="top-search"
        key={term}
        name="q"
        type="search"
        defaultValue={term}
        placeholder="Search products"
        className="w-full min-w-0 rounded-control border border-border-strong bg-bg px-3 py-2 type-body text-text placeholder:text-muted"
      />
    </form>
  );
}
