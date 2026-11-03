import Link from "next/link";
import { publicGet } from "@/lib/api";
import type { Category, Page, ProductSummary } from "@/lib/types";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { ProductCard } from "@/components/ui/ProductCard";

type Search = { q?: string; category?: string; sort?: string; min?: string; max?: string; cursor?: string };

const sorts = [
  ["", "Newest"],
  ["PriceAsc", "Price, low to high"],
  ["PriceDesc", "Price, high to low"],
] as const;

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
  const activeCategory = categoryList.find((category) => category.slug === search.category);
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
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-2">
        <h2 className="type-label text-muted">Category</h2>
        <ul className="flex flex-col gap-1">
          <li><Link href={href({ category: undefined })} className={!search.category ? "type-label text-accent" : "type-label text-text hover:text-accent"}>All categories</Link></li>
          {categoryList.map((category) => (
            <li key={category.id}>
              <Link href={href({ category: category.slug })} aria-current={category.slug === search.category ? "page" : undefined}
                className={category.slug === search.category ? "type-label text-accent" : "type-label text-text hover:text-accent"}>
                {category.name}
              </Link>
            </li>
          ))}
        </ul>
      </div>
      <form action="/" className="flex flex-col gap-3">
        {search.q && <input type="hidden" name="q" value={search.q} />}
        {search.category && <input type="hidden" name="category" value={search.category} />}
        <h2 className="type-label text-muted">Price, EUR</h2>
        <div className="flex items-center gap-2">
          <label className="sr-only" htmlFor="min">Minimum price</label>
          <input id="min" name="min" type="number" min="0" step="1" placeholder="Min" defaultValue={search.min}
            className="w-full rounded-control border border-border-strong bg-surface px-2 py-2 type-body text-text" />
          <span className="text-muted">–</span>
          <label className="sr-only" htmlFor="max">Maximum price</label>
          <input id="max" name="max" type="number" min="0" step="1" placeholder="Max" defaultValue={search.max}
            className="w-full rounded-control border border-border-strong bg-surface px-2 py-2 type-body text-text" />
        </div>
        <label htmlFor="sort" className="type-label text-muted">Sort</label>
        <select id="sort" name="sort" defaultValue={search.sort ?? ""}
          className="w-full rounded-control border border-border-strong bg-surface px-2 py-2 type-body text-text">
          {sorts.map(([value, label]) => <option key={value} value={value}>{search.q && value === "" ? "Most relevant" : label}</option>)}
        </select>
        <button type="submit" className="rounded-control border border-accent bg-accent px-4 py-2 type-label text-accent-contrast hover:bg-accent-hover">Apply</button>
        {filtered && <Link href="/" className="type-label text-accent">Clear filters</Link>}
      </form>
    </div>
  );

  const title = search.q ? `Results for “${search.q}”` : activeCategory?.name ?? "Browse";
  return (
    <div className="container-page flex flex-col py-8">
      <PageHeader title={title} description={search.q ? undefined : "Digital products from independent sellers."} />
      <div className="flex flex-col gap-8 lg:flex-row">
        <aside className="hidden w-60 shrink-0 lg:block" aria-label="Filters">{filters}</aside>
        <details className="rounded-card border border-border bg-surface p-4 lg:hidden">
          <summary className="cursor-pointer type-label text-text">Filters and sort</summary>
          <div className="pt-4">{filters}</div>
        </details>

        <div className="min-w-0 flex-1">
          {!products.ok ? (
            <EmptyState message="The catalogue could not be loaded. Try again in a moment." action={<Link href="/" className="type-label text-accent">Reload</Link>} />
          ) : products.data.items.length === 0 ? (
            <EmptyState message="No products match these filters." action={<Link href="/" className="rounded-control border border-border-strong px-4 py-2 type-label text-accent">Clear filters</Link>} />
          ) : (
            <>
              <ul className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
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
