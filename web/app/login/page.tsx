import { LoginForm } from "./LoginForm";

export default async function LoginPage({ searchParams }: { searchParams: Promise<{ next?: string; verified?: string }> }) {
  const { next, verified } = await searchParams;
  return (
    <div className="stack">
      <h1>Sign in</h1>
      {verified && <p className="notice">Your email is confirmed. You can sign in now.</p>}
      <LoginForm next={next && next.startsWith("/") && !next.startsWith("//") ? next : "/"} />
    </div>
  );
}
