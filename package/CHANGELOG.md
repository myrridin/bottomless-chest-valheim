# Changelog

## 0.1.0

First release.

- Buildable chest with unlimited storage and a typed search filter.
- Category tokens (`@food`, `@weapon`, ...) and name-sorted results.
- Take All and Stack respect the active search.
- Dedicated server support: contents are held server-side and paged to clients, so chest
  size does not affect network cost.
- Contents stored beside the world save rather than in the world file, and survive a
  destroyed chest, a crashed client or a failed mod load.
- Chests holding items cannot be dismantled, and never spill their contents on destruction.
