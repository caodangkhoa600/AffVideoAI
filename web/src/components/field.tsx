import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";

type FieldProps = { id: string; label: string; error?: string; hint?: string };

/** A labelled input with the reason it was refused underneath. */
export function Field({ label, error, hint, ...input }: React.ComponentProps<"input"> & FieldProps) {
  return (
    <Labelled id={input.id} label={label} error={error} hint={hint}>
      <Input aria-invalid={error ? true : undefined} aria-describedby={describedBy(input.id, error, hint)} {...input} />
    </Labelled>
  );
}

/** The same, for text that runs to several lines. */
export function TextAreaField({ label, error, hint, ...textarea }: React.ComponentProps<"textarea"> & FieldProps) {
  return (
    <Labelled id={textarea.id} label={label} error={error} hint={hint}>
      <Textarea
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(textarea.id, error, hint)}
        {...textarea}
      />
    </Labelled>
  );
}

function describedBy(id: string, error?: string, hint?: string) {
  return error ? `${id}-error` : hint ? `${id}-hint` : undefined;
}

function Labelled({ id, label, error, hint, children }: FieldProps & { children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label}</Label>
      {children}
      {error ? (
        <p id={`${id}-error`} role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : hint ? (
        <p id={`${id}-hint`} className="text-sm text-muted-foreground">
          {hint}
        </p>
      ) : null}
    </div>
  );
}
