import Link from "next/link";
import { publicGet, serverGet } from "@/lib/api";
import type { Category, Product } from "@/lib/types";
import { PageHeader } from "@/components/ui/PageHeader";
import { FormAlert } from "@/components/FormAlert";
import { StatusPanel } from "@/components/AuthShell";
import { ListingEditor } from "../ListingEditor";

// EP-22 with its ETag (owners only), then EP-24 to EP-27 and the cover upload from the editor.
export default async function EditListingPage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ created?: string }> }) {
  const { id } = await params;
  const { created } = await searchParams;
  const [product, categories] = await Promise.all([
    serverGet<Product>(`/products/${id}`, `/seller/products/${id}`),
    publicGet<Category[]>("/categories"),
  ]);

  // Only the owner gets an ETag, so its absence means this isn't the caller's listing (other sellers' look not found).
  if (!product.ok || !product.etag) {
    return (
      <div className="container-page py-8">
        <FormAlert>This listing could not be found in your shop.</FormAlert>
      </div>
    );
  }

  return (
    <div className="container-page py-8">
      <Link href="/seller" className="type-label text-accent-strong hover:underline">← Your shop</Link>
      <PageHeader title={product.data.title} />
      {created && (
        <div className="-mt-4 mb-6">
          <StatusPanel tone="success">Draft created. Add the file buyers download and a cover, then publish.</StatusPanel>
        </div>
      )}
      <ListingEditor product={product.data} etag={product.etag} categories={categories.ok ? categories.data : []} />
    </div>
  );
}
