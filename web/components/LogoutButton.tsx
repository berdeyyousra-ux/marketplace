"use client";

export function LogoutButton() {
  return (
    <button
      type="button"
      className="cursor-pointer border-0 bg-transparent p-0 text-left type-label text-text hover:text-accent"
      onClick={async () => {
        await fetch("/api/session/logout", { method: "POST" });
        window.location.href = "/";
      }}
    >
      Sign out
    </button>
  );
}
