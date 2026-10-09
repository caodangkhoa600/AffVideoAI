import Link from "next/link";

export default function Home() {
  return (
    <>
      <h1 className="text-3xl font-semibold tracking-tight">AffiVideo</h1>
      <p className="text-muted-foreground">
        Short vertical product videos from a photo and a few Confirmed Facts.
      </p>
      <ul className="flex flex-col gap-2">
        <li>
          <Link href="/members" className="font-medium underline underline-offset-4">
            Members
          </Link>
        </li>
        <li>
          <Link href="/settings" className="font-medium underline underline-offset-4">
            Organization settings
          </Link>
        </li>
        <li>
          <Link href="/status" className="font-medium underline underline-offset-4">
            System status
          </Link>
        </li>
      </ul>
    </>
  );
}
