# The Affiliate Lab is a flag on an ordinary Organization

Our own affiliate operation runs as a normal Organization with the Affiliate Lab enabled, using the same Product, Variant and Rendered Video entities as every paying Organization. Campaigns, Published Posts and Performance Snapshots are extra entities reachable only when the flag is on. We chose this over a separate internal admin area with its own product tables so that we use the product exactly as customers do, and so the Lab can later be offered to customers without a migration.

## Consequences

- Lab data is tenant-scoped like everything else; there is no cross-organization "internal" view.
- Anything the Lab needs from a Product (affiliate URL, commission) lives on the shared Product or on Lab-only entities, never in a parallel product table.
