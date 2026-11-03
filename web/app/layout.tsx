import type { Metadata } from "next";
import { Inter } from "next/font/google";
import { currentSession } from "@/lib/api";
import { Footer, TopBar } from "@/components/shell/TopBar";
import { ToastProvider } from "@/components/ui/Toast";
import { SessionKeeper } from "@/components/SessionKeeper";
import "./globals.css";

// One typeface (T-086): Inter, variable, swapped in so text is never invisible while it loads.
const inter = Inter({ subsets: ["latin"], display: "swap", variable: "--font-inter" });

export const metadata: Metadata = { title: "Marketplace", description: "Digital products from independent sellers." };

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const { me, restorable } = await currentSession();

  return (
    <html lang="en" className={inter.variable}>
      <body className="flex min-h-screen flex-col">
        <TopBar me={me} restoring={restorable} />
        <SessionKeeper restore={restorable} />
        <ToastProvider>
          <main className="w-full flex-1">{children}</main>
        </ToastProvider>
        <Footer />
      </body>
    </html>
  );
}
