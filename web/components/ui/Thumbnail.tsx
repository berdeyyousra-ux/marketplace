import { Cover } from "./Cover";
import { cn } from "@/lib/cn";

// A small 4:3 cover for rows and summaries. The title sits beside it, so the generated cover stays decorative.
export function Thumbnail({ id, title, url, className }: { id: string; title: string; url: string | null | undefined; className?: string }) {
  return (
    <div className={cn("w-16 shrink-0 overflow-hidden rounded-control [&_span]:text-[22px]", className)}>
      <Cover id={id} title={title} url={url} sizes="64px" />
    </div>
  );
}
