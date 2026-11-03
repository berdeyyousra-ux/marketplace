import Image from "next/image";
import { cn } from "@/lib/cn";

// The same product always gets the same cover: a gradient chosen from eight approved pairs by its id, with its first
// letter. Decorative, so alt="" (the title is next to it). Pure and computed on render: nothing stored, nothing to clean.
export function coverIndex(id: string) {
  let hash = 0;
  for (const character of id) {
    hash = (hash * 31 + character.charCodeAt(0)) >>> 0;
  }

  return (hash % 8) + 1;
}

export function GeneratedCover({ id, title, className }: { id: string; title: string; className?: string }) {
  const index = coverIndex(id);
  const letter = (title.trim().match(/[\p{L}\p{N}]/u)?.[0] ?? "?").toUpperCase();
  return (
    <div
      aria-hidden="true"
      className={cn("flex aspect-[4/3] w-full items-center justify-center", className)}
      style={{ backgroundImage: `linear-gradient(135deg, var(--cover-${index}-from), var(--cover-${index}-to))` }}
    >
      <span className="text-[56px] font-semibold leading-none" style={{ color: "var(--cover-letter)" }}>{letter}</span>
    </div>
  );
}

// A product's cover at 4:3, or its generated placeholder. sizes must describe the layout, or phones get desktop images.
export function Cover({ id, title, url, sizes, priority = false, className }: {
  id: string; title: string; url: string | null | undefined; sizes: string; priority?: boolean; className?: string;
}) {
  if (!url) {
    return <GeneratedCover id={id} title={title} className={className} />;
  }

  return (
    <div className={cn("relative aspect-[4/3] w-full overflow-hidden", className)}>
      <Image src={url} alt={`Cover of ${title}`} fill sizes={sizes} priority={priority} className="object-cover" />
    </div>
  );
}
