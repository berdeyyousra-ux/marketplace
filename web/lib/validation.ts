"use client";

import { useState } from "react";

// Client checks that mirror the API's validators (section 11.1), so most mistakes show on blur rather than after a
// round trip. The API stays the authority: its field errors are shown in the same place.
export const rules = {
  email: (value: string) => (!value.trim() ? "Enter your email address." : !/^[^\s@]+@[^\s@]+$/.test(value.trim()) ? "Enter a valid email address, like name@example.com." : null),
  name: (value: string) => (value.trim().length < 2 ? "Enter at least 2 characters." : value.length > 80 ? "Use at most 80 characters." : null),
  newPassword: (value: string) => (value.length < 10 ? "Use at least 10 characters." : value.length > 128 ? "Use at most 128 characters." : null),
  required: (message: string) => (value: string) => (value ? null : message),
};

export const passwordHint = "10 to 128 characters. Any characters; very common passwords are refused.";

type Validators<K extends string> = Record<K, (value: string) => string | null>;

// Field values, errors shown after a field is left (blur) or on submit, and server errors merged in by field name.
export function useForm<K extends string>(validators: Validators<K>) {
  const keys = Object.keys(validators) as K[];
  const [values, setValues] = useState(() => Object.fromEntries(keys.map((key) => [key, ""])) as Record<K, string>);
  const [errors, setErrors] = useState<Partial<Record<K, string>>>({});

  function check(key: K, value: string) {
    const message = validators[key](value);
    setErrors((current) => ({ ...current, [key]: message ?? undefined }));
    return message;
  }

  function field(key: K) {
    return {
      name: key,
      value: values[key],
      error: errors[key],
      onChange: (event: React.ChangeEvent<HTMLInputElement>) => {
        const value = event.target.value;
        setValues((current) => ({ ...current, [key]: value }));
        // Once shown, an error clears as soon as the value is fixed.
        if (errors[key]) check(key, value);
      },
      onBlur: () => {
        if (values[key]) check(key, values[key]);
      },
    };
  }

  // Validates every field; returns true when the form can be sent.
  function validateAll() {
    return keys.map((key) => check(key, values[key])).every((message) => message === null);
  }

  function setServerErrors(serverErrors: Record<string, string>) {
    setErrors((current) => ({ ...current, ...serverErrors }));
  }

  return { values, field, validateAll, setServerErrors };
}
