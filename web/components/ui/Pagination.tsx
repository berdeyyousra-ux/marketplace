import Link from "next/link";

// Keyset pages (D-29): the API gives only a next cursor, so the controls are "next" and "back to the start".
export function Pagination({ nextHref, firstHref, onFirstPage }: { nextHref: string | null; firstHref: string; onFirstPage: boolean }) {
  if (!nextHref && onFirstPage) {
    return null;
  }

  const link = "rounded-control border border-border-strong bg-surface px-4 py-2 type-label text-accent-strong hover:border-accent";
  return (
    <nav aria-label="Pages" className="flex items-center justify-between gap-4 py-6">
      {onFirstPage ? <span /> : <Link href={firstHref} className={link}>Back to the start</Link>}
      {nextHref ? <Link href={nextHref} className={link}>Next page</Link> : <span className="type-label text-muted">End of the list</span>}
    </nav>
  );
}
