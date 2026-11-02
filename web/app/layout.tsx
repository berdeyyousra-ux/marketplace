import type { Metadata } from "next";
import Link from "next/link";
import { currentUser } from "@/lib/api";
import { LogoutButton } from "@/components/LogoutButton";
import "./globals.css";

export const metadata: Metadata = { title: "Marketplace", description: "Digital products from independent sellers." };

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const me = await currentUser();
  const isSeller = me?.roles.includes("Seller") ?? false;
  const isAdmin = me?.roles.includes("Admin") ?? false;

  return (
    <html lang="en">
      <body>
        <nav className="nav">
          <div className="nav-inner">
            <Link href="/" className="nav-brand">Marketplace</Link>
            <Link href="/">Browse</Link>
            {me ? (
              <>
                <Link href="/orders">Orders</Link>
                <Link href="/library">Library</Link>
                <Link href="/seller">{isSeller ? "Seller dashboard" : "Sell"}</Link>
                {isAdmin && <Link href="/admin/refunds">Refund queue</Link>}
                <span className="muted small">{me.displayName}</span>
                <LogoutButton />
              </>
            ) : (
              <>
                <Link href="/login">Sign in</Link>
                <Link href="/register">Create account</Link>
              </>
            )}
          </div>
        </nav>
        <main>{children}</main>
      </body>
    </html>
  );
}
