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
The set of capabilities (Campaigns, Published Posts, Performance Snapshots) available only to Organizations that have it enabled.
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

**Manually Edited**:
A mark on a Scene whose text a person changed. Its text is no longer traceable to Facts and is the Organization's responsibility.
_Avoid_: Overridden, custom

**Flagged for Review**:
A mark on a Storyboard or Rendered Video that used a Fact which has since been Withdrawn. Flagged work is kept, never deleted.

**Rendered Video**:
The MP4 produced from one specific Storyboard version.
_Avoid_: Output, export, video file

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

### Affiliate Lab

**Campaign**:
A grouping of Variants across Products for one experiment. It references Variants and never owns them.

**Published Post**:
One approved Rendered Video posted on one social account at one URL. The same video on three accounts is three Published Posts.
_Avoid_: Publication, upload, post

**Performance Snapshot**:
The running totals for a Published Post as of one moment, from one named source. The latest snapshot is the current figure.
_Avoid_: Metrics entry, stats, daily delta

**Commission**:
Affiliate earnings recorded at the level the source report gives them (a link or a Product). It is never split across Published Posts that share a link.
_Avoid_: Revenue, attributed sales
