import { Restore } from "./Restore";

// Only same-site paths, so the return cannot be pointed elsewhere.
function safeNext(value: string | undefined) {
  return value && value.startsWith("/") && !value.startsWith("//") ? value : "/";
}

export default async function SessionRefreshPage({ searchParams }: { searchParams: Promise<{ next?: string }> }) {
  const { next } = await searchParams;
  return <Restore next={safeNext(next)} />;
}
