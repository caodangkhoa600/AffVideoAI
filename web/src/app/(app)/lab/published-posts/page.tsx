"use client";

import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Field } from "@/components/field";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  api,
  fieldErrors,
  problemDetail,
  type AffiliateLink,
  type PublishedPost,
  type SocialAccount,
  type SocialPlatform,
} from "@/lib/api/client";
import { creativeTemplateName } from "../../projects/creative-templates";
import { formatAmount } from "../commission/commission";
import { METRICS, formatMoment, formatTotal, sourceName } from "./performance";

const LOADING = <p className="text-sm text-muted-foreground">Loading…</p>;

// The most the API serves in one page; the count says so if there are more.
const PAGE_SIZE = 200;

// The API's limits (SocialAccount, AffiliateLink and Product in the domain).
const HANDLE_MAX_LENGTH = 100;
const LABEL_MAX_LENGTH = 200;
const URL_MAX_LENGTH = 2048;

/** The platforms a social account can be on (SocialPlatform in the domain). */
const PLATFORMS: SocialPlatform[] = ["TikTok", "Facebook", "Instagram", "YouTube", "Shopee"];

const SELECT =
  "h-8 w-full rounded-lg border border-input bg-transparent px-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30";

const accountName = (account: SocialAccount) => `${account.platform} · ${account.handle}`;
const linkName = (link: Pick<AffiliateLink, "url" | "label">) => link.label || link.url;

/** Today, as a date input writes it, where the member is. */
function today() {
  const now = new Date();
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
}

// What the list starts narrowed to is in the address, which is only known once the
// page is asked for, so it is drawn inside a Suspense boundary.
export default function PublishedPostsPage() {
  return (
    <>
      <div className="flex flex-col gap-1">
        <h1 className="text-3xl font-semibold tracking-tight">Published Posts</h1>
        <p className="text-sm text-muted-foreground">
          What is live where: each approved Rendered Video you posted, on which account and at which address. Posting
          is done by hand; this is the record of it.{" "}
          <Link href="/lab" className="font-medium underline underline-offset-4">
            Affiliate Lab
          </Link>
        </p>
      </div>
      <Suspense fallback={LOADING}>
        <PublishedPosts />
      </Suspense>
    </>
  );
}

function usePaged<T>(queryKey: unknown[], load: () => Promise<{ data?: T; response: Response }>) {
  return useQuery({
    queryKey,
    queryFn: async () => {
      const { data, response } = await load();
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
  });
}

function PublishedPosts() {
  const queryClient = useQueryClient();
  const reload = () => queryClient.invalidateQueries({ queryKey: ["lab", "publishing"] });

  const accounts = usePaged(["lab", "publishing", "accounts"], () =>
    api.GET("/api/v1/lab/social-accounts", { params: { query: { pageSize: PAGE_SIZE } } }),
  );
  const links = usePaged(["lab", "publishing", "links"], () =>
    api.GET("/api/v1/lab/affiliate-links", { params: { query: { pageSize: PAGE_SIZE } } }),
  );
  // Only an approved Rendered Video can be recorded as a Published Post.
  const videos = usePaged(["rendered-videos", "list", { state: "Approved", pageSize: PAGE_SIZE }], () =>
    api.GET("/api/v1/rendered-videos", { params: { query: { state: "Approved", pageSize: PAGE_SIZE } } }),
  );

  if (accounts.isError || links.isError || videos.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        The Published Posts could not be loaded.
      </p>
    );
  }
  if (!accounts.data || !links.data || !videos.data) return LOADING;

  // A Published Post is of a Rendered Video, so these are every Product and Variant one can be of.
  const products = new Map(videos.data.items.map((video) => [video.productId, video.productName]));
  const variants = new Map(videos.data.items.map((video) => [video.variantId, `${video.productName} · ${video.hook}`]));
  return (
    <>
      <section className="flex flex-col gap-4" data-testid="record-published-post">
        <h2 className="text-xl font-semibold tracking-tight">Record a Published Post</h2>
        {videos.data.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No Rendered Video is approved yet. Approve one in the{" "}
            <Link href="/videos" className="font-medium underline underline-offset-4">
              video library
            </Link>
            , post it, then record it here.
          </p>
        ) : (
          <RecordPost
            videos={videos.data.items.map((video) => ({
              id: video.id,
              name: `${video.productName} · ${video.hook} · rendered ${new Date(video.createdAt).toLocaleString()}`,
            }))}
            accounts={accounts.data.items}
            links={links.data.items}
            onRecorded={reload}
          />
        )}
        <div className="grid gap-4 md:grid-cols-2">
          <AddAccount onAdded={reload} />
          <AddLink onAdded={reload} />
        </div>
      </section>
      <PostList accounts={accounts.data.items} products={products} variants={variants} />
    </>
  );
}

