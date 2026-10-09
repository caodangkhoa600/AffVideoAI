"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Field } from "@/components/field";
import { Button } from "@/components/ui/button";
import { api, fieldErrors } from "@/lib/api/client";
import { useSession } from "@/lib/session";

// The most the API serves in one page. An Organization this month has a handful of members.
const PAGE_SIZE = 200;

export default function MembersPage() {
  const session = useSession();
  const organizationId = session.data?.organization.id;

  const members = useQuery({
    queryKey: ["members", organizationId],
    enabled: organizationId !== undefined,
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/organizations/{organizationId}/members", {
        params: { path: { organizationId: organizationId! }, query: { pageSize: PAGE_SIZE } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });

  return (
    <>
      <h1 className="text-3xl font-semibold tracking-tight">Members</h1>
      {members.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The members could not be loaded.
        </p>
      ) : (
        <ul className="divide-y rounded-lg border" data-testid="members">
          {members.data?.items.map((member) => (
            <li key={member.id} className="flex items-center justify-between px-4 py-3">
              <span className="font-medium">{member.email}</span>
              <span className="text-sm text-muted-foreground">{member.role}</span>
            </li>
          )) ?? <li className="px-4 py-3 text-sm text-muted-foreground">Loading…</li>}
        </ul>
      )}
      {members.data && members.data.total > members.data.items.length && (
        <p className="text-sm text-muted-foreground">
          Showing the first {members.data.items.length} of {members.data.total}.
        </p>
      )}
      {session.data &&
        (session.data.member.role === "Owner" ? (
          <AddEditor organizationId={session.data.organization.id} />
        ) : (
          <p className="text-sm text-muted-foreground">Only an Owner can add members.</p>
        ))}
    </>
  );
}

const schema = z.object({
  email: z.email("Enter the Editor's email address."),
  password: z.string().min(12, "Use at least 12 characters."),
});

function AddEditor({ organizationId }: { organizationId: string }) {
  const queryClient = useQueryClient();
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { email: "", password: "" },
  });

  const add = form.handleSubmit(async (values) => {
    const { error, response } = await api.POST("/api/v1/organizations/{organizationId}/members", {
      params: { path: { organizationId } },
      body: values,
    });
    if (response.ok) {
      form.reset();
      await queryClient.invalidateQueries({ queryKey: ["members", organizationId] });
      return;
    }
    const refused = fieldErrors(error);
    if (refused.email) form.setError("email", { message: refused.email.join(" ") });
    if (refused.password) form.setError("password", { message: refused.password.join(" ") });
    if (!refused.email && !refused.password) {
      form.setError("root", { message: "The Editor could not be added." });
    }
  });

  const { errors, isSubmitting, isSubmitSuccessful } = form.formState;
  return (
    <section className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">Add an Editor</h2>
        <p className="text-sm text-muted-foreground">
          An Editor does all creative work but cannot manage members or settings. Nothing is emailed:
          give them the password yourself.
        </p>
      </div>
      <form onSubmit={add} noValidate className="flex max-w-sm flex-col gap-4">
        <Field
          id="email"
          label="Email"
          type="email"
          autoComplete="off"
          error={errors.email?.message}
          {...form.register("email")}
        />
        <Field
          id="password"
          label="Password"
          type="password"
          autoComplete="new-password"
          hint="At least 12 characters."
          error={errors.password?.message}
          {...form.register("password")}
        />
        {errors.root && (
          <p role="alert" className="text-sm text-destructive">
            {errors.root.message}
          </p>
        )}
        <Button type="submit" disabled={isSubmitting} className="self-start">
          {isSubmitting ? "Adding…" : "Add Editor"}
        </Button>
        {isSubmitSuccessful && !isSubmitting && (
          <p role="status" className="text-sm text-muted-foreground">
            Editor added.
          </p>
        )}
      </form>
    </section>
  );
}
