"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useRef, useState } from "react";
import { call, describe, fieldErrors } from "@/lib/client";
import type { Category, Product, Problem } from "@/lib/types";
import { Button } from "@/components/ui/Button";
import { Input, Select, Textarea } from "@/components/ui/Field";
import { Cover } from "@/components/ui/Cover";
import { StatusBadge } from "@/components/ui/Badge";
import { useToast } from "@/components/ui/Toast";
import { FormAlert } from "@/components/FormAlert";

// Section 9.2 and T-085 limits, checked here first so a wrong file fails before a long upload; the API decides.
const FILE_TYPES = ".pdf,.zip,.epub,.png,.jpg,.jpeg,.mp3,.mp4,.txt,.md,.otf,.ttf";
const FILE_MAX = 100 * 1024 * 1024;
const COVER_TYPES = ["image/jpeg", "image/png", "image/webp"];
const COVER_MAX = 5 * 1024 * 1024;

type Details = { title: string; description: string; price: string; categoryId: string };

function validate(details: Details): Partial<Record<keyof Details, string>> {
  const errors: Partial<Record<keyof Details, string>> = {};
  const title = details.title.trim();
  if (title.length < 3 || title.length > 120) errors.title = "Use 3 to 120 characters.";
  if (!details.description.trim()) errors.description = "Describe what the buyer gets.";
  else if (details.description.length > 5000) errors.description = "Use at most 5000 characters.";
  const price = details.price.trim().replace(",", ".");
  if (!/^\d+(\.\d{1,2})?$/.test(price)) errors.price = "Enter a price like 12.50.";
  else if (Number(price) < 0.5 || Number(price) > 10000) errors.price = "Prices go from €0.50 to €10,000.";
  if (!details.categoryId) errors.categoryId = "Choose a category.";
  return errors;
}

