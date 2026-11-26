# Writing API contract v1

The Frontend team and Backend Master can mock these HTTP exchanges before the submission API and Worker ship. Use the existing authenticated cookie session and CSRF header. Send JSON with camelCase keys and UTC timestamps. Render every score and all feedback under a visible “Estimated Task 2 feedback” label. Render feedback strings as plain text.

## Submission and polling

POST /api/writing/submissions takes the body in [submission.json](examples/submission.json) and a required UUID Idempotency-Key header. The synthetic essay contains 100 words. One accepted submission costs one credit. Return 202 with Location pointing to the status resource and this receipt:

```json
{"submissionId":"018fa525-5529-7770-8df2-b455acbf0ce9","submittedAt":"2026-11-25T10:00:00+00:00","status":"Queued"}
```

Scope the key to the authenticated user and submission operation. Compare the prompt UUID and the exact decoded essay string. A retry after timeout reuses both key and body. Even after completion, replay returns the original receipt. A changed payload conflicts. Validate the existing accepted key before rechecking current balance or prompt availability. Rejections before acceptance leave the key available. Concurrent duplicates converge on one committed submission and charge. Retain the key throughout the submission lifetime.

GET /api/writing/submissions/{submissionId} returns 200 with submissionId, status and submittedAt. Omit absent fields. While Queued or Processing, poll Location with backoff. Succeeded adds completedAt and resultUrl. Fetch resultUrl once and stop polling. Failed adds completedAt and failure, e.g.:

```json
{"submissionId":"018fa525-5529-7770-8df2-b455acbf0ce9","status":"Failed","submittedAt":"2026-11-25T10:00:00+00:00","completedAt":"2026-11-25T10:01:20+00:00","failure":{"code":"EVALUATION_FAILED","creditRefunded":true}}
```

GET /api/writing/submissions/{submissionId}/result returns the [successful result](examples/result.json). All submission reads are owner-only. Status, detail and result return the same 404 with NOT_FOUND for both unknown and non-owned IDs. The history list and total include only the authenticated user's submissions.

Errors use the shared ProblemDetails envelope with uppercase code and lowercase kebab-case type URI. Invalid input or key returns 400. An inactive or missing prompt returns 404. Authentication and CSRF use existing middleware.

| Condition | HTTP | ProblemDetails code |
|---|---:|---|
| Insufficient credit | 402 | INSUFFICIENT_CREDIT |
| Same idempotency key with changed payload | 409 | IDEMPOTENCY_CONFLICT |
| Result requested while Queued or Processing | 409 | EVALUATION_NOT_READY |
| Result requested after terminal failure | 409 | EVALUATION_FAILED |

Map insufficient credit to ErrorType.PaymentRequired and all three 409 cases to ErrorType.Conflict. The Failed status payload uses EVALUATION_FAILED in its nested failure object, with HTTP 200 for the successful status read.

Example application/problem+json for HTTP 402.

```json
{"type":"https://bandup.app/errors/insufficient-credit","title":"Payment Required","status":402,"detail":"One credit is required for this evaluation.","instance":"/api/writing/submissions","code":"INSUFFICIENT_CREDIT","traceId":"writing-request-42"}
```

## Lifecycle for Cards 2 and 3

Queued starts when the essay and one charge commit together. Publish follows commit. A bounded recovery job must revisit unclaimed Queued submissions, including process crashes between commit and publish. Persist an acceptance deadline and retry schedule. Retry publishing with backoff up to a configured attempt/time budget. At expiry atomically transition a still-Queued submission to Failed and refund once. A late delivery can only claim a still-Queued eligible row. Processing starts on claim. Recover stale claims within a bounded budget. Retry transient failures in Processing, then atomically persist valid feedback and Succeeded, or refund and mark Failed. Retry an interrupted terminal transaction until reconciled. Conditional transitions and the unique ledger reference protect concurrent recovery and redelivery. Neither terminal state reopens. Handle stale/repeated deliveries idempotently. Cards 2 and 3 own runtime configuration and tests.

