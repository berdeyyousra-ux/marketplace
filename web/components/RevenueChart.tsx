"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Money } from "@/components/ui/Money";
import type { MonthlyRevenue } from "@/lib/types";

// Net sales per month (R-01): one series, one y-axis from zero, months on x. Built to the dataviz rules: a 2px line
// with a 10% wash, a recessive hairline grid, the latest value labelled at the line's end, a crosshair that snaps
// to the nearest month on hover or with the arrow keys, and a table view with every value, so nothing is hover-only.

const HEIGHT = 240;
const PAD = { top: 16, right: 76, bottom: 28, left: 52 };

const euro = new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR", maximumFractionDigits: 0 });
// Written out rather than Intl's compact notation, whose output differs between Node's and the browser's ICU data
// ("€0" against "€0.0"), which breaks hydration.
function euroCompact(value: number) {
  const sign = value < 0 ? "-" : "";
  const absolute = Math.abs(value);
  const scaled = (divisor: number, suffix: string) => `${sign}€${(absolute / divisor).toFixed(1).replace(/\.0$/, "")}${suffix}`;
  if (absolute >= 1_000_000) return scaled(1_000_000, "M");
  if (absolute >= 10_000) return scaled(1_000, "K");
  return euro.format(value);
}

function monthDate(month: string) {
  // "2026-05" or "2026-05-01T00:00:00Z": the first of that month, in UTC.
  const [year, number] = month.slice(0, 7).split("-").map(Number);
  return new Date(Date.UTC(year, number - 1, 1));
}

const monthShort = (date: Date) => date.toLocaleString("en-GB", { month: "short", timeZone: "UTC" });
const monthLong = (date: Date) => date.toLocaleString("en-GB", { month: "long", year: "numeric", timeZone: "UTC" });

// Round tick steps (1, 2 or 5 times a power of ten), about four intervals.
function ticks(min: number, max: number) {
  const span = Math.max(max - min, 1);
  const raw = span / 4;
  const power = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 5, 10].map((factor) => factor * power).find((candidate) => candidate >= raw) ?? raw;
  const low = Math.floor(min / step) * step;
  const high = Math.ceil(max / step) * step;
  const values: number[] = [];
  for (let value = low; value <= high + step / 2; value += step) values.push(Math.round(value * 100) / 100);
  return values;
}

