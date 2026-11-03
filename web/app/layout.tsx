import type { Metadata } from "next";
import { Inter } from "next/font/google";
import Link from "next/link";
import { currentUser } from "@/lib/api";
import { LogoutButton } from "@/components/LogoutButton";
import { ToastProvider } from "@/components/ui/Toast";
import "./globals.css";

// One typeface (T-086): Inter, variable, swapped in so text is never invisible while it loads.
const inter = Inter({ subsets: ["latin"], display: "swap", variable: "--font-inter" });

export const metadata: Metadata = { title: "Marketplace", description: "Digital products from independent sellers." };

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const me = await currentUser();
  const isSeller = me?.roles.includes("Seller") ?? false;
  const isAdmin = me?.roles.includes("Admin") ?? false;

  return (
    <html lang="en" className={inter.variable}>
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
        <ToastProvider>
          <main>{children}</main>
        </ToastProvider>
      </body>
    </html>
  );
}
