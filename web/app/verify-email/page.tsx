"use client";

import { useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { describe } from "@/lib/client";

function Verify() {
  const token = useSearchParams().get("token");
  const [error, setError] = useState<string | null>(token ? null : "This link has no token.");

  useEffect(() => {
    if (!token) {
      return;
    }

    fetch("/api/session/verify", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ token }) })
      .then(async (response) => {
        if (response.ok) {
          window.location.href = "/login?verified=1";
        } else {
          setError(describe(await response.json().catch(() => ({}))));
        }
      });
  }, [token]);

  return error ? <p className="error">{error}</p> : <p>Confirming your email…</p>;
}

export default function VerifyEmailPage() {
  return (
    <div className="stack">
      <h1>Confirm your email</h1>
      <Suspense fallback={<p>Confirming your email…</p>}>
        <Verify />
      </Suspense>
    </div>
  );
}
