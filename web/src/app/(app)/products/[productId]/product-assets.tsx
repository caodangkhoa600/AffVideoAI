"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { api, fieldErrors, type ProductAsset, type ProductAssetKind } from "@/lib/api/client";

// The API's limits (ProductAsset in the domain). The size is checked here too, so
// that a file far too large is refused before it is sent rather than cut off on the way.
const MAX_UPLOAD_BYTES = 20 * 1024 * 1024;
const LIMITS = "JPEG, PNG or WebP, up to 20 MB and 6000 pixels on each side.";

type Refusal = { file: string; reason: string };

const assetsKey = (productId: string) => ["products", "one", productId, "assets"];

/** The Product's photos and logo. */
export function useProductAssets(productId: string) {
  return useQuery({
    queryKey: assetsKey(productId),
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/products/{productId}/assets", {
        params: { path: { productId } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
}

/** The Product's photos and logo: what is there, adding to it, and removing from it. */
export function ProductAssets({ productId }: { productId: string }) {
  const queryClient = useQueryClient();
  const queryKey = assetsKey(productId);
  const assets = useProductAssets(productId);
  const [uploading, setUploading] = useState<ProductAssetKind | null>(null);
  const [refusals, setRefusals] = useState<Refusal[]>([]);

  // One at a time, so that each file gets its own answer and the API decodes one image at once.
  const upload = async (kind: ProductAssetKind, files: File[]) => {
    setUploading(kind);
    setRefusals([]);
    const refused: Refusal[] = [];
    try {
      for (const file of files) {
        const reason = await send(productId, kind, file);
        if (reason) refused.push({ file: file.name, reason });
      }
      await queryClient.invalidateQueries({ queryKey });
    } finally {
      setRefusals(refused);
      setUploading(null);
    }
  };

  const logo = assets.data?.find((asset) => asset.kind === "Logo");
  const photos = assets.data?.filter((asset) => asset.kind === "Photo") ?? [];
  return (
    <section className="flex flex-col gap-6" data-testid="product-assets">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">Photos and logo</h2>
        <p className="text-sm text-muted-foreground">{LIMITS} Each one is stored as a PNG.</p>
      </div>
      {refusals.length > 0 && (
        <ul role="alert" className="flex flex-col gap-1 text-sm text-destructive" data-testid="upload-refusals">
          {refusals.map((refusal, index) => (
            <li key={index}>
              {refusal.file}: {refusal.reason}
            </li>
          ))}
        </ul>
      )}
      {assets.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The photos and logo could not be loaded.
        </p>
      ) : !assets.data ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <>
          <div className="flex flex-col gap-3">
            <div className="flex items-center justify-between gap-4">
              <h3 className="font-medium">Photos</h3>
              <Choose
                label="Add photos"
                busyLabel="Uploading…"
                multiple
                busy={uploading === "Photo"}
                disabled={uploading !== null}
                onChosen={(files) => upload("Photo", files)}
              />
            </div>
            {photos.length === 0 ? (
              <p className="text-sm text-muted-foreground">No photos yet.</p>
            ) : (
              <ul className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4" data-testid="product-photos">
                {photos.map((photo) => (
                  <Tile key={photo.id} asset={photo} queryKey={queryKey} />
                ))}
              </ul>
            )}
          </div>
          <div className="flex flex-col gap-3">
            <div className="flex items-center justify-between gap-4">
              <h3 className="font-medium">Logo</h3>
              <Choose
                label={logo ? "Replace logo" : "Upload logo"}
                busyLabel="Uploading…"
                busy={uploading === "Logo"}
                disabled={uploading !== null}
                onChosen={(files) => upload("Logo", files)}
              />
            </div>
            {logo ? (
              <ul className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4" data-testid="product-logo">
                <Tile asset={logo} queryKey={queryKey} />
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground">No logo yet.</p>
            )}
          </div>
        </>
      )}
    </section>
  );
}

/** Sends one file. Answers the reason it was refused, or nothing when it was kept. */
async function send(productId: string, kind: ProductAssetKind, file: File): Promise<string | undefined> {
  if (file.size > MAX_UPLOAD_BYTES) return "The file is larger than 20 MB.";
  const { error, response } = await api
    .POST("/api/v1/products/{productId}/assets", {
      params: { path: { productId } },
      // The description calls a file a string; what is sent is the form below.
      body: { kind, file: file.name },
      bodySerializer: () => {
        const form = new FormData();
        form.set("kind", kind);
        form.set("file", file);
        return form;
      },
    })
    .catch(() => ({ error: undefined, response: undefined }));
  if (response?.ok) return undefined;
  return fieldErrors(error).file?.join(" ") ?? "The file could not be uploaded.";
}

// A button that opens the file picker. The input is emptied after each choice, so
// that choosing the same file again (after fixing it) is still a change.
function Choose({
  label,
  busyLabel,
  busy,
  disabled,
  multiple,
  onChosen,
}: {
  label: string;
  busyLabel: string;
  busy: boolean;
  disabled: boolean;
  multiple?: boolean;
  onChosen: (files: File[]) => void;
}) {
  const input = useRef<HTMLInputElement>(null);
  return (
    <>
      <input
        ref={input}
        type="file"
        accept="image/jpeg,image/png,image/webp"
        multiple={multiple}
        className="sr-only"
        aria-label={label}
        tabIndex={-1}
        onChange={(event) => {
          const files = Array.from(event.target.files ?? []);
          event.target.value = "";
          if (files.length > 0) onChosen(files);
        }}
      />
      <Button variant="outline" disabled={disabled} onClick={() => input.current?.click()}>
        {busy ? busyLabel : label}
      </Button>
    </>
  );
}

function Tile({ asset, queryKey }: { asset: ProductAsset; queryKey: string[] }) {
  const queryClient = useQueryClient();
  const [asking, setAsking] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [failed, setFailed] = useState(false);

  const remove = async () => {
    setRemoving(true);
    setFailed(false);
    const { response } = await api
      .DELETE("/api/v1/products/{productId}/assets/{assetId}", {
        params: { path: { productId: asset.productId, assetId: asset.id } },
      })
      .catch(() => ({ response: undefined }));
    // 404 is an asset someone has already removed: gone, which is what was asked for.
    if (response?.ok || response?.status === 404) {
      await queryClient.invalidateQueries({ queryKey });
      return;
    }
    setFailed(true);
    setRemoving(false);
    setAsking(false);
  };

  return (
    <li className="flex flex-col gap-2" data-testid="product-asset">
      {/* Not next/image: the file comes from the API, which only serves it to a signed-in
          member of the Organization, and the image optimiser would ask without the session. */}
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src={`/api/v1/products/${asset.productId}/assets/${asset.id}/content`}
        alt={asset.kind}
        width={asset.width}
        height={asset.height}
        loading="lazy"
        className="aspect-square w-full rounded-lg border bg-muted object-contain"
      />
      <div className="flex min-h-8 flex-wrap items-center justify-between gap-2">
        <span className="text-sm text-muted-foreground">
          {asset.width} × {asset.height}
        </span>
        {asking ? (
          <span className="flex gap-2">
            <Button variant="destructive" size="sm" onClick={remove} disabled={removing}>
              {removing ? "Removing…" : "Yes, remove"}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setAsking(false)} disabled={removing}>
              Cancel
            </Button>
          </span>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setAsking(true)}>
            Remove
          </Button>
        )}
      </div>
      {failed && (
        <p role="alert" className="text-sm text-destructive">
          The {asset.kind.toLowerCase()} could not be removed.
        </p>
      )}
    </li>
  );
}
