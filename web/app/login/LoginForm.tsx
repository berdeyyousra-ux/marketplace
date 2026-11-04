"use client";

import Link from "next/link";
import { useState } from "react";
import { describe, fieldErrors } from "@/lib/client";
import { rules, useForm } from "@/lib/validation";
import type { Problem } from "@/lib/types";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Field";
import { FormAlert } from "@/components/FormAlert";

// The API's login failures in words a person can act on (section 11.2: one answer for unknown email or wrong password).
function loginMessage(status: number, problem: Problem) {
  if (status === 401) return "That email and password don't match an account.";
  if (status === 403) return "Confirm your email first: open the link we sent when you registered.";
  if (status === 423) return "Too many failed attempts. The account is locked for 15 minutes; try again then, or reset your password.";
  if (status === 429) return "Too many attempts from here. Wait a minute and try again.";
  return describe(problem);
}

export function LoginForm({ next }: { next: string }) {
  const form = useForm({ email: rules.email, password: rules.required("Enter your password.") });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    if (!form.validateAll()) return;

    setBusy(true);
    const response = await fetch("/api/session/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: form.values.email.trim(), password: form.values.password }),
    });
    if (response.ok) {
      window.location.href = next;
      return;
    }

    const problem: Problem = await response.json().catch(() => ({}));
    if (response.status === 400 && problem.errors) {
      form.setServerErrors(fieldErrors(problem));
    } else {
      setError(loginMessage(response.status, problem));
    }

    setBusy(false);
  }

  return (
    <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
      {error && <FormAlert>{error}</FormAlert>}
      <Input label="Email" type="email" autoComplete="email" {...form.field("email")} />
      <div className="flex flex-col gap-1">
        <Input label="Password" type="password" autoComplete="current-password" {...form.field("password")} />
        <Link href="/forgot-password" className="self-end type-label text-accent-strong hover:underline">Forgot your password?</Link>
      </div>
      <Button type="submit" loading={busy} fullWidth>Sign in</Button>
      <p className="m-0 text-center type-label text-muted">
        New here? <Link href="/register" className="text-accent-strong hover:underline">Create an account</Link>
      </p>
    </form>
  );
}
