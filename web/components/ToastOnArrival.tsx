"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useRef } from "react";
import { useToast } from "@/components/ui/Toast";

const messages: Record<string, string> = {
  paid: "Payment received. Your purchase is ready to download.",
};

// Shows the toast a previous screen asked for with ?toast=, then drops the parameter so a reload doesn't repeat it.
export function ToastOnArrival() {
  const toast = useToast();
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const shown = useRef(false);
  const key = params.get("toast");

  useEffect(() => {
    if (!key || shown.current || !messages[key]) return;
    shown.current = true;
    toast("success", messages[key]);
    router.replace(pathname, { scroll: false });
  }, [key, pathname, router, toast]);

  return null;
}
