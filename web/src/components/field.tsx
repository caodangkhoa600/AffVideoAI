import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

/** A labelled input with the reason it was refused underneath. */
export function Field({
  label,
  error,
  hint,
  ...input
}: React.ComponentProps<"input"> & { id: string; label: string; error?: string; hint?: string }) {
  const describedBy = error ? `${input.id}-error` : hint ? `${input.id}-hint` : undefined;
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={input.id}>{label}</Label>
      <Input aria-invalid={error ? true : undefined} aria-describedby={describedBy} {...input} />
      {error ? (
        <p id={`${input.id}-error`} role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : hint ? (
        <p id={`${input.id}-hint`} className="text-sm text-muted-foreground">
          {hint}
        </p>
      ) : null}
    </div>
  );
}