function RecordPost({
  videos,
  accounts,
  links,
  onRecorded,
}: {
  videos: { id: string; name: string }[];
  accounts: SocialAccount[];
  links: AffiliateLink[];
  onRecorded: () => Promise<void>;
}) {
  const [renderedVideoId, setRenderedVideoId] = useState("");
  const [socialAccountId, setSocialAccountId] = useState("");
  const [publishedOn, setPublishedOn] = useState(today);
  const [url, setUrl] = useState("");
  const [affiliateLinkId, setAffiliateLinkId] = useState("");
  const [saving, setSaving] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [refused, setRefused] = useState<string>();
  const [recorded, setRecorded] = useState(false);

  const chosenLink = links.find((link) => link.id === affiliateLinkId);

  const record = async (event: React.FormEvent) => {
    event.preventDefault();
    setRecorded(false);
    const missing: Record<string, string> = {};
    if (!renderedVideoId) missing.renderedVideoId = "Choose the Rendered Video you posted.";
    if (!socialAccountId) missing.socialAccountId = "Choose the social account you posted it on.";
    if (!publishedOn) missing.publishedOn = "Enter the day you posted it.";
    if (url.trim() === "") missing.url = "Enter the address of the Published Post.";
    setErrors(missing);
    setRefused(undefined);
    if (Object.keys(missing).length > 0) return;

    setSaving(true);
    const { error, response } = await api
      .POST("/api/v1/lab/published-posts", {
        body: { renderedVideoId, socialAccountId, publishedOn, url, affiliateLinkId: affiliateLinkId || null },
      })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      // The video, account and day stay: the next post is often the same video on another account.
      setUrl("");
      setAffiliateLinkId("");
      setRecorded(true);
      await onRecorded();
    } else if (response?.status === 400) {
      const byField = Object.fromEntries(Object.entries(fieldErrors(error)).map(([field, said]) => [field, said.join(" ")]));
      setErrors(byField);
      if (Object.keys(byField).length === 0) setRefused("The Published Post could not be recorded.");
    } else {
      setRefused(problemDetail(error) ?? "The Published Post could not be recorded.");
    }
    setSaving(false);
  };

  return (
    <form onSubmit={record} noValidate className="flex max-w-xl flex-col gap-4">
      <Choice
        id="post-video"
        label="Approved Rendered Video"
        value={renderedVideoId}
        onChange={setRenderedVideoId}
        placeholder="Choose a Rendered Video"
        options={videos.map((video) => [video.id, video.name])}
        error={errors.renderedVideoId}
      />
      <Choice
        id="post-account"
        label="Social account"
        value={socialAccountId}
        onChange={setSocialAccountId}
        placeholder={accounts.length === 0 ? "Add a social account below first" : "Choose a social account"}
        options={accounts.map((account) => [account.id, accountName(account)])}
        error={errors.socialAccountId}
      />
      <Field
        id="post-date"
        label="Published on"
        type="date"
        value={publishedOn}
        onChange={(event) => setPublishedOn(event.target.value)}
        error={errors.publishedOn}
      />
      <Field
        id="post-url"
        label="Address of the Published Post"
        type="url"
        placeholder="https://"
        value={url}
        maxLength={URL_MAX_LENGTH}
        onChange={(event) => setUrl(event.target.value)}
        error={errors.url}
      />
      <Choice
        id="post-link"
        label="Affiliate link (optional)"
        value={affiliateLinkId}
        onChange={setAffiliateLinkId}
        placeholder="No affiliate link"
        options={links.map((link) => [
          link.id,
          link.publishedPostCount === 0 ? linkName(link) : `${linkName(link)} · used by ${postCount(link.publishedPostCount)}`,
        ])}
        error={errors.affiliateLinkId}
      />
      {chosenLink && chosenLink.publishedPostCount > 0 && (
        <p role="status" className="rounded-lg border border-amber-500/50 bg-amber-500/10 p-3 text-sm" data-testid="shared-link-warning">
          This affiliate link is already used by {postCount(chosenLink.publishedPostCount)}. Commission can then be shown
          for the link or the Product, but not for each Published Post. Give this one a link of its own if the
          programme allows.
        </p>
      )}
      {refused && (
        <p role="alert" className="text-sm text-destructive">
          {refused}
        </p>
      )}
      <div className="flex items-center gap-3">
        <Button type="submit" disabled={saving}>
          {saving ? "Recording…" : "Record Published Post"}
        </Button>
        {recorded && (
          <span role="status" className="text-sm text-muted-foreground">
            Recorded.
          </span>
        )}
      </div>
    </form>
  );
}

