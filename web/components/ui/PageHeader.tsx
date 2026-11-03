export function PageHeader({ title, description, action }: { title: string; description?: string; action?: React.ReactNode }) {
  return (
    <header className="mb-8 flex flex-wrap items-end justify-between gap-4">
      <div className="flex flex-col gap-2">
        <h1 className="type-page-title text-text">{title}</h1>
        {description && <p className="type-body text-muted">{description}</p>}
      </div>
      {action}
    </header>
  );
}
