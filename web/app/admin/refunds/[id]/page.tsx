import Link from "next/link";
import { serverGet } from "@/lib/api";
import { date, label, shortId } from "@/lib/format";
import { absenceReason, aiActionLabel } from "@/lib/ai";
import type { AdminRefundDetail } from "@/lib/types";
import { Badge, StatusBadge } from "@/components/ui/Badge";
import { Money } from "@/components/ui/Money";
import { FormAlert } from "@/components/FormAlert";
import { StatusPanel } from "@/components/AuthShell";
import { DecisionPanel } from "./DecisionForm";

function Fact({ term, children }: { term: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1 sm:flex-row sm:gap-4">
      <dt className="w-44 shrink-0 type-label text-muted">{term}</dt>
      <dd className="m-0 type-label text-text">{children}</dd>
    </div>
  );
}

const verdictText: Record<string, string> = {
  Eligible: "Eligible: inside the refund window and not downloaded",
  EligibleDownloaded: "Inside the window, but already downloaded",
  IneligibleOutsideWindow: "Outside the refund window: approving needs an override reason",
};

// EP-63 and section 12.6. Facts on the left, the advisory AI panel on the right, the decision below both. The decision
// is always the admin's (D-37): nothing here is chosen for them.
export default async function RefundDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const detail = await serverGet<AdminRefundDetail>(`/admin/refund-requests/${id}`, `/admin/refunds/${id}`);
  if (!detail.ok) {
    return (
      <div className="container-page py-8">
        <FormAlert>{detail.status === 403 ? "Refund review is for admins." : "This refund request could not be found."}</FormAlert>
      </div>
    );
  }

  const { data } = detail;
  const ai = data.aiSuggestion;
  const suggestion = ai?.suggestion ?? null;
  return (
    <div className="container-page flex flex-col gap-8 py-8">
      <header className="flex flex-col gap-2">
        <Link href="/admin/refunds" className="type-label text-accent-strong hover:underline">← Refund requests</Link>
        <h1 className="type-page-title m-0 text-text">{data.productTitle}</h1>
        <p className="m-0 flex flex-wrap items-center gap-2 type-label text-muted">
          <StatusBadge status={data.status} />
          <span>Requested {date(data.createdAt)}</span>
        </p>
      </header>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <section aria-labelledby="facts" className="flex flex-col gap-6 rounded-card border border-border bg-surface p-6">
          <h2 id="facts" className="type-section m-0 text-text">Facts</h2>
          <dl className="m-0 flex flex-col gap-3">
            <Fact term="Amount"><Money value={data.unitPrice} className="font-semibold" /></Fact>
            <Fact term="Order"><span className="font-mono">{shortId(data.orderId)}</span></Fact>
            <Fact term="Policy">{verdictText[data.policyVerdict] ?? label(data.policyVerdict)}</Fact>
            <Fact term="Downloads at request">{data.downloadCountAtRequest}</Fact>
            <Fact term="Buyer">
              {data.buyer?.displayName ?? "Deleted account"}
              {data.buyer?.email && <span className="block text-muted">{data.buyer.email}</span>}
            </Fact>
            {data.buyer && <Fact term="Buyer's paid orders">{data.buyer.paidOrders}</Fact>}
          </dl>

          <div className="flex flex-col gap-2">
            <h3 className="type-card-title m-0 text-text">Buyer&apos;s message</h3>
            <p className="m-0 whitespace-pre-wrap rounded-control bg-bg p-3 type-body text-text">{data.buyerMessage}</p>
          </div>

          <div className="flex flex-col gap-2">
            <h3 className="type-card-title m-0 text-text">Buyer&apos;s other refund requests</h3>
            {!data.buyer || data.buyer.otherRequests.length === 0 ? (
              <p className="m-0 type-label text-muted">None. This is their first.</p>
            ) : (
              <ul className="m-0 flex list-none flex-col gap-2 p-0">
                {data.buyer.otherRequests.map((other) => (
                  <li key={other.id} className="flex flex-wrap items-center justify-between gap-2 type-label">
                    <Link href={`/admin/refunds/${other.id}`} className="text-accent-strong hover:underline">{other.productTitle}</Link>
                    <span className="flex items-center gap-2 text-muted">{date(other.createdAt)} <StatusBadge status={other.status} /></span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </section>

        <section aria-labelledby="ai" className="flex flex-col gap-4 self-start rounded-card border border-dashed border-border-strong bg-bg p-6">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 id="ai" className="type-section m-0 text-text">AI suggestion <span className="font-normal text-muted">(advisory)</span></h2>
            {suggestion && <Badge>{aiActionLabel(suggestion.recommendedAction)}</Badge>}
          </div>
          {!ai ? (
            <p className="m-0 type-body text-muted">Not analysed yet. You can decide without it.</p>
          ) : !suggestion ? (
            <p className="m-0 type-body text-muted">No suggestion: {absenceReason(ai.status)}. Decide from the facts.</p>
          ) : (
            <>
              <dl className="m-0 flex flex-col gap-3">
                <Fact term="Category">{label(suggestion.category)}</Fact>
                <Fact term="Confidence">{Math.round(suggestion.confidence * 100)}%</Fact>
              </dl>
              <p className="m-0 type-body text-text">{suggestion.summary}</p>
              {suggestion.reasons.length > 0 && (
                <ul className="m-0 flex list-disc flex-col gap-1 pl-5 type-label text-text">
                  {suggestion.reasons.map((reason) => <li key={reason}>{reason}</li>)}
                </ul>
              )}
              {suggestion.flags.length > 0 && (
                <div className="flex flex-wrap gap-2">
                  {suggestion.flags.map((flag) => <Badge key={flag} tone="warning">{label(flag)}</Badge>)}
                </div>
              )}
            </>
          )}
          {ai && (
            <details className="type-label text-muted">
              <summary className="cursor-pointer">How this was produced</summary>
              <p className="my-2">{ai.model} · prompt {ai.promptVersion} · {date(ai.createdAt)}</p>
              <pre className="m-0 overflow-x-auto rounded-control bg-surface p-3 font-mono text-[12px] text-text">{JSON.stringify(ai.facts, null, 2)}</pre>
            </details>
          )}
        </section>
      </div>

      {data.decision && (
        <StatusPanel tone={data.failureReason ? "danger" : "neutral"}>
          <p className="m-0 flex flex-wrap items-center gap-2 font-medium"><StatusBadge status={data.decision} /> {date(data.decidedAt)}</p>
          <p className="m-0">Note to the buyer: {data.decisionNote}</p>
          {data.overrideReason && <p className="m-0 type-label">Override reason: {data.overrideReason}</p>}
          {data.failureReason && <p className="m-0 type-label text-danger">The refund failed: {data.failureReason}</p>}
          {data.executedAt && <p className="m-0 type-label text-muted">Refunded {date(data.executedAt)}</p>}
        </StatusPanel>
      )}

      <DecisionPanel
        id={data.id}
        status={data.status}
        policyVerdict={data.policyVerdict}
        aiAction={suggestion?.recommendedAction ?? null}
      />

      <section aria-labelledby="history" className="flex flex-col gap-3">
        <h2 id="history" className="type-section m-0 text-text">History</h2>
        <ol className="m-0 flex list-none flex-col gap-2 border-l border-border pl-4">
          {data.history.map((entry, index) => (
            <li key={index} className="type-label text-text">
              <span className="font-mono">{entry.action}</span> <span className="text-muted">· {entry.actorType} · {date(entry.occurredAt)}</span>
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}
