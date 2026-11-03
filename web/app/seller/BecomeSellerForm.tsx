"use client";

import { useState } from "react";
import { call, describe, fieldErrors } from "@/lib/client";
import { rules, useForm } from "@/lib/validation";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Field";
import { FormAlert } from "@/components/FormAlert";

// EP-27. The Seller role is in the next access token, so the session is refreshed before reloading.
export function BecomeSellerForm({ defaultName }: { defaultName: string }) {
  const form = useForm({
    displayName: rules.name,
    country: (value: string) => (/^[A-Za-z]{2}$/.test(value.trim()) ? null : "Enter the two-letter country code, like ES or IE."),
  }, { displayName: defaultName });
  const [bio, setBio] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    if (!form.validateAll()) return;

    setBusy(true);
    const result = await call("/sellers", {
      method: "POST",
      body: { displayName: form.values.displayName.trim(), bio: bio.trim() || null, country: form.values.country.trim().toUpperCase() },
    });
    if (result.ok) {
      await fetch("/api/session/refresh", { method: "POST" });
      window.location.reload();
      return;
    }

    const errors = fieldErrors(result.problem);
    if (Object.keys(errors).length > 0) form.setServerErrors(errors);
    else setError(describe(result.problem));
    setBusy(false);
  }

  return (
    <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
      {error && <FormAlert>{error}</FormAlert>}
      <Input label="Shop name" maxLength={80} hint="Shown on your listings. 2 to 80 characters." {...form.field("displayName")} />
      <Input label="Country" maxLength={2} autoComplete="country" placeholder="ES" hint="Two-letter code of the country you sell from." {...form.field("country")} />
      <div className="flex flex-col gap-1">
        <label htmlFor="bio" className="type-label text-text">About your shop <span className="text-muted">(optional)</span></label>
        <textarea id="bio" rows={3} maxLength={1000} value={bio} onChange={(event) => setBio(event.target.value)}
          className="w-full rounded-control border border-border-strong bg-surface px-3 py-2 type-body text-text" />
      </div>
      <Button type="submit" loading={busy} fullWidth>Open my shop</Button>
    </form>
  );
}
