"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Field } from "@/components/field";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api/client";

const schema = z.object({
  email: z.email("Enter your email address."),
  password: z.string().min(1, "Enter your password."),
});

// Where the member was going when they were sent here. Only a path on this
// site is accepted, so a crafted link cannot send them somewhere else.
function destination() {
  const next = new URLSearchParams(window.location.search).get("next");
  if (!next) return "/";
  // Resolved the way the browser will resolve it, then compared: "//host",
  // "/\host" and a path with a tab or newline in it all name another site.
  const url = new URL(next, window.location.origin);
  return url.origin === window.location.origin ? url.pathname + url.search + url.hash : "/";
}

export default function SignInPage() {
  const queryClient = useQueryClient();
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { email: "", password: "" },
  });

  const signIn = form.handleSubmit(async (values) => {
    const { response } = await api.POST("/api/v1/session", { body: values }).catch(() => ({ response: undefined }));
    if (!response?.ok) {
      form.setError("root", {
        message:
          response?.status === 401
            ? "The email or password is not correct."
            : "Could not sign in. Check that the API is running and try again.",
      });
      return;
    }
    queryClient.clear();
    // A full page load, so the page is requested again with the new cookie.
    window.location.assign(destination());
  });

  const { errors, isSubmitting } = form.formState;
  return (
    <main className="mx-auto flex w-full max-w-sm flex-1 flex-col justify-center gap-6 px-6 py-16">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">Sign in to AffiVideo</h1>
        <p className="text-sm text-muted-foreground">
          Members are added by the Owner of their Organization.
        </p>
      </div>
      <form onSubmit={signIn} noValidate className="flex flex-col gap-4">
        <Field
          id="email"
          label="Email"
          type="email"
          autoComplete="username"
          error={errors.email?.message}
          {...form.register("email")}
        />
        <Field
          id="password"
          label="Password"
          type="password"
          autoComplete="current-password"
          error={errors.password?.message}
          {...form.register("password")}
        />
        {errors.root && (
          <p role="alert" className="text-sm text-destructive">
            {errors.root.message}
          </p>
        )}
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? "Signing in…" : "Sign in"}
        </Button>
      </form>
    </main>
  );
}
