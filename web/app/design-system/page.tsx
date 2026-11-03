import type { Metadata } from "next";
import { Badge, StatusBadge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { coverIndex, GeneratedCover } from "@/components/ui/Cover";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Select, Textarea } from "@/components/ui/Field";
import { Money } from "@/components/ui/Money";
import { PageHeader } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { ProductCard } from "@/components/ui/ProductCard";
import { SkeletonCard, SkeletonRow, SkeletonText } from "@/components/ui/Skeleton";
import { StatTile } from "@/components/ui/StatTile";
import { ToastPreview } from "@/components/ui/Toast";

export const metadata: Metadata = { title: "Design system · Marketplace", robots: { index: false } };

// T-086: every component in every state, in light and dark side by side. A reference for building screens and for
// reviewing the tokens; it calls no API.
const tokens = [
  ["--bg", "Page background"], ["--surface", "Cards, panels"], ["--border", "Dividers, card edges"], ["--border-strong", "Form-control borders"],
  ["--text", "Primary text"], ["--text-muted", "Labels, metadata"], ["--accent", "Actions, links, focus"], ["--accent-hover", "Hover on accent"],
  ["--accent-contrast", "Text on accent"], ["--danger", "Errors, destructive"], ["--success", "Paid, completed"], ["--warning", "Pending, executing"],
] as const;

const statuses = ["Paid", "Completed", "Published", "Pending", "Executing", "RequiresPayment", "Draft", "Cancelled", "Expired", "Failed", "Rejected", "SomethingNew"];

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-4 border-t border-border pt-6">
      <h2 className="type-section text-text">{title}</h2>
      {children}
    </section>
  );
}

