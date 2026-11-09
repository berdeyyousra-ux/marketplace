import Link from "next/link";
import { publicGet } from "@/lib/api";
import type { Category, Page, ProductSummary } from "@/lib/types";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { ProductCard } from "@/components/ui/ProductCard";
import { FilterForm } from "./FilterForm";

type Search = { q?: string; category?: string; sort?: string; min?: string; max?: string; cursor?: string };

// EP-20 and EP-21: the catalogue. Filters in a left rail from 1024px, in a sheet below; search lives in the top bar.
export default async function CataloguePage({ searchParams }: { searchParams: Promise<Search> }) {
  const search = await searchParams;
  const query = new URLSearchParams({ limit: "24" });
  if (search.q) query.set("q", search.q);
  if (search.category) query.set("categorySlug", search.category);
  if (search.sort) query.set("sort", search.sort);
  if (search.min) query.set("minPrice", search.min);
  if (search.max) query.set("maxPrice", search.max);
  if (search.cursor) query.set("cursor", search.cursor);

  const [products, categories] = await Promise.all([
    publicGet<Page<ProductSummary>>(`/products?${query}`),
    publicGet<Category[]>("/categories"),
  ]);
  const categoryList = categories.ok ? categories.data : [];
  const filtered = Boolean(search.q || search.category || search.min || search.max);

  // Links keep every current filter except the ones being changed, and always return to the first page.
  const href = (changes: Partial<Search>) => {
    const next = new URLSearchParams();
    for (const [key, value] of Object.entries({ ...search, cursor: undefined, ...changes })) {
      if (value) next.set(key, value);
    }

    const text = next.toString();
    return text ? `/?${text}` : "/";
  };

  const filters = (
    <FilterForm q={search.q} category={search.category} min={search.min} max={search.max} sort={search.sort} filtered={filtered} />
  );

  return (
    <div className="container-page flex flex-col py-8">
      {search.q ? (
        <PageHeader title={`Results for “${search.q}”`} />
      ) : (
        <div className="flex flex-col gap-3 pb-8">
          <h1 className="max-w-2xl type-page-title text-[40px] leading-[1.1] sm:text-[48px]">
            Digital products, made by independent creators.
          </h1>
          <p className="max-w-lg type-body text-muted">
            Templates, ebooks, graphics, software and more. Browse the full catalogue below, or jump straight to a category.
          </p>
        </div>
      )}
      {categoryList.length > 0 && (
        <ul aria-label="Categories" className="mb-8 flex flex-wrap gap-2 border-b border-border pb-8">
          <li>
            <Link
              href={href({ category: undefined })}
              aria-current={!search.category ? "page" : undefined}
              className={!search.category ? "inline-flex items-center rounded-full border border-accent bg-accent-muted px-4 py-2 type-label text-accent-hover" : "inline-flex items-center rounded-full border border-border-strong bg-surface px-4 py-2 type-label text-text hover:border-accent hover:text-accent-strong"}
            >
              All categories
            </Link>
          </li>
          {categoryList.map((category) => {
            const active = category.slug === search.category;
            return (
              <li key={category.id}>
                <Link
                  href={href({ category: category.slug })}
                  aria-current={active ? "page" : undefined}
                  className={active ? "inline-flex items-center rounded-full border border-accent bg-accent-muted px-4 py-2 type-label text-accent-hover" : "inline-flex items-center rounded-full border border-border-strong bg-surface px-4 py-2 type-label text-text hover:border-accent hover:text-accent-strong"}
                >
                  {category.name}
                </Link>
              </li>
            );
          })}
        </ul>
      )}
      <div className="flex flex-col gap-8 lg:flex-row">
        <aside className="hidden w-60 shrink-0 lg:block" aria-label="Filters">{filters}</aside>
        <details className="rounded-card border border-border bg-surface p-4 lg:hidden">
          <summary className="cursor-pointer type-label text-text">Filters and sort</summary>
          <div className="pt-4">{filters}</div>
        </details>

        <div className="min-w-0 flex-1">
          {!products.ok ? (
            <EmptyState message="The catalogue could not be loaded. Try again in a moment." action={<Link href="/" className="type-label text-accent-strong">Reload</Link>} />
          ) : products.data.items.length === 0 ? (
            <EmptyState message="No products match these filters." action={<Link href="/" className="rounded-control border border-border-strong px-4 py-2 type-label text-accent-strong">Clear filters</Link>} />
          ) : (
            <>
              <ul className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
                {products.data.items.map((product) => (
                  <li key={product.id}>
                    <ProductCard product={product} />
                  </li>
                ))}
              </ul>
              <Pagination
                nextHref={products.data.nextCursor ? href({ cursor: products.data.nextCursor }) : null}
                firstHref={href({})}
                onFirstPage={!search.cursor}
              />
            </>
          )}
        </div>
      </div>
    </div>
  );
}
