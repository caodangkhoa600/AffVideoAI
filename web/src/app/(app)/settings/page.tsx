"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Field } from "@/components/field";
import { Button } from "@/components/ui/button";
import { api, fieldErrors, type Session } from "@/lib/api/client";
import { useSession } from "@/lib/session";

export default function SettingsPage() {
  const session = useSession();
  return (
    <>
      <h1 className="text-3xl font-semibold tracking-tight">Organization settings</h1>
      {session.data &&
        (session.data.member.role === "Owner" ? (
          // Keyed so the form starts again from the saved name.
          <RenameOrganization key={session.data.organization.name} organization={session.data.organization} />
        ) : (
          <dl className="flex flex-col gap-1">
            <dt className="text-sm text-muted-foreground">Name</dt>
            <dd className="font-medium">{session.data.organization.name}</dd>
            <dd className="text-sm text-muted-foreground">Only an Owner can change settings.</dd>
          </dl>
        ))}
    </>
  );
}

const schema = z.object({
  name: z.string().trim().min(1, "Enter a name.").max(100, "Use at most 100 characters."),
});

function RenameOrganization({ organization }: { organization: Session["organization"] }) {
  const queryClient = useQueryClient();
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { name: organization.name },
  });

  const save = form.handleSubmit(async (values) => {
    const { error, response } = await api.PUT("/api/v1/organizations/{organizationId}", {
      params: { path: { organizationId: organization.id } },
      body: values,
    });
    if (!response.ok) {
      form.setError("name", { message: fieldErrors(error).name?.join(" ") ?? "The name could not be saved." });
      return;
    }
    await queryClient.invalidateQueries({ queryKey: ["session"] });
  });

  const { errors, isSubmitting, isDirty } = form.formState;
  return (
    <form onSubmit={save} noValidate className="flex max-w-sm flex-col gap-4">
      <Field id="name" label="Name" error={errors.name?.message} {...form.register("name")} />
      <Button type="submit" disabled={isSubmitting || !isDirty} className="self-start">
        {isSubmitting ? "Saving…" : "Save"}
      </Button>
    </form>
  );
}
