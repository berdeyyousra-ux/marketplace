import { AuthShell } from "@/components/AuthShell";
import { Verify } from "./Verify";

export default async function VerifyEmailPage({ searchParams }: { searchParams: Promise<{ token?: string }> }) {
  const { token } = await searchParams;
  return (
    <AuthShell title="Confirm your email">
      <Verify token={token ?? null} />
    </AuthShell>
  );
}
