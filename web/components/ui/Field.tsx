import { useId } from "react";
import { cn } from "@/lib/cn";

type FieldProps = { label: string; hint?: string; error?: string; children: (props: { id: string; describedBy?: string; invalid: boolean }) => React.ReactNode };

// A real label, an optional hint, and an error tied to the control with aria-describedby.
function Field({ label, hint, error, children }: FieldProps) {
  const id = useId();
  const hintId = hint ? `${id}-hint` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(" ") || undefined;
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="type-label text-text">{label}</label>
      {children({ id, describedBy, invalid: Boolean(error) })}
      {hint && !error && <p id={hintId} className="type-label text-muted">{hint}</p>}
      {error && <p id={errorId} className="type-label text-danger" role="alert">{error}</p>}
    </div>
  );
}

const control = (invalid: boolean) =>
  cn(
    "w-full rounded-control border bg-surface px-3 py-2 type-body text-text placeholder:text-muted",
    "disabled:cursor-not-allowed disabled:opacity-55",
    invalid ? "border-danger" : "border-border-strong",
  );

type Common = { label: string; hint?: string; error?: string };

export function Input({ label, hint, error, className, ...rest }: Common & React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <Field label={label} hint={hint} error={error}>
      {({ id, describedBy, invalid }) => (
        <input id={id} aria-describedby={describedBy} aria-invalid={invalid || undefined} className={cn(control(invalid), className)} {...rest} />
      )}
    </Field>
  );
}

export function Textarea({ label, hint, error, className, ...rest }: Common & React.TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <Field label={label} hint={hint} error={error}>
      {({ id, describedBy, invalid }) => (
        <textarea id={id} aria-describedby={describedBy} aria-invalid={invalid || undefined} className={cn(control(invalid), "min-h-24", className)} {...rest} />
      )}
    </Field>
  );
}

export function Select({ label, hint, error, className, children, ...rest }: Common & React.SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <Field label={label} hint={hint} error={error}>
      {({ id, describedBy, invalid }) => (
        <select id={id} aria-describedby={describedBy} aria-invalid={invalid || undefined} className={cn(control(invalid), className)} {...rest}>
          {children}
        </select>
      )}
    </Field>
  );
}
