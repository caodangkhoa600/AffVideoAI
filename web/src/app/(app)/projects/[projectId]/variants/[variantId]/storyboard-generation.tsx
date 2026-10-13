"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import {
  api,
  problemDetail,
  type Clip,
  type ClipState,
  type Speech,
  type SpeechState,
  type Still,
  type StillState,
  type Storyboard,
  type Variant,
} from "@/lib/api/client";

/** Where a Still is (StillState in the domain), as a member reads it. There is no percentage to show. */
const STATES: Record<StillState, string> = {
  Queued: "Queued",
  Generating: "Being made",
  ReadyForReview: "Ready for review",
  Approved: "Approved",
  Rejected: "Rejected",
  Failed: "Failed",
};

const WAITING: StillState[] = ["Queued", "Generating"];

/** Where a Clip is (ClipState in the domain), as a member reads it. */
const CLIP_STATES: Record<ClipState, string> = {
  WaitingForStill: "Waiting for its Still to be approved",
  Queued: "Queued",
  Generating: "Being made",
  ReadyForReview: "Ready for review",
  Approved: "Approved",
  Failed: "Failed",
};

// A Clip that waits for its Still is not on its way: it starts when a member approves the Still.
const CLIP_WAITING: ClipState[] = ["Queued", "Generating"];

/** Where a piece of speech is (SpeechState in the domain), as a member reads it. Nobody approves speech. */
const SPEECH_STATES: Record<SpeechState, string> = {
  Queued: "Queued",
  Generating: "Being made",
  Made: "Made",
  Failed: "Failed",
};

const SPEECH_WAITING: SpeechState[] = ["Queued", "Generating"];

/** A length in seconds, to the tenth a Scene is timed to. */
const seconds = (durationMs: number) => `${(durationMs / 1000).toLocaleString(undefined, { maximumFractionDigits: 1 })} s`;

// How often the estimate is asked for while a Still or a Clip is on its way. The worker has it made; this page only asks.
const POLL_MS = 2000;

/** An estimated amount: cents, and finer only when there is something finer. Never an amount anyone was billed. */
const money = (amount: number, currency: string) =>
  `${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 6 })} ${currency}`;

/**
 * Paid generation for one Storyboard version: what it would make and cost, the member's one
 * confirmation, the Still of each generated Scene, to approve or to replace with another, the
 * Clip of each Scene of generated footage, to watch and approve, and the speech of each Scene's
 * narration text, to listen to. Only Hybrid has pictures; any version may be spoken.
 * Nothing is generated before the member confirms (ADR 0003).
 */
