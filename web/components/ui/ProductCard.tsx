import Link from "next/link";
import { Cover } from "./Cover";
import { Money, type MoneyValue } from "./Money";

export type ProductCardData = { id: string; title: string; price: MoneyValue; coverImageUrl?: string | null; sellerName?: string | null };

// Grid sizes: 1 column below 640px, 2 to 1024px, then 3 beside the 240px filter rail in the 1200px container.
export const productGridSizes = "(min-width: 1024px) 280px, (min-width: 640px) 45vw, 100vw";

// Fixed 4:3 cover, title clamped to two lines, price bottom-left. Hover lifts the border and zooms the cover slightly.
export function ProductCard({ product }: { product: ProductCardData }) {
  return (
    <Link
      href={`/products/${product.id}`}
      className="group flex h-full flex-col overflow-hidden rounded-card border border-border bg-surface text-text no-underline transition-colors hover:border-accent"
    >
      <Cover id={product.id} title={product.title} url={product.coverImageUrl} sizes={productGridSizes} className="transition-transform duration-300 group-hover:scale-105" />
      <div className="flex flex-1 flex-col gap-1.5 p-5">
        <span className="type-card-title line-clamp-2">{product.title}</span>
        {product.sellerName && <span className="type-label text-muted">{product.sellerName}</span>}
        <Money value={product.price} className="type-price mt-auto pt-2 text-accent-strong" />
      </div>
    </Link>
  );
}
