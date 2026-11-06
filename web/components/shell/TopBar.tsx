import Link from "next/link";
import { Suspense } from "react";
import type { Me } from "@/lib/types";
import { LogoutButton } from "@/components/LogoutButton";
import { SearchField } from "./SearchField";

const link = "type-label text-text hover:text-accent-strong";

function Links({ me, restoring }: { me: Me | null; restoring: boolean }) {
  // While the session is being restored, a quiet placeholder: neither "Sign in" nor the account, so nothing flips.
  if (!me && restoring) {
    return <span aria-label="Restoring your session" role="status" className="inline-block h-5 w-40 animate-pulse rounded-control bg-border" />;
  }

  if (!me) {
    return (
      <>
        <Link href="/login" className={link}>Sign in</Link>
        <Link href="/register" className="rounded-control bg-accent-strong px-3 py-2 type-label text-accent-contrast hover:bg-accent-hover">Create account</Link>
      </>
    );
  }

  return (
    <>
      <Link href="/library" className={link}>Library</Link>
      <Link href="/seller" className={link}>{me.roles.includes("Seller") ? "Seller dashboard" : "Sell"}</Link>
      <details className="relative">
        <summary className="cursor-pointer list-none type-label text-text hover:text-accent-strong">{me.displayName} ▾</summary>
        <div className="absolute right-0 z-40 mt-2 flex min-w-44 flex-col gap-3 rounded-card border border-border bg-surface p-4 shadow-raised">
          <span className="type-label text-muted">{me.email}</span>
          <Link href="/orders" className={link}>Orders</Link>
          <Link href="/library" className={link}>Library</Link>
          {me.roles.includes("Admin") && <Link href="/admin/refunds" className={link}>Refund queue</Link>}
          <LogoutButton />
        </div>
      </details>
    </>
  );
}

// Sticky 64px bar (docs/frontend-design-plan.md, Shell). Below 768px the links collapse into a sheet.
export function TopBar({ me, restoring = false }: { me: Me | null; restoring?: boolean }) {
  return (
    <header className="sticky z-30 border-b border-border bg-surface" style={{ top: "env(safe-area-inset-top, 0px)" }}>
      <div className="container-page flex h-16 items-center gap-3 md:gap-6">
        <Link href="/" className="shrink-0 type-section text-text no-underline">Marketplace</Link>
        <div className="flex min-w-0 flex-1 justify-center">
          <Suspense fallback={null}>
            <SearchField />
          </Suspense>
        </div>
        <nav aria-label="Main" className="hidden items-center gap-5 md:flex">
          <Links me={me} restoring={restoring} />
        </nav>
        <details className="shrink-0 md:hidden">
          <summary className="cursor-pointer list-none rounded-control border border-border-strong px-3 py-2 type-label text-text">Menu</summary>
          <nav aria-label="Main" className="absolute inset-x-0 top-16 z-40 flex flex-col gap-4 border-b border-border bg-surface px-6 py-4 shadow-raised">
            <Links me={me} restoring={restoring} />
          </nav>
        </details>
      </div>
    </header>
  );
}

export function Footer() {
  return (
    <footer className="border-t border-border">
      <div className="container-page flex h-14 items-center justify-between type-label text-muted">
        <span>Marketplace</span>
        <a href="https://github.com/berdeyyousra-ux/marketplace" className="text-muted hover:text-accent-strong">Source on GitHub</a>
      </div>
    </footer>
  );
}