const postCount = (count: number) => (count === 1 ? "1 Published Post" : `${count} Published Posts`);

/** A labelled select whose first option chooses nothing. */
function Choice({
  id,
  label,
  value,
  onChange,
  placeholder,
  options,
  error,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  placeholder: string;
  options: (readonly [value: string, name: string])[];
  error?: string;
}) {
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label}</Label>
      <select
        id={id}
        className={SELECT}
        value={value}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : undefined}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">{placeholder}</option>
        {options.map(([option, name]) => (
          <option key={option} value={option}>
            {name}
          </option>
        ))}
      </select>
      {error && (
        <p id={`${id}-error`} role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
    </div>
  );
}

function AddAccount({ onAdded }: { onAdded: () => Promise<void> }) {
  const [platform, setPlatform] = useState<SocialPlatform>("TikTok");
  const [handle, setHandle] = useState("");
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<string>();

  const add = async (event: React.FormEvent) => {
    event.preventDefault();
    if (handle.trim() === "") {
      setRefused("Enter the social account's handle.");
      return;
    }
    setSaving(true);
    const { error, response } = await api
      .POST("/api/v1/lab/social-accounts", { body: { platform, handle } })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      setHandle("");
      setRefused(undefined);
      await onAdded();
    } else {
      setRefused(fieldErrors(error).handle?.join(" ") ?? problemDetail(error) ?? "The social account could not be added.");
    }
    setSaving(false);
  };

  return (
    <form onSubmit={add} noValidate className="flex flex-col gap-4 rounded-lg border p-4" data-testid="add-social-account">
      <h3 className="font-medium">Add a social account</h3>
      <div className="flex flex-col gap-2">
        <Label htmlFor="account-platform">Platform</Label>
        <select
          id="account-platform"
          className={SELECT}
          value={platform}
          onChange={(event) => setPlatform(event.target.value as SocialPlatform)}
        >
          {PLATFORMS.map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>
      </div>
      <Field
        id="account-handle"
        label="Handle"
        value={handle}
        maxLength={HANDLE_MAX_LENGTH}
        onChange={(event) => setHandle(event.target.value)}
        error={refused}
      />
      <Button type="submit" variant="outline" disabled={saving} className="self-start">
        {saving ? "Adding…" : "Add social account"}
      </Button>
    </form>
  );
}

