# Working notes

This directory holds working material for whoever picks a topic up next: investigations, the
reasoning behind a decision, and snapshots that were expensive to produce and would be expensive
to produce again.

The split to keep:

- `docs/` — documentation for people *using* StackExchange.Redis. It is a Jekyll site
  (`docs/_config.yml`, published at <https://seredis.dev>), so anything placed there is built into
  the site *and* listed in `sitemap.xml`.
- `notes/` — internal handover material. Not built, not published, not indexed.

Notes are grouped by topic in a subdirectory, and the files inside are named for their content
rather than repeating the topic — `notes/otel/findings.md`, not `notes/otel/otel-findings.md`.

When something in here graduates into advice for users, write it up in `docs/` and leave the note
as the record of *why*.

## Topics

- [`otel/`](otel/) — OpenTelemetry: what the ecosystem does today, and the plan for in-box
  traces and metrics.
