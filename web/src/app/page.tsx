import Link from "next/link";

export default function Home() {
  return (
    <main className="mx-auto flex w-full max-w-xl flex-1 flex-col justify-center gap-4 px-6 py-16">
      <h1 className="text-3xl font-semibold tracking-tight">AffiVideo</h1>
      <p className="text-zinc-600">
        Short vertical product videos from a photo and a few Confirmed Facts.
      </p>
      <Link href="/status" className="font-medium underline underline-offset-4">
        System status
      </Link>
    </main>
  );
}
