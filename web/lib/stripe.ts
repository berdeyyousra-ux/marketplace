"use client";

import { loadStripe, type Stripe } from "@stripe/stripe-js";
import type { Appearance } from "@stripe/stripe-js";

// T-064. Stripe.js is loaded once per publishable key: loadStripe injects a script tag, and calling it on every
// render would add one each time. The key arrives from the API at run time (EP-33), not from a NEXT_PUBLIC_ build
// argument, because the web image is built once and deployed to any environment.
const loaders = new Map<string, Promise<Stripe | null>>();

export function stripeFor(publishableKey: string) {
  let loader = loaders.get(publishableKey);
  if (!loader) {
    loader = loadStripe(publishableKey);
    loaders.set(publishableKey, loader);
  }

  return loader;
}

// Elements render in an iframe, so the design tokens can't reach them as CSS variables; their resolved values are
// read off the document and handed over. This follows whichever theme is active (dark by default, light by
// preference or a data-theme override), and falls back to Stripe's own theme if a token is missing.
export function stripeAppearance(): Appearance {
  const styles = getComputedStyle(document.documentElement);
  const token = (name: string) => styles.getPropertyValue(name).trim();
  const surface = token("--surface");
  const text = token("--text");

  // The page background is near-black in dark mode; its own surface colour tells the two themes apart.
  const dark = isDark(token("--bg"));
  return {
    theme: dark ? "night" : "stripe",
    variables: {
      fontFamily: token("--font-sans") || undefined,
      fontSizeBase: "15px",
      borderRadius: token("--radius-control") || undefined,
      colorPrimary: token("--accent-strong") || undefined,
      colorBackground: surface || undefined,
      colorText: text || undefined,
      colorTextSecondary: token("--text-muted") || undefined,
      colorTextPlaceholder: token("--text-muted") || undefined,
      colorDanger: token("--danger") || undefined,
      colorSuccess: token("--success") || undefined,
      colorWarning: token("--warning") || undefined,
      accessibleColorOnColorPrimary: token("--accent-contrast") || undefined,
    },
    rules: {
      ".Input": { borderColor: token("--border-strong") || "", boxShadow: "none" },
      ".Input:focus": { borderColor: token("--accent") || "", boxShadow: `0 0 0 2px ${token("--accent")}` },
      ".Tab": { borderColor: token("--border") || "" },
      ".Tab--selected": { borderColor: token("--accent") || "" },
    },
  };
}

// True for a dark colour. Hex is what the tokens use; anything else (a named or oklch colour) counts as light,
// which only picks Stripe's lighter base theme and never changes the colours set above.
function isDark(color: string) {
  const hex = color.replace("#", "");
  if (hex.length !== 3 && hex.length !== 6) {
    return false;
  }

  const full = hex.length === 3 ? [...hex].map((digit) => digit + digit).join("") : hex;
  const [red, green, blue] = [0, 2, 4].map((at) => Number.parseInt(full.slice(at, at + 2), 16));
  if ([red, green, blue].some(Number.isNaN)) {
    return false;
  }

  // Rec. 601 luma, the usual quick brightness test.
  return (0.299 * red + 0.587 * green + 0.114 * blue) / 255 < 0.5;
}
