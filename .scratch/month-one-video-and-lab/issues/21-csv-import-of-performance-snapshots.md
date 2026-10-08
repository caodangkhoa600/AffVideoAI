# 21: CSV import of Performance Snapshots

**What to build:** A Lab member uploads a CSV file to add Performance Snapshots for many Published Posts at once, and gets a report of which rows were accepted and which were rejected and why.

**Blocked by:** 20

**Status:** wontfix

- [ ] The column layout is documented, and a template file can be downloaded
- [ ] Rows are matched to Published Posts by URL
- [ ] Every row is validated; valid rows are applied and invalid rows are reported with row number and reason
- [ ] Imported snapshots record their source as CSV import, with the file name
- [ ] Importing the same file twice creates no duplicates
- [ ] File size and row count are limited, and cell content is treated as data, never as a formula or instruction
- [ ] A row for a Published Post in another Organization is rejected as not found
- [ ] The analytics-importer interface exists, with CSV as its only implementation

## Comments

- 2026-10-08: Deferred out of month one to make room for the look work (tickets 25 and 26). Thirty Published Posts can be entered by hand. Reopen after the month.
