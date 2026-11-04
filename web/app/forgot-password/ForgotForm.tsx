"use client";

import Link from "next/link";
import { useState } from "react";
import { rules, useForm } from "@/lib/validation";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Field";
import { FormAlert } from "@/components/FormAlert";
import { StatusPanel } from "@/components/AuthShell";

export function ForgotForm() {
  const form = useForm({ email: rules.email });
  const [sentTo, setSentTo] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    if (!form.validateAll()) return;

    setBusy(true);
    const email = form.values.email.trim();
    const response = await fetch("/api/session/forgot-password", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email }),
    });
    if (response.ok) setSentTo(email);
    else setError(response.status === 429 ? "Too many attempts from here. Wait a minute and try again." : "The request could not be sent. Try again.");
    setBusy(false);
  }

  // EP-06 answers 202 for every address, so the page must not reveal whether this one has an account.
  if (sentTo) {
    return (
      <StatusPanel tone="success">
        <p className="m-0 font-medium">Check your email</p>
        <p className="m-0">If <strong>{sentTo}</strong> has an account, we sent it a link to set a new password. The link works for 30 minutes.</p>
        <p className="m-0 type-label"><Link href="/login" className="text-accent-strong hover:underline">Back to sign in</Link></p>
      </StatusPanel>
    );
  }

  return (
    <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
      {error && <FormAlert>{error}</FormAlert>}
      <Input label="Email" type="email" autoComplete="email" {...form.field("email")} />
      <Button type="submit" loading={busy} fullWidth>Send reset link</Button>
      <p className="m-0 text-center type-label text-muted">
        Remembered it? <Link href="/login" className="text-accent-strong hover:underline">Sign in</Link>
      </p>
    </form>
  );
}