function AddLink({ onAdded }: { onAdded: () => Promise<void> }) {
  const [url, setUrl] = useState("");
  const [label, setLabel] = useState("");
  const [saving, setSaving] = useState(false);
  const [refused, setRefused] = useState<string>();

  const add = async (event: React.FormEvent) => {
    event.preventDefault();
    if (url.trim() === "") {
      setRefused("Enter the affiliate link.");
      return;
    }
    setSaving(true);
    const { error, response } = await api
      .POST("/api/v1/lab/affiliate-links", { body: { url, label: label || null } })
      .catch(() => ({ error: undefined, response: undefined }));
    if (response?.ok) {
      setUrl("");
      setLabel("");
      setRefused(undefined);
      await onAdded();
    } else {
      setRefused(fieldErrors(error).url?.join(" ") ?? problemDetail(error) ?? "The affiliate link could not be added.");
    }
    setSaving(false);
  };

  return (
    <form onSubmit={add} noValidate className="flex flex-col gap-4 rounded-lg border p-4" data-testid="add-affiliate-link">
      <h3 className="font-medium">Add an affiliate link</h3>
      <Field
        id="link-url"
        label="Affiliate link"
        type="url"
        placeholder="https://"
        value={url}
        maxLength={URL_MAX_LENGTH}
        onChange={(event) => setUrl(event.target.value)}
        error={refused}
      />
      <Field
        id="link-label"
        label="Name (optional)"
        value={label}
        maxLength={LABEL_MAX_LENGTH}
        onChange={(event) => setLabel(event.target.value)}
      />
      <Button type="submit" variant="outline" disabled={saving} className="self-start">
        {saving ? "Adding…" : "Add affiliate link"}
      </Button>
    </form>
  );
}

function PostList({
  accounts,
  products,
  variants,
}: {
  accounts: SocialAccount[];
  products: Map<string, string>;
  variants: Map<string, string>;
}) {
  // A link from a Campaign's, Product's or Variant's page arrives already narrowed.
  const asked = useSearchParams();
  const [campaignId, setCampaignId] = useState(asked.get("campaignId") ?? "");
  const [productId, setProductId] = useState(asked.get("productId") ?? "");
  const [variantId, setVariantId] = useState(asked.get("variantId") ?? "");
  const [platform, setPlatform] = useState<SocialPlatform | "">("");
  const [socialAccountId, setSocialAccountId] = useState("");

  const campaigns = usePaged(["lab", "campaigns", "list"], () =>
    api.GET("/api/v1/lab/campaigns", { params: { query: { pageSize: PAGE_SIZE } } }),
  );
  const posts = useQuery({
    queryKey: ["lab", "publishing", "posts", { campaignId, productId, variantId, platform, socialAccountId }],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/lab/published-posts", {
        params: {
          query: {
            campaignId: campaignId || undefined,
            productId: productId || undefined,
            variantId: variantId || undefined,
            platform: platform || undefined,
            socialAccountId: socialAccountId || undefined,
            pageSize: PAGE_SIZE,
          },
        },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    // The list stays on screen while it is narrowed.
    placeholderData: keepPreviousData,
  });

  return (
    <section className="flex flex-col gap-4" data-testid="published-posts">
      <h2 className="text-xl font-semibold tracking-tight">
        Recorded
        {posts.data && <span className="text-muted-foreground"> · {posts.data.total}</span>}
      </h2>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
        <Choice
          id="filter-campaign"
          label="Campaign"
          value={campaignId}
          onChange={setCampaignId}
          placeholder="All"
          options={(campaigns.data?.items ?? []).map((campaign) => [campaign.id, campaign.name])}
        />
        <Choice
          id="filter-product"
          label="Product"
          value={productId}
          onChange={setProductId}
          placeholder="All"
          options={withAsked(products, productId, "The Product in the address")}
        />
        <Choice
          id="filter-variant"
          label="Variant"
          value={variantId}
          onChange={setVariantId}
          placeholder="All"
          options={withAsked(variants, variantId, "The Variant in the address")}
        />
        <Choice
          id="filter-platform"
          label="Platform"
          value={platform}
          onChange={(value) => setPlatform(value as SocialPlatform | "")}
          placeholder="All"
          options={PLATFORMS.map((value) => [value, value])}
        />
        <Choice
          id="filter-account"
          label="Social account"
          value={socialAccountId}
          onChange={setSocialAccountId}
          placeholder="All"
          options={accounts.map((account) => [account.id, accountName(account)])}
        />
      </div>
      {posts.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Published Posts could not be loaded.
        </p>
      ) : !posts.data ? (
        LOADING
      ) : posts.data.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">No Published Posts here yet.</p>
      ) : (
        <>
          <ul className="flex flex-col divide-y rounded-lg border">
            {posts.data.items.map((post) => (
              <RecordedPost key={post.id} post={post} />
            ))}
          </ul>
          {posts.data.total > posts.data.items.length && (
            <p className="text-sm text-muted-foreground">
              Showing the latest {posts.data.items.length} of {posts.data.total}. Narrow the list to see the rest.
            </p>
          )}
        </>
      )}
    </section>
  );
}

