import Link from "next/link";
import { EmptyState } from "@/components/ui/EmptyState";

export default function NotFound() {
  return (
    <div className="container-page py-8">
      <h1 className="type-page-title mb-8 text-text">Page not found</h1>
      <EmptyState
        message="This page doesn't exist, or the product is no longer for sale."
        action={<Link href="/" className="rounded-control bg-accent px-4 py-2 type-label text-accent-contrast hover:bg-accent-hover">Browse products</Link>}
      />
    </div>
  );
}
