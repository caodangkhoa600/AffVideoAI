import { AppHeader } from "./app-header";

// Pages for signed-in members. src/proxy.ts sends anyone else to /sign-in
// before these are served.
export default function AppLayout({ children }: LayoutProps<"/">) {
  return (
    <>
      <AppHeader />
      <main className="mx-auto flex w-full max-w-3xl flex-1 flex-col gap-6 px-6 py-10">{children}</main>
    </>
  );
}