export function StoryboardGeneration({
  variant,
  storyboard,
  onAdded,
}: {
  variant: Variant;
  storyboard: Storyboard;
  /** A Storyboard version was added: approving a Still or a Clip makes the version that refers to it. */
  onAdded: () => Promise<void>;
}) {
  const queryClient = useQueryClient();
  const path = { projectId: variant.projectId, variantId: variant.id, version: storyboard.version };
  const key = ["projects", "one", variant.projectId, "variants", variant.id, "storyboards", storyboard.version, "generation"];
  const estimate = useQuery({
    queryKey: key,
    queryFn: async () => {
      const { data, response } = await api.GET(
        "/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{version}/generation",
        { params: { path } },
      );
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    refetchInterval: (query) =>
      query.state.data?.sceneStills.some((has) => WAITING.includes(has.still.state)) ||
      query.state.data?.sceneClips.some((has) => CLIP_WAITING.includes(has.clip.state)) ||
      query.state.data?.sceneSpeech.some((has) => SPEECH_WAITING.includes(has.speech.state))
        ? POLL_MS
        : false,
  });
  const [working, setWorking] = useState(false);
  const [refused, setRefused] = useState<string>();
  // One key for one wish to spend: a click sent twice, or sent again after the answer was
  // lost, generates once. A new key is made once the API has answered.
  const idempotencyKey = useRef<string>(undefined);

  // Whatever the API answered, the estimate is asked for again: it says where things stand now.
  const act = async (ask: (key: string) => Promise<{ ok: boolean; error: unknown }>, fallback: string) => {
    setWorking(true);
    setRefused(undefined);
    idempotencyKey.current ??= crypto.randomUUID();
    const { ok, error } = await ask(idempotencyKey.current).catch(() => ({ ok: false, error: undefined }));
    if (ok) {
      idempotencyKey.current = undefined;
    } else {
      setRefused(problemDetail(error) ?? fallback);
    }
    await queryClient.invalidateQueries({ queryKey: key });
    setWorking(false);
  };

  const confirm = (total: number) =>
    act(async (idempotency) => {
      const { error, response } = await api.POST(
        "/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{version}/generation",
        { params: { path, header: { "Idempotency-Key": idempotency } }, body: { confirmedTotal: total } },
      );
      return { ok: response.ok, error };
    }, "The generation could not be started.");

  const approve = (still: Still) =>
    act(async () => {
      const { error, response } = await api.POST("/api/v1/stills/{stillId}/approve", { params: { path: { stillId: still.id } } });
      // The version that uses the Still is the next one, and that is the one to read and render now.
      if (response.ok) await onAdded();
      return { ok: response.ok, error };
    }, "The Still could not be approved.");

  const approveClip = (clip: Clip) =>
    act(async () => {
      const { error, response } = await api.POST("/api/v1/clips/{clipId}/approve", { params: { path: { clipId: clip.id } } });
      // As for a Still: the version that uses the Clip is the next one.
      if (response.ok) await onAdded();
      return { ok: response.ok, error };
    }, "The Clip could not be approved.");

  // The button names what another costs now, and that amount is what is confirmed.
  const another = (still: Still, confirmedAmount: number) =>
    act(async (idempotency) => {
      const { error, response } = await api.POST("/api/v1/stills/{stillId}/another", {
        params: { path: { stillId: still.id }, header: { "Idempotency-Key": idempotency } },
        body: { confirmedAmount },
      });
      return { ok: response.ok, error };
    }, "Another Still could not be asked for.");

  const generated = storyboard.scenes.filter((scene) => scene.description !== null);
  const data = estimate.data;
  const hybrid = storyboard.renderMode === "Hybrid";
  const speaks = data !== undefined && data.speechProvider !== "None";
  // A Product Lock version has no pictures to generate: without a speech provider there is nothing here for it.
  if (!hybrid && !speaks) return null;

  return (
    <section className="flex flex-col gap-3" data-testid="generation">
      <h3 className="text-lg font-semibold tracking-tight">{hybrid ? "Generated pictures and narration" : "Spoken narration"}</h3>
      {speaks && (
        <p className="text-sm text-muted-foreground" data-testid="generation-speech-intro">
          Each Scene&apos;s narration text can be spoken, in the voice of the Project&apos;s brief ({data.voice}). Speech costs
          money, so none is made until you have seen the estimate and confirmed it. It is made once for the Project:
          only a Scene whose narration text changes is spoken again. Listen to each piece before rendering. A narration
          you upload is used in place of the speech.
        </p>
      )}
      {hybrid && (
      <p className="text-sm text-muted-foreground">
        In Hybrid up to two Scenes are generated: a backdrop behind the Product, or footage with no Product in it. The
        Product itself is always its photo. A backdrop is a Still. Footage is a Clip, made from its Still once you
        have approved that. Both are made once for the Project and used by every Variant of it. Making them costs
        money, so nothing is generated until you have seen the estimate and confirmed it, once, for the Stills and
        the Clips together. Approving a backdrop&apos;s Still, or a Clip, makes the next Storyboard version, which uses
        it: that version is the one to render. A render generates nothing and no provider is paid for it.
      </p>
      )}
      {estimate.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The estimate could not be loaded.
        </p>
      ) : !data ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <>
          {hybrid && !data.available && (
            <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="generation-unavailable">
              {data.unavailable}
            </p>
          )}
          {data.available && data.imageProvider === "Fake" && (
            <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="generation-fake">
              Pictures here come from the fake image provider: a fixed striped picture, not a picture of anything. No
              provider is called and nobody is charged; the amounts stand in for a real provider&apos;s.
            </p>
          )}
          {data.available && data.videoProvider === "Fake" && (
            <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="generation-fake-video">
              Clips here come from the fake video provider: a test pattern, not footage of anything. No provider is
              called and nobody is charged; the amounts stand in for a real provider&apos;s.
            </p>
          )}
          {hybrid && data.available && generated.length === 0 && (
            <p className="text-sm text-muted-foreground" data-testid="generation-nothing">
              No Scene of this version is generated.
            </p>
          )}
          {data.available && data.speechProvider === "Fake" && (
            <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="generation-fake-speech">
              Speech here comes from the fake speech provider: a tone as long as the text would take to say, not a
              voice. No provider is called and nobody is charged; the amounts stand in for a real provider&apos;s.
            </p>
          )}
          <p className="text-sm" data-testid="generation-spent">
            <span className="text-muted-foreground">Generation for this Project so far: </span>
            <span className="font-medium">{money(data.spent, data.currency)}</span>
            <span className="text-muted-foreground">
              {" "}
              of a ceiling of {money(data.ceiling, data.currency)}, estimated from configured rates. Rejected takes
              count.
            </span>
          </p>
          {(data.stills.length > 0 || data.clips.length > 0 || data.speech.length > 0) && (
            <div className="flex flex-col gap-2 rounded-lg border p-4" data-testid="generation-estimate">
              <p className="text-sm font-medium">Confirming will generate:</p>
              <ul className="list-disc pl-5 text-sm">
                {data.stills.map((still) => (
                  <li key={still.scenePosition}>
                    A Still for Scene {still.scenePosition}, estimated at {money(still.estimatedAmount, data.currency)}
                  </li>
                ))}
                {data.clips.map((clip) => (
                  <li key={`clip-${clip.scenePosition}`}>
                    A Clip of {seconds(clip.durationMs)} for Scene {clip.scenePosition}, estimated at{" "}
                    {money(clip.estimatedAmount, data.currency)}. It is made from the Scene&apos;s Still once you have
                    approved that, with no further confirmation.
                  </li>
                ))}
                {data.speech.map((speech) => (
                  <li key={`speech-${speech.scenePosition}`} data-testid="generation-speech">
                    Speech for Scene {speech.scenePosition}, about {seconds(speech.estimatedDurationMs)}, estimated at{" "}
                    {money(speech.estimatedAmount, data.currency)}
                    {speech.warning && (
                      <span role="alert" className="block text-destructive" data-testid="generation-speech-warning">
                        {speech.warning}
                      </span>
                    )}
                  </li>
                ))}
              </ul>
              <p className="text-sm">
                <span className="text-muted-foreground">Estimated total: </span>
                <span className="font-medium" data-testid="generation-total">
                  {money(data.total, data.currency)}
                </span>
              </p>
              <div>
                <Button onClick={() => confirm(data.total)} disabled={working} data-testid="generation-confirm">
                  {working ? "Starting…" : `Confirm and generate for ${money(data.total, data.currency)}`}
                </Button>
              </div>
            </div>
          )}
          {data.sceneStills.length > 0 && (
            <ul className="flex flex-col divide-y rounded-lg border">
              {data.sceneStills.map(({ scenePosition, still }) => (
                <StillRow
                  key={still.id}
                  scenePosition={scenePosition}
                  still={still}
                  // A Clip starts from one Still. Once it has, another Still would change nothing in the video.
                  anotherFor={
                    data.anotherStillAmount === null || data.sceneClips.some((has) => has.clip.stillId === still.id)
                      ? undefined
                      : money(data.anotherStillAmount, data.currency)
                  }
                  disabled={working}
                  onApprove={() => approve(still)}
                  onAnother={() => data.anotherStillAmount !== null && another(still, data.anotherStillAmount)}
                />
              ))}
            </ul>
          )}
          {data.sceneClips.length > 0 && (
            <ul className="flex flex-col divide-y rounded-lg border">
              {data.sceneClips.map(({ scenePosition, clip }) => (
                <ClipRow key={clip.id} scenePosition={scenePosition} clip={clip} disabled={working} onApprove={() => approveClip(clip)} />
              ))}
            </ul>
          )}
          {data.sceneSpeech.length > 0 && (
            <ul className="flex flex-col divide-y rounded-lg border">
              {data.sceneSpeech.map(({ scenePosition, speech }) => (
                <SpeechRow
                  key={`${scenePosition}-${speech.id}`}
                  scenePosition={scenePosition}
                  sceneDurationMs={storyboard.scenes.find((scene) => scene.position === scenePosition)?.durationMs}
                  speech={speech}
                />
              ))}
            </ul>
          )}
        </>
      )}
      {refused && (
        <p role="alert" className="text-sm text-destructive" data-testid="generation-refused">
          {refused}
        </p>
      )}
      {hybrid && <FailedStills variant={variant} descriptions={generated.map((scene) => scene.description!)} />}
      {speaks && <FailedSpeech variant={variant} texts={storyboard.scenes.map((scene) => scene.narrationText).filter((text) => text.trim() !== "")} />}
      <FailedClips
        variant={variant}
        descriptions={generated.filter((scene) => scene.technique === "GeneratedFootage").map((scene) => scene.description!)}
      />
    </section>
  );
}

