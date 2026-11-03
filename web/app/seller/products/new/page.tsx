import Link from "next/link";
import { publicGet, serverGet } from "@/lib/api";
import type { Category, Me } from "@/lib/types";
import { PageHeader } from "@/components/ui/PageHeader";
import { ListingEditor } from "../ListingEditor";

// EP-23 (US-11): a new listing starts as a draft.
export default async function NewListingPage() {
  await serverGet<Me>("/me", "/seller/products/new");
  const categories = await publicGet<Category[]>("/categories");
  return (
    <div className="container-page py-8">
      <Link href="/seller" className="type-label text-accent hover:underline">← Your shop</Link>
      <PageHeader title="New listing" description="Start with the details. It stays a private draft until you publish it." />
      <ListingEditor product={null} etag={null} categories={categories.ok ? categories.data : []} />
    </div>
  );
}
