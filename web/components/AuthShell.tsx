// The narrow centred column every account screen uses (400px).
export function AuthShell({ title, description, children }: { title: string; description?: string; children: React.ReactNode }) {
  return (
    <div className="mx-auto flex w-full max-w-[400px] flex-col gap-6 py-8">
      <div className="flex flex-col gap-2">
        <h1 className="type-page-title m-0 text-text">{title}</h1>
        {description && <p className="m-0 type-body text-muted">{description}</p>}
      </div>
      {children}
    </div>
  );
}

// A status panel for pages users reach from an email link, with no other context.
export function StatusPanel({ tone, children }: { tone: "success" | "danger" | "neutral"; children: React.ReactNode }) {
  const border = tone === "success" ? "border-success" : tone === "danger" ? "border-danger" : "border-border";
  return <div role="status" className={`flex flex-col gap-3 rounded-card border ${border} bg-surface p-4 type-body text-text`}>{children}</div>;
}