function Gallery() {
  const product = { id: "0192710a-56c5-7437-95f6-574a6466ba2a", title: "Minimal Notion Budget Template with a long title that wraps", price: { amount: "19.99", currency: "EUR" } };
  return (
    <div className="flex flex-col gap-8">
      <Section title="Tokens">
        <div className="grid grid-cols-2 gap-2">
          {tokens.map(([name, use]) => (
            <div key={name} className="flex items-center gap-3">
              <span className="size-8 shrink-0 rounded-control border border-border" style={{ background: `var(${name})` }} />
              <span className="flex flex-col">
                <code className="type-label text-text">{name}</code>
                <span className="type-label text-muted">{use}</span>
              </span>
            </div>
          ))}
        </div>
      </Section>

      <Section title="Type">
        <p className="type-page-title text-text">Page title 32/600</p>
        <p className="type-section text-text">Section heading 20/600</p>
        <p className="type-card-title text-text">Card title 15/500</p>
        <p className="type-body text-text">Body 15/400. Digital goods made with care, with clear instructions and free updates.</p>
        <p className="type-price text-text">€1,234.56 price</p>
        <p className="type-label text-muted">Label and metadata 13/500</p>
        <p className="type-micro text-muted">Micro badge 11/600</p>
      </Section>

      <Section title="Buttons">
        {(["primary", "secondary", "ghost", "danger"] as const).map((variant) => (
          <div key={variant} className="flex flex-wrap items-center gap-3">
            <Button variant={variant}>{variant}</Button>
            <Button variant={variant} loading>{variant} loading</Button>
            <Button variant={variant} disabled>{variant} disabled</Button>
          </div>
        ))}
        <Button fullWidth>Full width primary</Button>
      </Section>

      <Section title="Form controls">
        <Input label="Email" placeholder="you@example.com" />
        <Input label="Email (focused)" defaultValue="buyer@example.com" className="outline-2 outline-offset-2 outline-accent" />
        <Input label="Password" type="password" defaultValue="short" error="Use at least 10 characters." />
        <Input label="Shop name" defaultValue="Pixel Studio" disabled hint="Set when the profile was created." />
        <Textarea label="Reason" placeholder="What went wrong?" hint="10 to 2000 characters. An admin reviews every request." />
        <Select label="Sort" defaultValue="Newest">
          <option>Newest</option>
          <option>Price, low to high</option>
        </Select>
        <Select label="Category" error="Choose a category." defaultValue="">
          <option value="">Choose…</option>
        </Select>
      </Section>

      <Section title="Cards">
        <Card className="p-4"><p className="type-body text-text">Plain card</p></Card>
        <Card interactive className="p-4"><p className="type-body text-text">Interactive card (hover changes the border)</p></Card>
      </Section>

      <Section title="Product cards">
        <div className="grid grid-cols-2 gap-6">
          <ProductCard product={{ ...product, coverImageUrl: "/design/sample-cover.png", sellerName: "Pixel Studio" }} />
          <ProductCard product={{ ...product, id: "0191b36c-f3d1-7054-aa1f-e63381bb12b8", title: "Lo-fi Beat Pack 3", sellerName: "Quill Audio" }} />
        </div>
      </Section>

      <Section title="Generated covers (all eight)">
        <div className="grid grid-cols-4 gap-2">
          {["a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "m"].map((seed) => `seed-${seed}`)
            .filter((id, index, ids) => ids.findIndex((other) => coverIndex(other) === coverIndex(id)) === index).slice(0, 8)
            .map((id) => <GeneratedCover key={id} id={id} title={id.slice(5)} className="rounded-control" />)}
        </div>
      </Section>

      <Section title="Badges">
        <div className="flex flex-wrap gap-2">
          <Badge>Neutral</Badge><Badge tone="success">Success</Badge><Badge tone="warning">Warning</Badge><Badge tone="danger">Danger</Badge>
        </div>
        <div className="flex flex-wrap gap-2">{statuses.map((status) => <StatusBadge key={status} status={status} />)}</div>
      </Section>

      <Section title="Money">
        <div className="flex flex-col items-end gap-1 type-body text-text">
          <Money value={{ amount: "9.99", currency: "EUR" }} />
          <Money value={{ amount: "1234.50", currency: "EUR" }} />
          <Money value={{ amount: "-17.99", currency: "EUR" }} />
          <Money value={null} />
        </div>
      </Section>

      <Section title="Stat tiles">
        <div className="grid grid-cols-2 gap-4">
          <StatTile label="Revenue, 30 days" value={<Money value={{ amount: "4812.30", currency: "EUR" }} />} change={{ text: "+12% on the previous 30 days", direction: "up" }} />
          <StatTile label="Refund rate" value="2.4%" change={{ text: "+0.6 points", direction: "down" }} />
          <StatTile label="Orders" value="318" />
          <StatTile label="Active listings" value="12" change={{ text: "No change", direction: "flat" }} />
        </div>
      </Section>

      <Section title="Page header">
        <PageHeader title="Library" description="Everything you have bought, ready to download." action={<Button variant="secondary">Browse</Button>} />
      </Section>

      <Section title="Empty state">
        <EmptyState message="Nothing in your library yet." action={<Button>Browse products</Button>} />
      </Section>

      <Section title="Skeletons">
        <div className="grid grid-cols-2 gap-6"><SkeletonCard /><SkeletonCard /></div>
        <div><SkeletonRow /><SkeletonRow /></div>
        <SkeletonText />
      </Section>

      <Section title="Toasts">
        <div className="flex flex-wrap gap-3">
          <ToastPreview tone="success" message="Refund request sent. An admin will review it." />
          <ToastPreview tone="error" message="The download link could not be created." />
        </div>
      </Section>

      <Section title="Pagination">
        <Pagination nextHref="#next" firstHref="#first" onFirstPage />
        <Pagination nextHref={null} firstHref="#first" onFirstPage={false} />
      </Section>
    </div>
  );
}

export default function DesignSystemPage() {
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title="Design system" description="Tokens and components from docs/frontend-design-plan.md, in light and dark." />
      <div className="grid gap-6 xl:grid-cols-2">
        {(["light", "dark"] as const).map((theme) => (
          <div key={theme} data-theme={theme} className="rounded-modal border border-border bg-bg p-6 text-text">
            <p className="type-micro mb-4 text-muted">{theme}</p>
            <Gallery />
          </div>
        ))}
      </div>
    </div>
  );
}
