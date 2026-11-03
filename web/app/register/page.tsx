import { AuthShell } from "@/components/AuthShell";
import { RegisterForm } from "./RegisterForm";

export default function RegisterPage() {
  return (
    <AuthShell title="Create an account" description="Buy digital products, or open a shop and sell your own.">
      <RegisterForm />
    </AuthShell>
  );
}
