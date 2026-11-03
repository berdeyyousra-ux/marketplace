import { AuthShell, StatusPanel } from "@/components/AuthShell";
import { LoginForm } from "./LoginForm";

const notices: Record<string, string> = {
  verified: "Your email is confirmed. Sign in to continue.",
  reset: "Your password is changed and every device that was signed in has been signed out. Sign in with the new password.",
};

export default async function LoginPage({ searchParams }: { searchParams: Promise<{ next?: string; verified?: string; reset?: string }> }) {
  const { next, verified, reset } = await searchParams;
  const notice = verified ? notices.verified : reset ? notices.reset : null;
  return (
    <AuthShell title="Sign in">
      {notice && <StatusPanel tone="success">{notice}</StatusPanel>}
      <LoginForm next={next && next.startsWith("/") && !next.startsWith("//") ? next : "/"} />
    </AuthShell>
  );
}