function StillRow({
  scenePosition,
  still,
  anotherFor,
  disabled,
  onApprove,
  onAnother,
}: {
  scenePosition: number;
  still: Still;
  /** What another Still is estimated at now. Absent when nothing can be generated. */
  anotherFor?: string;
  disabled: boolean;
  onApprove: () => void;
  onAnother: () => void;
}) {
  const made = still.state === "ReadyForReview" || still.state === "Approved";
  return (
    <li className="flex gap-4 p-4" data-testid="still">
      {made && (
        // Not next/image: the file comes from the API, which only serves it to a signed-in member of the Organization.
        // eslint-disable-next-line @next/next/no-img-element
        <img
          src={`/api/v1/stills/${still.id}/content`}
          alt={
            still.technique === "GeneratedFootage"
              ? `The Still the footage of Scene ${scenePosition} starts from`
              : `The generated backdrop of Scene ${scenePosition}`
          }
          loading="lazy"
          className="aspect-[9/16] w-28 shrink-0 rounded-lg border bg-muted object-cover"
        />
      )}
      <div className="flex min-w-0 flex-col gap-2">
        <p className="text-sm" aria-live="polite">
          <span className="text-muted-foreground">Still for Scene {scenePosition} · </span>
          <span className="font-medium" data-testid="still-state">
            {STATES[still.state]}
          </span>
        </p>
        {still.fake && (
          <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="still-fake">
            Fake: made by the fake image provider. It is a fixed picture and shows nothing of the description.
          </p>
        )}
        <p className="break-words text-sm">
          <span className="text-muted-foreground">Description: </span>
          {still.description}
        </p>
        {still.state === "Queued" && still.retryAt && (
          <p className="text-sm text-muted-foreground" data-testid="still-retry">
            Attempt {still.attempt} failed without being charged for. It will be tried again after{" "}
            {new Date(still.retryAt).toLocaleTimeString()}.
          </p>
        )}
        {still.cost && (
          <p className="text-sm text-muted-foreground" data-testid="still-cost">
            Estimated cost: {money(still.cost.estimatedAmount, still.cost.currency)}
            {still.cost.model ? ` · ${still.cost.provider} · ${still.cost.model}` : ""}
          </p>
        )}
        {made && (
          <div className="flex flex-wrap gap-2">
            {still.state === "ReadyForReview" && (
              <Button size="sm" onClick={onApprove} disabled={disabled} data-testid="still-approve">
                Approve
              </Button>
            )}
            {anotherFor && (
              <Button size="sm" variant="outline" onClick={onAnother} disabled={disabled} data-testid="still-another">
                Ask for another for {anotherFor}
              </Button>
            )}
          </div>
        )}
        {made && anotherFor && (
          <p className="text-sm text-muted-foreground">
            Another is a new paid generation. This one is then rejected and kept, with what it cost.
          </p>
        )}
      </div>
    </li>
  );
}

