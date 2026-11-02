import Link from "next/link";
import { publicGet } from "@/lib/api";
import { money } from "@/lib/format";
import type { Category, Page, ProductSummary } from "@/lib/types";

type Search = { q?: string; category?: string; sort?: string; cursor?: string };

// EP-20 and EP-21: published products, newest first, with search and a category filter.
export default async function BrowsePage({ searchParams }: { searchParams: Promise<Search> }) {
  const { q, category, sort, cursor } = await searchParams;
  const query = new URLSearchParams({ limit: "24" });
  if (q) query.set("q", q);
  if (category) query.set("categorySlug", category);
  if (sort) query.set("sort", sort);
  if (cursor) query.set("cursor", cursor);

  const [products, categories] = await Promise.all([
    publicGet<Page<ProductSummary>>(`/products?${query}`),
    publicGet<Category[]>("/categories"),
  ]);
  const categoryName = new Map((categories.ok ? categories.data : []).map((item) => [item.id, item.name]));
  const nextQuery = new URLSearchParams(query);
  nextQuery.delete("limit");

  return (
    <div className="stack">
      <h1>Browse</h1>
      <form className="row" action="/">
        <input name="q" defaultValue={q} placeholder="Search products" style={{ maxWidth: 320 }} />
        <select name="category" defaultValue={category ?? ""} style={{ maxWidth: 220 }}>
          <option value="">All categories</option>
          {(categories.ok ? categories.data : []).map((item) => (
            <option key={item.id} value={item.slug}>{item.name}</option>
          ))}
        </select>
        <select name="sort" defaultValue={sort ?? ""} style={{ maxWidth: 200 }}>
          <option value="">{q ? "Most relevant" : "Newest"}</option>
          <option value="Newest">Newest</option>
          <option value="PriceAsc">Price, low to high</option>
          <option value="PriceDesc">Price, high to low</option>
        </select>
        <button type="submit">Search</button>
      </form>

      {!products.ok ? (
        <p className="error">The catalogue could not be loaded ({products.problem.detail ?? products.status}).</p>
      ) : products.data.items.length === 0 ? (
        <p className="muted">No products match.</p>
      ) : (
        <>
          <div className="grid">
            {products.data.items.map((product) => (
              <Link key={product.id} href={`/products/${product.id}`} className="card" style={{ color: "inherit" }}>
                <div className="small muted">{categoryName.get(product.categoryId) ?? "Product"}</div>
                <div style={{ fontWeight: 600, margin: "0.25rem 0" }}>{product.title}</div>
                <div>{money(product.price)}</div>
              </Link>
            ))}
          </div>
          {products.data.nextCursor && (
            <p>
              <Link className="button secondary" href={`/?${new URLSearchParams({ ...Object.fromEntries(nextQuery), cursor: products.data.nextCursor })}`}>
                More products
              </Link>
            </p>
          )}
        </>
      )}
    </div>
  );
}
