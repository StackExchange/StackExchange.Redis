# Release Notes

## Unreleased changes:

- fix: make performance-counter tracking opt-in (`IncludePerformanceCountersInExceptions`) as it was [causing problems](#587)
- add: can now specifiy allowed SSL/TLS protocols  (#603)
- add: track message status in exceptions (#576)
- improved connection retry policy (#510, #572)

Other changes (not library related)

- (project) refactor /docs for github pages
- improve release note tracking
- rework build process to use csproj

## 1.2.1

- fix: avoid overlapping per-endpoint heartbeats

## 1.2.0

- (same as 1.2.0-alpha1)

## 1.2.0-alpha1

- add: GEO commands (#489)
- add: ZADD support for new NX/XX switches (#520)
- add: core-clr preview support improvements

(I'm happy to take PRs for change history going back in time)