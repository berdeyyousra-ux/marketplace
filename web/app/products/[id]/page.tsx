import { notFound } from "next/navigation";
import { currentUser, publicGet } from "@/lib/api";
import { money } from "@/lib/format";
import type { Product } from "@/lib/types";
import { BuyButton } from "./BuyButton";

// EP-22.
export default async function ProductPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const [product, me] = await Promise.all([publicGet<Product>(`/products/${id}`), currentUser()]);
  if (!product.ok) {
    notFound();
  }

  const { data } = product;
  return (
    <div className="stack" style={{ maxWidth: 720 }}>
      <h1>{data.title}</h1>
      <div className="row">
        <span style={{ fontSize: "1.3rem", fontWeight: 650 }}>{money(data.price)}</span>
        {data.file && <span className="muted small">{data.file.name} · {(data.file.sizeBytes / 1024).toFixed(0)} KB</span>}
      </div>
      <p style={{ whiteSpace: "pre-wrap" }}>{data.description}</p>
      <BuyButton productId={data.id} signedIn={me !== null} />
    </div>
  );
}
