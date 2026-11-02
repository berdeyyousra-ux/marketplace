import Link from "next/link";
import { serverGet } from "@/lib/api";
import { date, label, money } from "@/lib/format";
import type { AdminRefundDetail } from "@/lib/types";
import { StatusBadge } from "@/components/StatusBadge";
import { DecisionForm } from "./DecisionForm";

// EP-63 and section 12.6: the facts sent, the policy verdict, the suggestion with its flags, the model and prompt
// version, or a clear note that no suggestion is available. The decision is always the admin's (D-37).
export default async function RefundDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const detail = await serverGet<AdminRefundDetail>(`/admin/refund-requests/${id}`, `/admin/refunds/${id}`);
  if (!detail.ok) {
    return <p className="error">{detail.status === 403 ? "Refund review is for admins." : "This refund request could not be found."}</p>;
  }

  const { data } = detail;
  const ai = data.aiSuggestion;
  return (
    <div className="stack" style={{ maxWidth: 860 }}>
      <p className="small"><Link href="/admin/refunds">← Refund queue</Link></p>
      <h1>{data.productTitle}</h1>
      <div className="row">
        <StatusBadge value={data.status} />
        <span>Policy: <StatusBadge value={data.policyVerdict} /></span>
        <span className="muted small">{money(data.unitPrice)} · requested {date(data.createdAt)} · {data.downloadCountAtRequest} downloads at request</span>
      </div>

      <div className="card stack">
        <h2 style={{ marginTop: 0 }}>Buyer&apos;s message</h2>
        <p style={{ whiteSpace: "pre-wrap" }}>{data.buyerMessage}</p>
      </div>

      <div className="card stack">
        <h2 style={{ marginTop: 0 }}>AI suggestion</h2>
        {!ai ? (
          <p className="muted">Not analyzed yet. The request can be decided without it.</p>
        ) : ai.suggestion ? (
          <>
            <div className="row">
              <StatusBadge value={ai.suggestion.recommendedAction} />
              <span>{label(ai.suggestion.category)}</span>
              <span className="muted small">confidence {Math.round(ai.suggestion.confidence * 100)}%</span>
            </div>
            <p>{ai.suggestion.summary}</p>
            <ul>{ai.suggestion.reasons.map((reason) => <li key={reason}>{reason}</li>)}</ul>
            {ai.suggestion.flags.length > 0 && (
              <div className="row">{ai.suggestion.flags.map((flag) => <span key={flag} className="badge warn">{label(flag)}</span>)}</div>
            )}
          </>
        ) : (
          <p className="notice">No suggestion is available ({label(ai.status)}{ai.error ? `: ${ai.error}` : ""}). Decide from the facts below.</p>
        )}
        {ai && <p className="small muted">{ai.model} · {ai.promptVersion} · {date(ai.createdAt)}</p>}
        {ai && (
          <details>
            <summary className="small">Facts sent to the model</summary>
            <pre>{JSON.stringify(ai.facts, null, 2)}</pre>
          </details>
        )}
      </div>

      {data.decision ? (
        <div className="card stack">
          <h2 style={{ marginTop: 0 }}>Decision</h2>
          <p><StatusBadge value={data.decision} /> {date(data.decidedAt)}</p>
          <p>{data.decisionNote}</p>
          {data.overrideReason && <p className="small">Override reason: {data.overrideReason}</p>}
          {data.failureReason && <p className="error small">Execution failed: {data.failureReason}</p>}
          {data.executedAt && <p className="small muted">Refunded {date(data.executedAt)}</p>}
        </div>
      ) : null}

      <DecisionForm id={data.id} status={data.status} needsOverride={data.policyVerdict === "IneligibleOutsideWindow"} />

      <h2>History</h2>
      <div className="table-wrap">
        <table>
          <tbody>
            {data.history.map((entry, index) => (
              <tr key={index}><td>{date(entry.occurredAt)}</td><td>{entry.action}</td><td className="muted">{entry.actorType}</td></tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
