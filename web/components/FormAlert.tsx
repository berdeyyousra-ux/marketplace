// A form-level error (wrong credentials, an expired link): above the fields, announced, never colour alone.
export function FormAlert({ children }: { children: React.ReactNode }) {
  return (
    <div role="alert" className="rounded-control border border-danger bg-surface px-4 py-3 type-label text-danger">
      {children}
    </div>
  );
}