/** The Clip of a Scene of generated footage: where it is, and once it is made the video itself, to watch before approving. */
function ClipRow({
  scenePosition,
  clip,
  disabled,
  onApprove,
}: {
  scenePosition: number;
  clip: Clip;
  disabled: boolean;
  onApprove: () => void;
}) {
  const made = clip.state === "ReadyForReview" || clip.state === "Approved";
  return (
    <li className="flex gap-4 p-4" data-testid="clip">
      {made && (
        // The file comes from the API, which only serves it to a signed-in member of the Organization. It has no sound.
        <video
          src={`/api/v1/clips/${clip.id}/content`}
          controls
          muted
          loop
          playsInline
          preload="metadata"
          aria-label={`The generated footage of Scene ${scenePosition}`}
          className="aspect-[9/16] w-28 shrink-0 rounded-lg border bg-muted object-cover"
        />
      )}
      <div className="flex min-w-0 flex-col gap-2">
        <p className="text-sm" aria-live="polite">
          <span className="text-muted-foreground">
            Clip for Scene {scenePosition} · {seconds(clip.durationMs)} ·{" "}
          </span>
          <span className="font-medium" data-testid="clip-state">
            {CLIP_STATES[clip.state]}
          </span>
        </p>
        {clip.fake && (
          <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="clip-fake">
            Fake: made by the fake video provider. It is a test pattern and shows nothing of the description.
          </p>
        )}
        {clip.state === "WaitingForStill" && (
          <p className="text-sm text-muted-foreground">
            It was confirmed at {money(clip.estimatedAmount, clip.currency)} and is made from the Scene&apos;s Still.
            Approve that Still to start it.
          </p>
        )}
        {clip.state === "Queued" && clip.retryAt && (
          <p className="text-sm text-muted-foreground" data-testid="clip-retry">
            Attempt {clip.attempt} failed without being charged for. It will be tried again after{" "}
            {new Date(clip.retryAt).toLocaleTimeString()}.
          </p>
        )}
        {clip.cost && (
          <p className="text-sm text-muted-foreground" data-testid="clip-cost">
            Estimated cost: {money(clip.cost.estimatedAmount, clip.cost.currency)}
            {clip.cost.model ? ` · ${clip.cost.provider} · ${clip.cost.model}` : ""}. It is the Clip&apos;s, once, however
            many videos use it.
          </p>
        )}
        {clip.state === "ReadyForReview" && (
          <div className="flex flex-wrap gap-2">
            <Button size="sm" onClick={onApprove} disabled={disabled} data-testid="clip-approve">
              Approve
            </Button>
          </div>
        )}
      </div>
    </li>
  );
}

