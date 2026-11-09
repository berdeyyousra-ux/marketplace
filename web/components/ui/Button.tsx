import { cn } from "@/lib/cn";

type Variant = "primary" | "accent" | "secondary" | "ghost" | "danger";

const variants: Record<Variant, string> = {
  // Near-black in light mode, near-white in dark: the strongest element on the page, kept for the main action.
  primary: "bg-cta text-cta-contrast border-cta hover:bg-cta-hover hover:border-cta-hover",
  // Orange fill for a deliberate secondary action beside a primary one; not a default.
  accent: "bg-accent-strong text-accent-contrast border-accent-strong hover:bg-accent-hover hover:border-accent-hover",
  secondary: "bg-surface text-accent-strong border-border-strong hover:border-accent",
  ghost: "bg-transparent text-text border-transparent hover:bg-border",
  danger: "bg-surface text-danger border-danger hover:bg-danger hover:text-surface",
};

type Props = React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; loading?: boolean; fullWidth?: boolean };

// While loading, the label stays in place (invisible) under the spinner, so the button keeps its width.
export function Button({ variant = "primary", loading = false, fullWidth = false, disabled, className, children, ...rest }: Props) {
  return (
    <button
      {...rest}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={cn(
        "relative inline-flex items-center justify-center gap-2 rounded-control border px-4 py-2 type-label cursor-pointer",
        // A loading button is busy, not unavailable: it keeps its colour and only refuses clicks.
        loading ? "cursor-wait" : "disabled:cursor-not-allowed disabled:opacity-55",
        variants[variant],
        fullWidth && "w-full",
        className,
      )}
    >
      <span className={cn("inline-flex items-center gap-2", loading && "invisible")}>{children}</span>
      {loading && (
        <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
          <span className="size-4 animate-spin rounded-full border-2 border-current border-t-transparent" />
        </span>
      )}
    </button>
  );
}
