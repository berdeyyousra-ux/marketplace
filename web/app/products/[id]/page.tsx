import Link from "next/link";
import { notFound } from "next/navigation";
import { currentUser, publicGet } from "@/lib/api";
import type { Category, Product } from "@/lib/types";
import { Cover } from "@/components/ui/Cover";
import { Money } from "@/components/ui/Money";
import { BuyButton } from "./BuyButton";

const fileTypes: Record<string, string> = {
  "application/pdf": "PDF",
  "application/zip": "ZIP archive",
  "application/epub+zip": "EPUB",
  "image/png": "PNG image",
  "image/jpeg": "JPEG image",
  "audio/mpeg": "MP3 audio",
  "video/mp4": "MP4 video",
  "text/plain": "Text",
  "text/markdown": "Markdown",
  "font/otf": "OpenType font",
  "font/ttf": "TrueType font",
};

function size(bytes: number | null | undefined) {
  if (!bytes) return null;
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// EP-22. Two columns from 1024px. The action depends on the viewer: Buy, Go to library (already owned) or Edit listing
// (their own product); a product is never offered to someone who has it.
export default async function ProductPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const [product, me, categories] = await Promise.all([
    publicGet<Product>(`/products/${id}`, { authenticated: true }),
    currentUser(),
    publicGet<Category[]>("/categories"),
  ]);
  if (!product.ok) {
    notFound();
  }

  const data = product.data;
  const category = categories.ok ? categories.data.find((item) => item.id === data.categoryId)?.name : undefined;
  const isSeller = me?.sellerProfile?.id === data.sellerId;
  const paragraphs = data.description.split(/\n\s*\n/).map((paragraph) => paragraph.trim()).filter(Boolean);
  const facts = [
    ["Category", category],
    ["File type", data.fileType ? fileTypes[data.fileType] ?? data.fileType : null],
    ["Size", size(data.fileSizeBytes)],
  ].filter((fact): fact is [string, string] => Boolean(fact[1]));

  return (
    <article className="container-page grid gap-10 py-8 lg:grid-cols-2">
      <Cover id={data.id} title={data.title} url={data.coverImageUrl} sizes="(min-width: 1024px) 576px, 100vw" priority className="rounded-card border border-border" />
      <div className="flex flex-col gap-6">
        <div className="flex flex-col gap-2">
          <h1 className="type-page-title m-0 text-text">{data.title}</h1>
          {data.sellerName && <p className="type-label text-muted">by {data.sellerName}</p>}
        </div>
        <Money value={data.price} className="type-price text-[24px] text-text" />
        {isSeller ? (
          <Link href={`/seller/products/${data.id}`} className="block rounded-control border border-border-strong px-4 py-3 text-center type-label text-accent hover:border-accent">Edit listing</Link>
        ) : data.viewerOwns ? (
          <Link href="/library" className="block rounded-control border border-accent bg-accent px-4 py-3 text-center type-label text-accent-contrast hover:bg-accent-hover">Go to library</Link>
        ) : (
          <BuyButton productId={data.id} signedIn={me !== null} />
        )}
        <div className="flex flex-col gap-4 type-body text-text">
          {paragraphs.map((paragraph, index) => <p key={index} className="m-0 whitespace-pre-line">{paragraph}</p>)}
        </div>
        {facts.length > 0 && (
          <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-2 border-t border-border pt-6">
            {facts.map(([label, value]) => (
              <div key={label} className="contents">
                <dt className="type-label text-muted">{label}</dt>
                <dd className="m-0 type-label text-text">{value}</dd>
              </div>
            ))}
          </dl>
        )}
      </div>
    </article>
  );
}
