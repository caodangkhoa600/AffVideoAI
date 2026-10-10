# AffiVideo

AffiVideo turns product information and product images into short vertical marketing videos. The same system serves paying organizations and our own affiliate experiments.

## Language

### Tenancy

**Organization**:
The tenant that owns products, projects and videos. Our own affiliate operation is an ordinary Organization.
_Avoid_: Workspace, account, tenant

**Member**:
A person who signs in. A member belongs to exactly one Organization, as its Owner or as an Editor.
_Avoid_: User, account

**Owner**:
A member who can do everything in their Organization, including adding members and changing its settings.
_Avoid_: Admin

**Editor**:
A member who does all creative work but cannot manage members or the Organization's settings.

**Audit log**:
The record of sensitive actions in an Organization: who did what, and when. Entries are never changed.

**Affiliate Lab**:
The set of capabilities (Campaigns, Published Posts, Performance Snapshots, Commission records) available only to Organizations that have it enabled.
_Avoid_: Admin area, internal module

### Products

**Product**:
A thing being advertised, described by its details, assets and Facts. The same concept inside and outside the Affiliate Lab.
_Avoid_: Item, listing, SKU

**Archived**:
The state of a Product that is out of the way of day-to-day work. It is kept, with everything made from it.
_Avoid_: Deleted, inactive, hidden

**Asset**:
A photo or the logo of a Product, kept as the image a member's upload decodes to. A Product has several photos and at most one logo.
_Avoid_: Media, attachment

**Fact**:
A single statement about a Product, written in one language, that a script may use only once it is Confirmed.
_Avoid_: Claim, feature, verified fact

**Proposed**:
The state of a Fact that has been suggested, by a person or by AI extraction, but not yet Confirmed.
_Avoid_: Unverified, pending

**Confirmed**:
The state of a Fact that a member of the Organization has explicitly accepted. It records who and when; the Organization is responsible for its truth.
_Avoid_: Verified, approved

**Withdrawn**:
The state of a Fact that is no longer to be used. A Fact is never edited in place: changing it withdraws it and creates a new Proposed Fact.
_Avoid_: Deleted, rejected, retracted

### Creative work

**Project**:
One Product plus one brief (audience, language, duration, objective).
_Avoid_: Creative project, job

**Variant**:
One creative treatment of a Project, defined by its creative template and its Hook. It is the unit compared in an experiment.
_Avoid_: Version, concept, creative

**Hook**:
The opening line of a Variant, meant to stop the viewer scrolling. A different Hook makes a different Variant.
_Avoid_: Opener, headline

**Storyboard**:
A versioned, ordered list of Scenes for a Variant. Any edit produces a new Storyboard version.
_Avoid_: Shot plan, script, timeline

**Scene**:
One timed segment of a Storyboard, with its text, source assets and Technique.
_Avoid_: Shot, clip, segment

**Narration**:
A recording of a voice that a member uploads for a Variant, heard over every video rendered for it afterwards. A Scene's narration text is words; Narration is sound, and nothing makes one from the other.
_Avoid_: Voiceover, voice track

**Music**:
A recording that a member uploads for a Variant, mixed under its videos at a volume the member chooses. A Variant has at most one Narration and one Music.
_Avoid_: Soundtrack, background track, BGM

**Rights confirmation**:
A member's statement, made on uploading Narration or Music, that they hold the rights to use it. It records who and when; the Organization is responsible for its truth.
_Avoid_: Licence check, clearance

**Manually Edited**:
A mark on a Scene whose text a person changed. Its text is no longer traceable to Facts and is the Organization's responsibility. The mark stays through later versions until the Scene is regenerated.
_Avoid_: Overridden, custom

**Flagged for Review**:
A mark on a Storyboard version or Rendered Video that used a Fact which has since been Withdrawn. Flagged work is kept, never deleted. A member clears the mark after reviewing the work, one version or one video at a time.
_Avoid_: Invalidated, stale, needs attention

**Rendered Video**:
The MP4 produced from one specific Storyboard version.
_Avoid_: Output, export, video file

**Ready for review**:
The state of a Rendered Video that no member has approved yet. It can be previewed but not downloaded.
_Avoid_: Pending, draft

