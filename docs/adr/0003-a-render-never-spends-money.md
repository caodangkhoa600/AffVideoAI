# A render never spends money

Everything a paid provider makes for a video (Stills, Clips, speech) is generated in a step of its own at Storyboard review, after the member has seen an estimate and confirmed once. The Render job only assembles what is already there. We chose this over generating during the Render job, which is where the month-one job states ("generating assets", "generating video") had pointed, because a Render job is retried automatically, is cached Scene by Scene and is started freely, and none of that is safe when an Attempt costs money. It also lets a member reject a Still for cents before a Clip is paid for, and see and hear what a video will contain before rendering it.

## Consequences

- A Render job stays free, repeatable and safe to retry. Submitting one for a Storyboard version whose generated Scenes lack an approved Clip, or whose speech is missing, fails validation.
- Generation is never retaken automatically for quality. Only a technical failure that was not billed is retried without the member.
- Making a Hybrid video takes two waits, generation and then rendering, where Product Lock has one.
- A Clip exists apart from any Rendered Video. Its Production cost is recorded once, on the Clip, and is never split across the videos that use it.
- The ceiling on generation belongs to the Project, because several videos share its Clips.
