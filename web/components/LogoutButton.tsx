"use client";

export function LogoutButton() {
  return (
    <button
      className="link"
      onClick={async () => {
        await fetch("/api/session/logout", { method: "POST" });
        window.location.href = "/";
      }}
    >
      Sign out
    </button>
  );
}
