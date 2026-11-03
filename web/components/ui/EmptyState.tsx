// One icon, one line of explanation, one action.
export function EmptyState({ message, action }: { message: string; action?: React.ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-4 rounded-card border border-dashed border-border-strong bg-surface px-6 py-12 text-center">
      <svg aria-hidden="true" viewBox="0 0 24 24" className="size-8 text-muted" fill="none" stroke="currentColor" strokeWidth="1.5">
        <path d="M3 7l9-4 9 4v10l-9 4-9-4V7z" />
        <path d="M3 7l9 4 9-4M12 11v10" />
      </svg>
      <p className="type-body text-muted">{message}</p>
      {action}
    </div>
  );
}
