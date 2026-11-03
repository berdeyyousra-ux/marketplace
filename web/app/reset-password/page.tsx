import { AuthShell } from "@/components/AuthShell";
import { ResetForm } from "./ResetForm";

export default async function ResetPasswordPage({ searchParams }: { searchParams: Promise<{ token?: string }> }) {
  const { token } = await searchParams;
  return (
    <AuthShell title="Set a new password">
      <ResetForm token={token ?? null} />
    </AuthShell>
  );
}
