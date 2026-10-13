"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Field } from "@/components/field";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
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
          <>
            <RenameOrganization key={session.data.organization.name} organization={session.data.organization} />
            <DefaultVoice key={session.data.organization.defaultVoice ?? ""} organization={session.data.organization} />
          </>
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

/** The voice a Project is given when its brief names none: an Owner chooses it from the voices on offer. */
function DefaultVoice({ organization }: { organization: Session["organization"] }) {
  const queryClient = useQueryClient();
  const voices = useQuery({
    queryKey: ["voices"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/voices");
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
  const [chosen, setChosen] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<string>();
  if (!voices.data) return null;

  const current = voices.data.defaultVoice;
  const voice = chosen ?? current;
  const heard = voices.data.voices.find((offered) => offered.id === voice);
  const save = async () => {
    setSaving(true);
    setRefused(undefined);
    const { error, response } = await api
      .PUT("/api/v1/organizations/{organizationId}", {
        params: { path: { organizationId: organization.id } },
        body: { name: organization.name, defaultVoice: voice },
      })
      .catch(() => ({ error: undefined, response: undefined }));
    if (!response?.ok) setRefused(fieldErrors(error).defaultVoice?.join(" ") ?? "The voice could not be saved.");
    await queryClient.invalidateQueries({ queryKey: ["session"] });
    await queryClient.invalidateQueries({ queryKey: ["voices"] });
    setSaving(false);
  };

  return (
    <div className="flex max-w-sm flex-col gap-2">
      <Label htmlFor="defaultVoice">Voice</Label>
      <select
        id="defaultVoice"
        value={voice}
        onChange={(event) => setChosen(event.target.value)}
        className="h-8 w-full rounded-lg border border-input bg-transparent px-2.5 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 md:text-sm dark:bg-input/30"
      >
        {voices.data.voices.map((offered) => (
          <option key={offered.id} value={offered.id}>
            {offered.name}
          </option>
        ))}
      </select>
      <p className="text-sm text-muted-foreground">
        The voice narration text is spoken in for a new Project whose brief names none. Projects that exist keep theirs.
      </p>
      {heard?.hasSample && (
        // Keyed by the voice, so the player starts again with the sample of the one now chosen. Playing it costs nothing.
        <audio
          key={heard.id}
          src={`/api/v1/voices/${encodeURIComponent(heard.id)}/sample`}
          controls
          preload="none"
          aria-label={`A sample of the voice ${heard.name}`}
          className="w-full"
        />
      )}
      {refused && (
        <p role="alert" className="text-sm text-destructive">
          {refused}
        </p>
      )}
      <Button onClick={save} disabled={saving || voice === current} className="self-start">
        {saving ? "Saving…" : "Save voice"}
      </Button>
    </div>
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