function size(bytes: number) {
  return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

function problemText(status: number, problem: Problem) {
  if (status === 412) return "This listing changed in another tab or window. Reload the page to see the latest version, then make your change again.";
  if (status === 413) return "That file is too large.";
  return describe(problem);
}

export function ListingEditor({ product: initial, etag: initialEtag, categories }: { product: Product | null; etag: string | null; categories: Category[] }) {
  const router = useRouter();
  const toast = useToast();
  const [product, setProduct] = useState(initial);
  const [etag, setEtag] = useState(initialEtag);
  const [details, setDetails] = useState<Details>({
    title: initial?.title ?? "",
    description: initial?.description ?? "",
    price: initial?.price.amount ?? "",
    categoryId: initial ? String(initial.categoryId) : "",
  });
  const [errors, setErrors] = useState<Partial<Record<keyof Details, string>>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [uploading, setUploading] = useState<"file" | "cover" | null>(null);
  const [statusBusy, setStatusBusy] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const coverInput = useRef<HTMLInputElement>(null);

  function field<K extends keyof Details>(key: K) {
    return {
      value: details[key],
      error: errors[key],
      onChange: (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => {
        const next = { ...details, [key]: event.target.value };
        setDetails(next);
        if (errors[key]) setErrors((current) => ({ ...current, [key]: validate(next)[key] }));
      },
      onBlur: () => {
        if (details[key]) setErrors((current) => ({ ...current, [key]: validate(details)[key] }));
      },
    };
  }

  // Uploads and status changes move the product's version on; read it back so the next save carries the right ETag.
  async function reload(id: string) {
    const latest = await call<Product>(`/products/${id}`);
    if (latest.ok) {
      setProduct(latest.data);
      setEtag(latest.etag);
    }
  }

  async function save(event: React.FormEvent) {
    event.preventDefault();
    setFormError(null);
    const found = validate(details);
    setErrors(found);
    if (Object.values(found).some(Boolean)) return;

    setSaving(true);
    const body = {
      title: details.title.trim(),
      description: details.description.trim(),
      price: { amount: Number(details.price.trim().replace(",", ".")).toFixed(2), currency: "EUR" },
      categoryId: Number(details.categoryId),
    };
    const result = product
      ? await call<Product>(`/products/${product.id}`, { method: "PUT", body, ifMatch: etag })
      : await call<Product>("/products", { method: "POST", body });
    setSaving(false);
    if (!result.ok) {
      const fields = fieldErrors(result.problem);
      const mapped: Partial<Record<keyof Details, string>> = {
        title: fields.title, description: fields.description, price: fields["price.amount"] ?? fields.price, categoryId: fields.categoryId,
      };
      if (Object.values(mapped).some(Boolean)) setErrors(mapped);
      else setFormError(problemText(result.status, result.problem));
      return;
    }

    if (!product) {
      // A new listing is a draft; the file and cover are added on its own page.
      router.replace(`/seller/products/${result.data.id}?created=1`);
      return;
    }

    setProduct(result.data);
    setEtag(result.etag);
    toast("success", "Changes saved.");
    // The page title is rendered on the server.
    router.refresh();
  }

  async function upload(kind: "file" | "cover", input: HTMLInputElement | null) {
    const chosen = input?.files?.[0];
    if (!product || !chosen || !input) return;
    input.value = "";
    if (kind === "cover" && !COVER_TYPES.includes(chosen.type)) return toast("error", "Covers must be JPEG, PNG or WebP images.");
    if (kind === "cover" && chosen.size > COVER_MAX) return toast("error", "Covers can be at most 5 MB.");
    if (kind === "file" && chosen.size > FILE_MAX) return toast("error", "Files can be at most 100 MB.");

    setUploading(kind);
    const form = new FormData();
    form.append("file", chosen);
    const result = await call(`/products/${product.id}/${kind}`, { method: kind === "cover" ? "PUT" : "POST", body: form });
    if (result.ok) {
      await reload(product.id);
      toast("success", kind === "cover" ? "Cover updated." : `${chosen.name} uploaded.`);
    } else {
      toast("error", problemText(result.status, result.problem));
    }

    setUploading(null);
  }

  async function changeStatus(publish: boolean) {
    if (!product) return;
    setStatusBusy(true);
    const result = await call<Product>(`/products/${product.id}/${publish ? "publish" : "unpublish"}`, { method: "POST", ifMatch: etag });
    setStatusBusy(false);
    if (!result.ok) {
      toast("error", problemText(result.status, result.problem));
      return;
    }

    setProduct(result.data);
    setEtag(result.etag);
    toast("success", publish ? "Your listing is live." : "Hidden from the catalogue. Buyers keep their access.");
  }

  const canPublish = product && (product.status === "Draft" || product.status === "Unpublished");

  return (
    <div className="grid grid-cols-1 gap-8 lg:grid-cols-[minmax(0,1fr)_320px]">
      <form className="flex flex-col gap-4 self-start rounded-card border border-border bg-surface p-6" onSubmit={save} noValidate>
        <h2 className="type-section m-0 text-text">Details</h2>
        {formError && <FormAlert>{formError}</FormAlert>}
        <Input label="Title" maxLength={120} hint="3 to 120 characters. Shown on the card and the product page." {...field("title")} />
        <Textarea label="Description" rows={8} maxLength={5000} hint="What the buyer gets, the format, and anything they need to use it." {...field("description")} />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input label="Price (EUR)" inputMode="decimal" placeholder="12.50" hint="€0.50 to €10,000." {...field("price")} />
          <Select label="Category" {...field("categoryId")}>
            <option value="">Choose…</option>
            {categories.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}
          </Select>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-3">
          <Link href="/seller" className="type-label text-muted hover:text-text">Cancel</Link>
          <Button type="submit" loading={saving}>{product ? "Save changes" : "Create draft"}</Button>
        </div>
      </form>

      <aside className="flex flex-col gap-6">
        {!product ? (
          <p className="m-0 rounded-card border border-dashed border-border-strong bg-surface p-4 type-label text-muted">
            Create the draft first. You can then add the file buyers download, a cover image, and publish it.
          </p>
        ) : (
          <>
            <section className="flex flex-col gap-3 rounded-card border border-border bg-surface p-4">
              <div className="flex items-center justify-between gap-2">
                <h2 className="type-card-title m-0 text-text">Status</h2>
                <StatusBadge status={product.status} />
              </div>
              {canPublish ? (
                <>
                  <Button loading={statusBusy} onClick={() => changeStatus(true)} disabled={!product.file} fullWidth>Publish</Button>
                  {!product.file && <p className="m-0 type-label text-muted">Upload the file buyers download before publishing.</p>}
                </>
              ) : product.status === "Published" ? (
                <>
                  <Link href={`/products/${product.id}`} className="type-label text-accent hover:underline">View in the catalogue</Link>
                  <Button variant="secondary" loading={statusBusy} onClick={() => changeStatus(false)} fullWidth>Unpublish</Button>
                  <p className="m-0 type-label text-muted">Unpublishing hides it from the catalogue. People who bought it keep their downloads.</p>
                </>
              ) : (
                <p className="m-0 type-label text-muted">This listing was removed by the Marketplace team and can&apos;t be changed.</p>
              )}
            </section>

            <section className="flex flex-col gap-3 rounded-card border border-border bg-surface p-4">
              <h2 className="type-card-title m-0 text-text">File</h2>
              {product.file ? (
                <p className="m-0 flex flex-col type-label">
                  <span className="truncate text-text">{product.file.name}</span>
                  <span className="text-muted">{size(product.file.sizeBytes)}</span>
                </p>
              ) : (
                <p className="m-0 type-label text-muted">No file yet.</p>
              )}
              <input ref={fileInput} type="file" accept={FILE_TYPES} className="sr-only" id="product-file" onChange={() => upload("file", fileInput.current)} />
              <Button variant="secondary" loading={uploading === "file"} onClick={() => fileInput.current?.click()} fullWidth>
                {product.file ? "Replace file" : "Upload file"}
              </Button>
              <p className="m-0 type-label text-muted">PDF, ZIP, EPUB, images, MP3, MP4, text or fonts, up to 100 MB.</p>
            </section>

            <section className="flex flex-col gap-3 rounded-card border border-border bg-surface p-4">
              <h2 className="type-card-title m-0 text-text">Cover</h2>
              <div className="overflow-hidden rounded-control">
                <Cover id={product.id} title={details.title || product.title} url={product.coverImageUrl} sizes="320px" />
              </div>
              <input ref={coverInput} type="file" accept={COVER_TYPES.join(",")} className="sr-only" id="product-cover" onChange={() => upload("cover", coverInput.current)} />
              <Button variant="secondary" loading={uploading === "cover"} onClick={() => coverInput.current?.click()} fullWidth>
                {product.coverImageUrl ? "Replace cover" : "Upload cover"}
              </Button>
              <p className="m-0 type-label text-muted">JPEG, PNG or WebP, up to 5 MB, shown at 4:3. Without one, a generated cover is used. Location and camera data are removed.</p>
            </section>
          </>
        )}
      </aside>
    </div>
  );
}
