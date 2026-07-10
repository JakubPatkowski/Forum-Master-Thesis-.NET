import { PageShell } from "@/components/layout/PageShell";
import { NotFoundState } from "@/components/ui/ErrorState";

export default function NotFound() {
  return (
    <PageShell wide={false}>
      <NotFoundState
        title="Page not found"
        description="The address doesn't match anything here — it may have moved or never existed."
        detail="errorType: NotFound"
      />
    </PageShell>
  );
}