// What is chosen is always among the options: one that arrived in the address and that no
// approved Rendered Video listed here names is offered under a name of its own.
function withAsked(options: Map<string, string>, chosen: string, name: string): [string, string][] {
  return chosen && !options.has(chosen) ? [...options, [chosen, name]] : [...options];
}

function RecordedPost({ post }: { post: PublishedPost }) {
  return (
    <li className="flex flex-col gap-1 p-4" data-testid="published-post">
      <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <Link
          href={`/projects/${post.projectId}/variants/${post.variantId}`}
          className="min-w-0 break-words font-medium underline-offset-4 hover:underline"
        >
          {post.hook}
        </Link>
        <span className="shrink-0 text-sm text-muted-foreground">
          {post.socialAccount.platform} · {post.socialAccount.handle} · {post.publishedOn}
        </span>
      </div>
      <p className="text-sm text-muted-foreground">
        {post.productName} · {creativeTemplateName(post.creativeTemplate)}
      </p>
      <a
        href={post.url}
        target="_blank"
        rel="noopener noreferrer"
        className="break-all text-sm underline underline-offset-4"
      >
        {post.url}
      </a>
      <p className="text-sm text-muted-foreground">
        {post.affiliateLink ? (
          <>
            Affiliate link: <span className="break-all">{linkName(post.affiliateLink)}</span>
            {post.affiliateLinkShared && (
              <span data-testid="shared-link"> · shared with another Published Post, so Commission is not shown for this one</span>
            )}
            {post.commission && (
              <span data-testid="post-commission">
                {" · "}
                {post.commission.length === 0
                  ? "no Commission recorded yet"
                  : `Commission ${post.commission.map((total) => formatAmount(total.net, total.currency)).join(" + ")} net, from ${post.commission.flatMap((total) => total.sources).join(", ")}`}
              </span>
            )}
          </>
        ) : (
          "No affiliate link"
        )}
      </p>
      <p className="text-sm text-muted-foreground" data-testid="post-performance">
        {post.currentPerformance ? (
          <>
            {METRICS.map(({ metric, key }) => `${metric} ${formatTotal(post.currentPerformance?.[key]).toLowerCase()}`).join(" · ")}
            {" · "}
            {sourceName(post.currentPerformance.source).toLowerCase()}, as of {formatMoment(post.currentPerformance.takenAt)}, entered{" "}
            {formatMoment(post.currentPerformance.recordedAt)}
            {" · "}
          </>
        ) : (
          "No Performance Snapshot yet · "
        )}
        <Link href={`/lab/published-posts/${post.id}`} className="font-medium text-foreground underline underline-offset-4">
          Performance
        </Link>
      </p>
    </li>
  );
}