export function RevenueChart({ months }: { months: MonthlyRevenue[] }) {
  const [view, setView] = useState<"chart" | "table">("chart");
  const [width, setWidth] = useState(720);
  const [active, setActive] = useState<number | null>(null);
  const frame = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const element = frame.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => setWidth(Math.max(280, Math.round(entry.contentRect.width))));
    observer.observe(element);
    return () => observer.disconnect();
  }, [view]);

  const points = useMemo(() => months.map((month) => ({ date: monthDate(month.month), value: Number(month.netSales.amount), source: month })), [months]);
  const values = points.map((point) => point.value);
  // The axis starts at zero; it goes below only when a month's refunds exceeded its sales.
  const yTicks = ticks(Math.min(0, ...values), Math.max(0, ...values));
  const yMin = yTicks[0];
  const yMax = yTicks[yTicks.length - 1];
  const plotWidth = width - PAD.left - PAD.right;
  const plotHeight = HEIGHT - PAD.top - PAD.bottom;
  const x = (index: number) => PAD.left + (points.length === 1 ? plotWidth / 2 : (index / (points.length - 1)) * plotWidth);
  const y = (value: number) => PAD.top + plotHeight - ((value - yMin) / (yMax - yMin || 1)) * plotHeight;
  const compact = Math.max(Math.abs(yMin), Math.abs(yMax)) >= 10000;

  // The current month is still running, so its low value is not a drop: its segment is dashed and its dot hollow.
  const now = new Date();
  const partial = points.length > 1 && points[points.length - 1].date.getUTCFullYear() === now.getUTCFullYear() && points[points.length - 1].date.getUTCMonth() === now.getUTCMonth();
  const solidCount = partial ? points.length - 1 : points.length;
  const path = (from: number, to: number) => points.slice(from, to).map((point, offset) => `${offset === 0 ? "M" : "L"}${x(from + offset).toFixed(1)},${y(point.value).toFixed(1)}`).join(" ");
  const line = path(0, points.length);
  const solidLine = path(0, solidCount);
  const partialLine = partial ? path(points.length - 2, points.length) : "";
  const area = points.length > 1 ? `${line} L${x(points.length - 1).toFixed(1)},${y(Math.max(0, yMin)).toFixed(1)} L${x(0).toFixed(1)},${y(Math.max(0, yMin)).toFixed(1)} Z` : "";
  // Month labels thin out on narrow screens; January also shows the year so the timeline reads across a year boundary.
  const labelEvery = Math.ceil(points.length / Math.max(2, Math.floor(plotWidth / 56)));
  const last = points.length - 1;

  function pointerMove(event: React.PointerEvent<SVGSVGElement>) {
    const box = event.currentTarget.getBoundingClientRect();
    const position = ((event.clientX - box.left) / box.width) * width;
    const index = points.length === 1 ? 0 : Math.round(((position - PAD.left) / plotWidth) * (points.length - 1));
    setActive(Math.min(last, Math.max(0, index)));
  }

  function keyDown(event: React.KeyboardEvent<SVGSVGElement>) {
    if (event.key === "ArrowRight" || event.key === "ArrowLeft") {
      event.preventDefault();
      const step = event.key === "ArrowRight" ? 1 : -1;
      setActive((current) => Math.min(last, Math.max(0, (current ?? last) + step)));
    } else if (event.key === "Home" || event.key === "End") {
      event.preventDefault();
      setActive(event.key === "Home" ? 0 : last);
    } else if (event.key === "Escape") {
      setActive(null);
    }
  }

  const total = values.reduce((sum, value) => sum + value, 0);
  const hovered = active === null ? null : points[active];
  const tooltipLeft = active === null ? 0 : Math.min(Math.max(x(active), 80), width - 80);

  return (
    <figure className="m-0 flex flex-col gap-4 rounded-card border border-border bg-surface p-4 md:p-6">
      <figcaption className="flex flex-wrap items-start justify-between gap-3">
        <span className="flex flex-col gap-1">
          <span className="type-card-title text-text">Net sales by month</span>
          <span className="type-label text-muted">Sales minus refunds, before platform fees. Last {points.length} months: {euro.format(total)}.{partial ? ` ${monthLong(points[last].date).split(" ")[0]} is still running (dashed).` : ""}</span>
        </span>
        <button
          type="button"
          onClick={() => setView(view === "chart" ? "table" : "chart")}
          aria-pressed={view === "table"}
          className="cursor-pointer rounded-control border border-border-strong bg-surface px-3 py-1 type-label text-accent hover:border-accent"
        >
          {view === "chart" ? "Show table" : "Show chart"}
        </button>
      </figcaption>

      {view === "table" ? (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse">
            <thead>
              <tr className="border-b border-border text-left">
                <th scope="col" className="py-2 pr-4 type-micro text-muted">Month</th>
                <th scope="col" className="py-2 pr-4 text-right type-micro text-muted">Net sales</th>
                <th scope="col" className="py-2 pr-4 text-right type-micro text-muted">Platform fees</th>
                <th scope="col" className="py-2 text-right type-micro text-muted">Your earnings</th>
              </tr>
            </thead>
            <tbody>
              {points.map((point) => (
                <tr key={point.source.month} className="border-b border-border last:border-b-0">
                  <th scope="row" className="py-2 pr-4 text-left type-label font-normal text-text">{monthLong(point.date)}{partial && point === points[last] ? " (so far)" : ""}</th>
                  <td className="py-2 pr-4 text-right type-label text-text"><Money value={point.source.netSales} /></td>
                  <td className="py-2 pr-4 text-right type-label text-muted"><Money value={point.source.platformFees} /></td>
                  <td className="py-2 text-right type-label text-text"><Money value={point.source.sellerNet} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div ref={frame} className="relative w-full">
          <svg
            width={width}
            height={HEIGHT}
            viewBox={`0 0 ${width} ${HEIGHT}`}
            className="block max-w-full touch-pan-y outline-none focus-visible:outline-2 focus-visible:outline-accent"
            role="img"
            aria-label={`Line chart of net sales by month, ${monthLong(points[0].date)} to ${monthLong(points[last].date)}. Latest month ${euro.format(points[last].value)}. Use the arrow keys to read each month, or show the table.`}
            tabIndex={0}
            onPointerMove={pointerMove}
            onPointerLeave={() => setActive(null)}
            onKeyDown={keyDown}
            onFocus={() => setActive((current) => current ?? last)}
            onBlur={() => setActive(null)}
          >
            {yTicks.map((tick) => (
              <g key={tick}>
                <line x1={PAD.left} x2={width - PAD.right} y1={y(tick)} y2={y(tick)} stroke="var(--border)" strokeWidth={1} shapeRendering="crispEdges" />
                <text x={PAD.left - 8} y={y(tick)} dy="0.32em" textAnchor="end" fill="var(--text-muted)" fontSize={12} style={{ fontVariantNumeric: "tabular-nums" }}>
                  {compact ? euroCompact(tick) : euro.format(tick)}
                </text>
              </g>
            ))}

            {points.map((point, index) =>
              index % labelEvery === 0 || index === last ? (
                <text key={point.source.month} x={x(index)} y={HEIGHT - 8} textAnchor="middle" fill="var(--text-muted)" fontSize={12}>
                  {monthShort(point.date)}{point.date.getUTCMonth() === 0 || index === 0 ? ` ${String(point.date.getUTCFullYear()).slice(2)}` : ""}
                </text>
              ) : null,
            )}

            {area && <path d={area} fill="var(--chart-1)" fillOpacity={0.1} />}
            <path d={solidLine} fill="none" stroke="var(--chart-1)" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
            {partialLine && <path d={partialLine} fill="none" stroke="var(--chart-1)" strokeWidth={2} strokeDasharray="4 4" strokeLinecap="round" />}

            {active !== null && (
              <line x1={x(active)} x2={x(active)} y1={PAD.top} y2={PAD.top + plotHeight} stroke="var(--border-strong)" strokeWidth={1} shapeRendering="crispEdges" />
            )}

            {/* The latest month: an end dot with a surface ring, and its value as the one direct label. */}
            <circle cx={x(last)} cy={y(points[last].value)} r={4} fill={partial ? "var(--surface)" : "var(--chart-1)"} stroke={partial ? "var(--chart-1)" : "var(--surface)"} strokeWidth={2} />
            <text x={x(last) + 8} y={y(points[last].value)} dy="0.32em" fill="var(--text)" fontSize={12} fontWeight={600} style={{ fontVariantNumeric: "tabular-nums" }}>
              {euroCompact(points[last].value)}{partial ? " so far" : ""}
            </text>

            {active !== null && active !== last && (
              <circle cx={x(active)} cy={y(points[active].value)} r={4} fill="var(--chart-1)" stroke="var(--surface)" strokeWidth={2} />
            )}
          </svg>

          {hovered && (
            <div
              role="status"
              className="pointer-events-none absolute top-0 flex -translate-x-1/2 flex-col gap-1 rounded-control border border-border bg-surface px-3 py-2 shadow-raised"
              style={{ left: tooltipLeft }}
            >
              <span className="type-label text-muted">{monthLong(hovered.date)}{partial && active === last ? ", so far" : ""}</span>
              <span className="flex items-center gap-2">
                <span aria-hidden="true" className="inline-block h-0.5 w-3 rounded-full bg-chart-1" />
                <Money value={hovered.source.netSales} className="type-card-title font-semibold text-text" />
              </span>
              <span className="type-label text-muted">Your earnings <Money value={hovered.source.sellerNet} /></span>
            </div>
          )}
        </div>
      )}
    </figure>
  );
}
