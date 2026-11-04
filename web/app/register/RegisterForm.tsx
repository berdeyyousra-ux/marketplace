"use client";

import Link from "next/link";
import { useState } from "react";
import { describe, fieldErrors } from "@/lib/client";
import { passwordHint, rules, useForm } from "@/lib/validation";
import type { Problem } from "@/lib/types";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Field";
import { FormAlert } from "@/components/FormAlert";
import { StatusPanel } from "@/components/AuthShell";

export function RegisterForm() {
  const form = useForm({ displayName: rules.name, email: rules.email, password: rules.newPassword });
  const [error, setError] = useState<string | null>(null);
  const [sentTo, setSentTo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    if (!form.validateAll()) return;

    setBusy(true);
    const email = form.values.email.trim();
    const response = await fetch("/api/session/register", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password: form.values.password, displayName: form.values.displayName.trim() }),
    });
    if (response.ok) {
      setSentTo(email);
    } else {
      const problem: Problem = await response.json().catch(() => ({}));
      if (problem.errors) form.setServerErrors(fieldErrors(problem));
      else setError(response.status === 429 ? "Too many attempts from here. Wait a minute and try again." : describe(problem));
    }

    setBusy(false);
  }

  // The API answers the same way for new and existing emails (US-01), so this message must not say which it was.
  if (sentTo) {
    return (
      <StatusPanel tone="success">
        <p className="m-0 font-medium">Check your email</p>
        <p className="m-0">We sent a message to <strong>{sentTo}</strong>. Open the link in it to confirm your address, then sign in. The link works for 24 hours.</p>
        <p className="m-0 type-label text-muted">No email after a few minutes? Check the spam folder, or register again to get a new link.</p>
      </StatusPanel>
    );
  }

  return (
    <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
      {error && <FormAlert>{error}</FormAlert>}
      <Input label="Name" autoComplete="name" maxLength={80} hint="Shown on your orders and, if you sell, on your listings." {...form.field("displayName")} />
      <Input label="Email" type="email" autoComplete="email" {...form.field("email")} />
      <Input label="Password" type="password" autoComplete="new-password" maxLength={128} hint={passwordHint} {...form.field("password")} />
      <Button type="submit" loading={busy} fullWidth>Create account</Button>
      <p className="m-0 text-center type-label text-muted">
        Already have an account? <Link href="/login" className="text-accent-strong hover:underline">Sign in</Link>
      </p>
    </form>
  );
}
