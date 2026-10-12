# 02: A Still for a generated backdrop

**What to build:** In a Hybrid Variant, the Hook Scene can have a generated backdrop. The member sees the Scene's description, which comes from the creative template with the Product's category and audience slotted in, and may edit it. They see an estimate, confirm, and get a Still, made in the background by a fake image provider. They approve it or ask for another. Nothing is rendered differently yet.

**Blocked by:** 01

**Status:** needs-triage

- [ ] The Technique list has generated backdrop and generated footage and no longer has image-to-video
- [ ] In Hybrid the planning engine gives the Hook Scene generated backdrop when the template allows it and an image provider exists; in Product Lock it never does
- [ ] With no provider configured, Hybrid plans exactly what it does today and generation says it is unavailable
- [ ] The description comes from the creative template; a member's edit replaces it for that Scene
- [ ] Nothing is generated until the member has seen the estimate and confirmed
- [ ] A Still is made on the existing queue, with Lease, Attempt and idempotency as a Render job has, and its state is shown
- [ ] A member approves a Still or asks for another; another is a new paid generation
- [ ] A Project has a ceiling on generation, $5 by default; a generation that would pass it is refused with the reason
- [ ] A technical failure that was not billed is retried without the member; nothing else is
- [ ] A refusal by the provider is shown as a failure with a reason
- [ ] Each Still, rejected ones included, has a Production cost record
- [ ] What the fake provider made is labelled as fake in the data and on screen
- [ ] A member of another Organization is refused a Still by identifier, including its file