Card 2 must bound body bytes and essay characters before expensive processing, safely reject malformed Unicode before normalization, and enforce owner-only queries and state-dependent fields. Charge and save atomically. Protect the balance against concurrent submissions with different keys. Keep the accepted receipt replayable even if publishing fails. Save the selected prompt snapshot in that same transaction.

Card 3 must parse bounded model responses in .NET with size and depth limits, reject duplicate properties and validate Draft 2020-12 using the same schema files with offline reference resolution. Validate score tokens by exact numeric value before conversion to a bounded numeric type. Reject unsupported exponents and out-of-set values, including 1e-9999999 and values arbitrarily close to a half band. Run Python/.NET parity fixtures for valid and invalid payloads. Use ordinal evidence matching. Run validation in the Worker process without spawning Python. Fence stale workers with claim ownership or version checks on success, failure and refund. Commit results and transitions atomically, make refunds idempotent, and reconcile interrupted processing within the recovery budget.

## Validation

IELTS Academic Task 2 essays accept 100 through 350 words inclusive. For counting only, normalize NFKC, enumerate Unicode scalars and count letter/number runs. Attached combining marks extend the run. Internal ASCII hyphen, straight apostrophe and curly apostrophe join adjacent word parts. Other punctuation, whitespace and formatting characters delimit words. For example, well-being and don’t each count once, while 3.5 and word--word each count twice. Preserve the submitted string for evidence and replay comparison.

Use [the model schema](schemas/writing-feedback.model.v1.schema.json) before constructing typed DTOs. Every criterion needs explanation, strength and improvement arrays and at least one recommendation. Empty strength/improvement arrays are acceptable when justified. Optional evidence quotes must occur verbatim in the original essay. A reviewer assesses whether the observation is supported. Reject malformed JSON, duplicate keys, extra fields, invalid types, nulls and invalid scores. The raw model cannot set the version, label or overall. The public DTO supplies these fields. The [public schema](schemas/writing-feedback.public.v1.schema.json) references the model definitions by local registry ID.

BandUp uses four equal-weight scores from 0–9 on half-band increments. Round the decimal mean to the nearest half band with midpoint upward, so 6.25 becomes 6.5 and 6.75 becomes 7. These are product rules pending formal confirmation.

## Prompt list, history and detail

Writing Mode A requires an approved BandUp question and the learner’s own essay. Reject custom question text on submission. Backend Master approved all six API contracts. Card 1 owns the contracts and Card 2 owns their runtime implementation. The AI feature owner implements the APIs and coordinates with Frontend and Backend Master.

GET /api/writing/prompts returns the active approved prompts available to the signed-in learner. Include only currently approved, active Task 2 prompts. Return the entire picker in ascending prompt ID order, or {"items":[]} when empty. Each item requires id, title and promptText. Submit the selected id and render the supplied text.

See the [prompts response](examples/prompts.json).

GET /api/writing/submissions?page=1&pageSize=20 uses the existing PagedResult shape. Scope totals and rows to the authenticated owner. Sort by submittedAt descending with submissionId descending as tie-breaker. Use PageRequest defaults page 1 and pageSize 20, page at least 1 and pageSize clamped to 1–100. Historical titles in the list and question text in detail come from the immutable submission-time prompt snapshot. Each row requires submissionId, promptId, title, status and submittedAt. Terminal rows also require completedAt. Only a successful row includes estimatedOverallBand and scoreLabel. On selection fetch the owner-only GET /api/writing/submissions/{submissionId}/detail. Keeping detail separate from polling avoids repeatedly returning private essay text. Detail returns submissionId, original essayText and promptSnapshot containing id, title and promptText copied at acceptance, including after prompt retirement or edits. Then read the existing status URL and follow resultUrl on success. An empty page contains an empty items array.

See the [history response](examples/history.json).

See the [detail response](examples/detail.json) for the original essay and immutable question snapshot.
