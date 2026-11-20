# Contributing

## Branching strategy

`development` is the active integration branch — all work branches off it and merges back
into it. `main` stays on the last submitted/released state and only receives a merge from
`development` at a milestone (end of term), not on every change. Both branches are
protected: no direct pushes, merging requires a green CI run and at least one review
approval.

All work happens on a short-lived branch off the latest `development`, named
`<type>/<short-description>`:

| Type | Use it for |
|---|---|
| `feat/` | a new capability |
| `fix/` | a bug fix |
| `chore/` | tooling, infra, dependencies — nothing user-facing |
| `docs/` | documentation only |

Examples: `feat/identity-register-login`, `fix/attempt-timezone-bug`, `chore/devops-deployment`.

Keep a branch focused on one card and open a pull request back into `development` as soon as
there is something reviewable — do not let a branch sit unmerged for more than a few days.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/): `type(scope): summary`, written in
English, imperative mood, under ~72 characters for the summary line. Use the same `type` prefixes
as the branch names above, plus `test:`, `refactor:` and `ci:` where they fit better than `chore:`.

## Pull requests

- Base the PR on `development`, not `main`.
- Link the PR to its Trello card in the description.
- Describe what changed and why, not just what — the "why" is what a reviewer actually needs.
- CI (build + test) must pass before merge.
- At least one team member must approve before merge.
- Prefer a small, focused PR over a large one spanning multiple cards.

## Task tracking

Every card on the [Trello board](https://trello.com/b/oeAHpSaq/software) moves through
`Sprint Backlog → In Progress → Review/Testing → Done` as work on it progresses — move a card
yourself when its status changes, rather than leaving it to catch up with reality later. Link
the related PR on the card once one exists.
