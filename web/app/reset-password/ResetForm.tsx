"use client";

import Link from "next/link";
import { useState } from "react";
import { fieldErrors } from "@/lib/client";
import { passwordHint, rules, useForm } from "@/lib/validation";
import type { Problem } from "@/lib/types";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Field";
import { FormAlert } from "@/components/FormAlert";
import { StatusPanel } from "@/components/AuthShell";

function LinkFailed() {
  return (
    <StatusPanel tone="danger">
      <p className="m-0 font-medium">This link can&apos;t be used</p>
      <p className="m-0">It may have expired (reset links last 30 minutes), been used already, or been copied incompletely.</p>
      <p className="m-0 type-label"><Link href="/forgot-password" className="text-accent-strong hover:underline">Request a new link</Link></p>
    </StatusPanel>
  );
}

export function ResetForm({ token }: { token: string | null }) {
  const [confirmError, setConfirmError] = useState<string | undefined>();
  const form = useForm({ newPassword: rules.newPassword, confirm: rules.required("Enter the password again.") });
  const [linkFailed, setLinkFailed] = useState(!token);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (linkFailed) return <LinkFailed />;

  function checkMatch() {
    const mismatch = form.values.confirm && form.values.confirm !== form.values.newPassword ? "The passwords don't match." : undefined;
    setConfirmError(mismatch);
    return !mismatch;
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    if (!form.validateAll() || !checkMatch()) return;

    setBusy(true);
    const response = await fetch("/api/session/reset-password", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ token, newPassword: form.values.newPassword }),
    });
    if (response.ok) {
      window.location.replace("/login?reset=1");
      return;
    }

    const problem: Problem = await response.json().catch(() => ({}));
    if (problem.code === "invalid_token") setLinkFailed(true);
    else if (problem.errors) form.setServerErrors(fieldErrors(problem));
    else setError(response.status === 429 ? "Too many attempts from here. Wait a minute and try again." : "The password could not be changed. Try again.");
    setBusy(false);
  }

  const confirm = form.field("confirm");
  return (
    <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
      {error && <FormAlert>{error}</FormAlert>}
      <Input label="New password" type="password" autoComplete="new-password" maxLength={128} hint={passwordHint} {...form.field("newPassword")} />
      <Input
        label="Confirm new password"
        type="password"
        autoComplete="new-password"
        maxLength={128}
        {...confirm}
        onChange={(event) => { confirm.onChange(event); setConfirmError(undefined); }}
        error={confirm.error ?? confirmError}
        onBlur={() => { confirm.onBlur(); checkMatch(); }}
      />
      <Button type="submit" loading={busy} fullWidth>Set new password</Button>
      <p className="m-0 type-label text-muted">Every device signed in to this account will be signed out.</p>
    </form>
  );
}