/** The speech of a Scene's narration text: where it is, and once it is made the sound itself, to listen to before rendering. */
function SpeechRow({
  scenePosition,
  sceneDurationMs,
  speech,
}: {
  scenePosition: number;
  /** How long the Scene lasts. Absent when the version has no such Scene. */
  sceneDurationMs?: number;
  speech: Speech;
}) {
  // Speech longer than its Scene is sped up at render, by a tenth at most. Beyond that the render fails.
  const tooLong = speech.state === "Made" && sceneDurationMs !== undefined && speech.durationMs * 10 > sceneDurationMs * 11;
  return (
    <li className="flex flex-col gap-2 p-4" data-testid="speech">
      <p className="text-sm" aria-live="polite">
        <span className="text-muted-foreground">
          Speech for Scene {scenePosition}
          {speech.state === "Made" ? ` · ${seconds(speech.durationMs)}` : ""} · {speech.voice} ·{" "}
        </span>
        <span className="font-medium" data-testid="speech-state">
          {SPEECH_STATES[speech.state]}
        </span>
      </p>
      {speech.fake && (
        <p className="w-fit rounded-md border bg-muted px-2 py-1 text-sm" data-testid="speech-fake">
          Fake: made by the fake speech provider. It is a tone and says nothing of the text.
        </p>
      )}
      <p className="break-words text-sm">
        <span className="text-muted-foreground">Narration text: </span>
        {speech.narrationText}
      </p>
      {speech.state === "Made" && (
        // The file comes from the API, which only serves it to a signed-in member of the Organization.
        <audio
          src={`/api/v1/speech/${speech.id}/content`}
          controls
          preload="none"
          aria-label={`The speech of Scene ${scenePosition}`}
          className="w-full max-w-md"
        />
      )}
      {tooLong && (
        <p role="alert" className="text-sm text-destructive" data-testid="speech-too-long">
          This speech lasts {seconds(speech.durationMs)} and the Scene {seconds(sceneDurationMs)}. Speech is sped up by a
          tenth at most, so a render of this version will fail. Shorten the narration text or give the Scene more time.
        </p>
      )}
      {speech.state === "Queued" && speech.retryAt && (
        <p className="text-sm text-muted-foreground" data-testid="speech-retry">
          Attempt {speech.attempt} failed without being charged for. It will be tried again after{" "}
          {new Date(speech.retryAt).toLocaleTimeString()}.
        </p>
      )}
      {speech.cost && (
        <p className="text-sm text-muted-foreground" data-testid="speech-cost">
          Estimated cost: {money(speech.cost.estimatedAmount, speech.cost.currency)}
          {speech.cost.model ? ` · ${speech.cost.provider} · ${speech.cost.model}` : ""}
        </p>
      )}
    </li>
  );
}

