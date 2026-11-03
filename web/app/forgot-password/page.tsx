import { AuthShell } from "@/components/AuthShell";
import { ForgotForm } from "./ForgotForm";

export default function ForgotPasswordPage() {
  return (
    <AuthShell title="Reset your password" description="Enter the email you signed up with and we'll send a link to set a new password.">
      <ForgotForm />
    </AuthShell>
  );
}
