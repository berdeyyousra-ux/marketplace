import type { Metadata } from "next";
import { Inter } from "next/font/google";
import { currentUser } from "@/lib/api";
import { Footer, TopBar } from "@/components/shell/TopBar";
import { ToastProvider } from "@/components/ui/Toast";
import "./globals.css";

// One typeface (T-086): Inter, variable, swapped in so text is never invisible while it loads.
const inter = Inter({ subsets: ["latin"], display: "swap", variable: "--font-inter" });

export const metadata: Metadata = { title: "Marketplace", description: "Digital products from independent sellers." };

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const me = await currentUser();

  return (
    <html lang="en" className={inter.variable}>
      <body className="flex min-h-screen flex-col">
        <TopBar me={me} />
        <ToastProvider>
          <main className="w-full flex-1">{children}</main>
        </ToastProvider>
        <Footer />
      </body>
    </html>
  );
}