/**
 * Why the newest speech of a narration text of this version was not made, when it was not: the
 * Scene then has no speech, and the estimate above offers it again.
 */
function FailedSpeech({ variant, texts }: { variant: Variant; texts: string[] }) {
  const speech = useQuery({
    queryKey: ["projects", "one", variant.projectId, "speech"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}/speech", {
        params: { path: { projectId: variant.projectId }, query: { pageSize: 50 } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    enabled: texts.length > 0,
    refetchInterval: POLL_MS * 5,
  });
  // Newest first: only the latest speech of each text says where it stands.
  const failed = [...new Set(texts)]
    .map((text) => speech.data?.items.find((said) => said.narrationText === text))
    .filter((said): said is Speech => said?.state === "Failed");
  if (failed.length === 0) return null;

  return (
    <ul className="flex flex-col gap-2">
      {failed.map((said) => (
        <li key={said.id} role="alert" className="text-sm text-destructive" data-testid="speech-failure">
          {said.failure?.message ?? "The speech could not be made."}
          {said.failure?.billed ? " The provider charged for the attempt all the same." : ""}
        </li>
      ))}
    </ul>
  );
}

/**
 * Why the newest Clip of footage this version describes was not made, when it was not: the
 * Scene then has no Clip, and the estimate above offers it again.
 */
function FailedClips({ variant, descriptions }: { variant: Variant; descriptions: string[] }) {
  const clips = useQuery({
    queryKey: ["projects", "one", variant.projectId, "clips"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}/clips", {
        params: { path: { projectId: variant.projectId }, query: { pageSize: 50 } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    enabled: descriptions.length > 0,
    refetchInterval: POLL_MS * 5,
  });
  // Newest first: only the latest Clip of each piece of footage says where it stands.
  const failed = descriptions
    .map((description) => clips.data?.items.find((clip) => clip.description === description))
    .filter((clip): clip is Clip => clip?.state === "Failed");
  if (failed.length === 0) return null;

  return (
    <ul className="flex flex-col gap-2">
      {failed.map((clip) => (
        <li key={clip.id} role="alert" className="text-sm text-destructive" data-testid="clip-failure">
          {clip.failure?.message ?? "The Clip could not be made."}
          {clip.failure?.billed ? " The provider charged for the attempt all the same." : ""}
        </li>
      ))}
    </ul>
  );
}

/**
 * Why the newest Still of a picture this version describes was not made, when it was not: the
 * Scene then has no Still, and the estimate above offers it again.
 */
function FailedStills({ variant, descriptions }: { variant: Variant; descriptions: string[] }) {
  const stills = useQuery({
    queryKey: ["projects", "one", variant.projectId, "stills"],
    queryFn: async () => {
      const { data, response } = await api.GET("/api/v1/projects/{projectId}/stills", {
        params: { path: { projectId: variant.projectId }, query: { pageSize: 50 } },
      });
      if (!data) throw new Error(`The API answered ${response.status}`);
      return data;
    },
    refetchInterval: POLL_MS * 5,
  });
  // Newest first: only the latest take of each picture says where it stands.
  const failed = descriptions
    .map((description) => stills.data?.items.find((still) => still.description === description))
    .filter((still): still is Still => still?.state === "Failed");
  if (failed.length === 0) return null;

  return (
    <ul className="flex flex-col gap-2">
      {failed.map((still) => (
        <li key={still.id} role="alert" className="text-sm text-destructive" data-testid="still-failure">
          {still.failure?.message ?? "The picture could not be made."}
          {still.failure?.billed ? " The provider charged for the attempt all the same." : ""}
        </li>
      ))}
    </ul>
  );
}