**Approved**:
The state of a Rendered Video that a member has marked as fit to publish. It records who and when, and only an approved Rendered Video can be downloaded or recorded as a Published Post. A Fact is Confirmed, never approved.
_Avoid_: Published, final, accepted

**Library**:
All the Rendered Videos of an Organization, in one list.
_Avoid_: Gallery, media library

### Rendering

**Technique**:
How a single Scene is produced: static image, image motion, image-to-video, video asset, text animation or 3D render.
_Avoid_: Rendering mode, scene type

**Render Mode**:
The per-video choice that limits which Techniques the planner may use: Product Lock or Hybrid.
_Avoid_: Mode A/B/C, AI Video mode, credit tier

**Product Lock**:
The Render Mode that allows no generative Techniques, so the Product's appearance is never altered.

**Hybrid**:
The Render Mode that allows generative Techniques within a budget, alongside non-generative ones.
_Avoid_: AI Video, Enhanced Hybrid, Premium Cinematic

**Render job**:
The work of rendering one Storyboard version, done by a worker in the background. Its state is the only progress a member is shown.
_Avoid_: Task, render request

**Attempt**:
One taking of a Render job by a worker. A job whose attempt fails for a reason that might pass, or whose worker stops, is tried again after a wait, a limited number of times.
_Avoid_: Retry count, run

**Lease**:
A worker's time-limited hold on a Render job, which it renews while it works. A job whose lease runs out goes back to the queue.
_Avoid_: Lock, claim

**Production cost**:
What rendering is estimated to have cost the Organization, from rates it configures. One record is kept for each Attempt, failed ones included. It is always an estimate, never an amount anyone was billed.
_Avoid_: Bill, charge, spend, price

**Cut-out**:
A Product photo with everything but the Product made transparent. Only transparency is decided; the Product's own pixels are the photo's.
_Avoid_: Mask, background removal

**Card**:
A photo shown whole, with rounded corners, because no cut-out of it passed its checks.

### Affiliate Lab

**Shortlist**:
The Products a Lab member is considering promoting, each with research notes and its commission rate or amount when known. A shortlisted Product is the same Product as everywhere else.
_Avoid_: Watchlist, candidates, research list

**Campaign**:
A grouping of Variants across Products for one experiment. It references Variants and never owns them.

**Published Post**:
One approved Rendered Video posted on one social account at one URL. The same video on three accounts is three Published Posts.
_Avoid_: Publication, upload, post

**Social account**:
An account on a platform that videos are posted on, known by its platform and handle. It is kept so it can be chosen again.
_Avoid_: Channel, profile, page

**Affiliate link**:
A link an affiliate programme gave, kept so it can be chosen again. Commission is shown for a Published Post only when no other Published Post carries its link.
_Avoid_: Tracking link, referral URL

**Performance Snapshot**:
The running totals for a Published Post as of one moment, from one named source. The latest snapshot is the current figure.
_Avoid_: Metrics entry, stats, daily delta

**Commission**:
Affiliate earnings recorded at the level the source report gives them (a link or a Product). It is never split across Published Posts that share a link.
_Avoid_: Revenue, attributed sales

**Commission record**:
What one Report says for one period: orders, confirmed orders, Commission, refunds and adjustments, in one currency, for exactly one affiliate link or one Product.
_Avoid_: Commission entry, payout, earnings row

**Report**:
The statement an affiliate programme gives of what was earned, as the member names it. It is where a Commission record's figures were read.
_Avoid_: Source, statement, dashboard

**Source**:
How a figure got into the system, such as manual entry. It is not where the figure was read: for Commission that is the Report.
_Avoid_: Origin, provider, channel

**Refund**:
Commission an affiliate programme took back because orders were refunded. It is an amount of Commission, not the money returned to the buyer.
_Avoid_: Return, chargeback, clawback

**Adjustment**:
A change an affiliate programme made to Commission for any reason other than a refund. It takes Commission away or adds some.
_Avoid_: Correction, deduction, penalty, bonus

**Net Commission**:
Commission less refunds, with adjustments applied. It can be below zero.
_Avoid_: Profit, payout, earnings
