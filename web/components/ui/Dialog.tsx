"use client";

import { useEffect, useRef } from "react";

// A modal on the native <dialog>: focus is trapped and Escape closes it. 14px radius and the raised shadow.
export function Dialog({ open, onClose, title, children }: { open: boolean; onClose: () => void; title: string; children: React.ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
  }, [open]);

  return (
    <dialog
      ref={ref}
      onClose={onClose}
      aria-labelledby="dialog-title"
      className="m-auto w-[min(32rem,calc(100vw-2rem))] rounded-modal border border-border bg-surface p-0 text-text shadow-raised backdrop:bg-text/40"
    >
      <div className="flex flex-col gap-4 p-6">
        <h2 id="dialog-title" className="type-section m-0">{title}</h2>
        {children}
      </div>
    </dialog>
  );
}
