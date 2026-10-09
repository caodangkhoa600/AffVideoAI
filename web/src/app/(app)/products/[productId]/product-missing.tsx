import Link from "next/link";

/** Shown in place of a Product that is not there, or that could not be loaded. */
export function ProductMissing({ failed }: { failed: boolean }) {
  return (
    <>
      <h1 className="text-3xl font-semibold tracking-tight">Product</h1>
      <p role="alert" className="text-sm text-destructive">
        {failed ? "The Product could not be loaded." : "There is no such Product in your Organization."}
      </p>
      <Link href="/products" className="font-medium underline underline-offset-4">
        All Products
      </Link>
    </>
  );
}
