# Admin passages API

All routes below require an active `Admin` or `SuperAdmin`. An anonymous, invalid or inactive
session receives 401; an ordinary active `User` receives 403. Authentication and permissions
use the existing cookies and current database role checks.

## Passage list — `Admin_ListPassages`

`GET /api/admin/passages?page=1&pageSize=4&status=Draft&search=beekeeping`

| Parameter | Contract |
|---|---|
| `page` | Default 1; clamped to at least 1. |
| `pageSize` | Default 20; clamped to 1–100. Send 4 to match the design. |
| `status` | Optional: `Draft`, `Published`, `Archived`. Omit for the All tab. |
| `search` | Optional case-insensitive substring of the title, trimmed, maximum 320 characters. |

Search and status combine with AND and apply before counting and pagination. Blank search
does not filter. Invalid status or overlong search returns 400. Pages beyond the end return
an empty `items` array while retaining the filtered `totalCount`.

```json
{
  "items": [
    {
      "id": "019aef00-0000-7000-8000-000000000001",
      "title": "Urban Beekeeping in the Digital Age",
      "status": "Draft",
      "version": 1,
      "publishedAt": null,
      "questionCount": 5,
      "updatedAt": "2026-11-22T12:00:00Z",
      "durationMinutes": 20
    }
  ],
  "page": 1,
  "pageSize": 4,
  "totalCount": 1
}
```

`questionCount` includes every question in that passage, including draft questions. The list
does not return question prompts, answer keys or explanations. Cards are ordered by known
update time first, newest first, then title and ID as stable tie-breakers. Records with an
unknown update time appear last.

The current product has no configurable Reading category; `Academic Reading` and the book
illustration in the design are frontend presentation, not new API fields.

## New passage — existing `Admin_CreatePassage`

`POST /api/admin/passages`

```json
{
  "title": "Urban Beekeeping in the Digital Age",
  "body": "Passage text...",
  "sourceNote": "BandUp team",
  "usageBasis": "Original content",
  "durationMinutes": 20
}
```

All four text fields are required and nonblank. Returns 201 with `{ "id": "..." }` and a Location
pointing to the detail route. The server supplies the creator, initial Draft status and
version 1. A new passage has zero questions until they are added separately.

## Edit draft / View details — existing routes

- `GET /api/admin/passages/{passageId}` (`Admin_GetPassage`): the full passage, questions,
  explanations and answer keys, `updatedAt`, `wordCount`, `isReadOnly`, `canCreateRevision`
  and `publishReadiness`. Answer keys also expose `isVerified`. Returns 404 if missing.
- `PUT /api/admin/passages/{passageId}` (`Admin_EditPassage`): the same four fields as create;
  returns 204. Only drafts are editable. Published or archived content returns 409.
- `POST /api/admin/passages/{passageId}/revisions` (`Admin_CreatePassageRevision`): creates a
  draft revision of the latest previously published version. Existing published content stays
  available until its replacement is published. Copied answer keys must be verified again.

For the design's primary card action, use status Draft to display Edit draft; for Published
or Archived, display View details. Both load the same GET detail endpoint.

## Passage editor

### Activity duration

POST/PUT accept `durationMinutes`, a positive integer number of minutes. Zero, negative,
fractional and nonnumeric values return 400. Omitted/null on POST uses 20 minutes;
omitted/null on PUT preserves the current duration. List and detail GET responses both
return the saved durationMinutes. Only Draft is editable; use a revision for published
content. Revisions inherit the source duration and can change it independently.
Changing duration alone does not reset answer verification. It updates updatedAt;
saving identical values does not.

This sprint stores and exposes duration in admin APIs only. Learner DTOs, the current
20-minute exam rule and existing attempt deadlines are unchanged. Applying this metadata
to learner timers is a separate integration task.

Both admin roles use the same editor APIs. Details/Text/Questions/Review tabs scroll within
the frontend and use the same passage detail response. The 20-minute reading time comes from
the existing exam rule; Academic Reading is the current product category.

The title, original text, source note and usage basis are saved with the existing passage
POST/PUT. The source metadata remains required; the original-content screen can send
`sourceNote: "BandUp team"` and `usageBasis: "Original content"`. Questions are saved through
the routes below. Saving a draft does not publish it or require verified answers. A passage
still needs a nonblank title and body, and each question needs a nonblank prompt.

### Questions, explanations and checkboxes

Use `/api/admin/passages/{passageId}/questions` as the question collection path, and
`/api/admin/passages/{passageId}/questions/{questionId}` as the individual question path.

| Method and suffix | Body / behavior |
|---|---|
| `POST` collection | `{ "type": "TrueFalseNotGiven", "prompt": "A claim.", "sortOrder": 1 }`; returns 201 `{ "id": "..." }`. The other supported type is `SentenceCompletion`. |
| `GET` question | Full question, explanation and answer key, or 404 when it does not belong to the passage. |
| `PUT` question | `{ "prompt": "Updated claim.", "sortOrder": 1 }`; returns 204. |
| `DELETE` question | Removes the draft question; returns 204. |
| `PUT /answer-key` | `{ "correctAnswer": "True", "acceptedVariants": [] }`; returns 204. TFNG accepts True/False/Not Given, case-insensitively. Variants are only supported for Sentence Completion. |
| `DELETE /answer-key` | Clears the draft's answer while retaining the question and explanation; returns 204, including if already absent. |
| `PUT /explanation` | Sets all four fields: `explanation`, `evidenceSentence`, `misleadingSentence`, `unspecifiedPart`. Omitted/null/blank fields clear the saved value. Incomplete explanations can be saved in a draft; returns 204. |
| `POST /answer-key/verify` | Checks the answer and explanation, then records the current admin and server time; returns 204. Complete content is required. |
| `DELETE /answer-key/verify` | Unchecks verification without clearing the answer/explanation; returns 204. |

For True/False and Sentence Completion, verification requires a nonblank explanation and
evidence sentence, with no unspecified part. For Not Given, verification requires an
explanation and the unspecified information, with no evidence sentence. A misleading
sentence is optional. Presence and verification are enforced; the API cannot determine
whether the teacher's explanation is factually correct.

Changes to the answer, accepted variants, prompt or explanation invalidate that question's
verification. Changing the passage body invalidates every question's verification. Saving
identical values preserves verification and `updatedAt`. Save the content before checking
its verification checkbox.

### Review and publish

GET passage details includes this content readiness report:

```json
{
  "wordCount": 169,
  "isReadOnly": false,
  "canCreateRevision": false,
  "publishReadiness": {
    "isReady": false,
    "errors": {
      "questions.019aef00-0000-7000-8000-000000000001.explanation": [
        "An explanation is required."
      ],
      "questions.019aef00-0000-7000-8000-000000000001.evidenceSentence": [
        "An evidence sentence from the passage is required."
      ],
      "questions.019aef00-0000-7000-8000-000000000001.answerKey.isVerified": [
        "The answer, explanation and evidence must be checked before publication."
      ]
    }
  }
}
```

The snippet shows only the additional editor fields. `isReady` concerns the saved content;
the frontend must also require the final review checkbox before enabling Publish. Error
keys use question **IDs**, so removing/reordering questions does not misplace inline errors.
The report collects all blockers in one response and does not change saved content.

`POST /api/admin/passages/{passageId}/publish` (`Admin_PublishPassage`) now takes JSON:

```json
{ "reviewConfirmed": true }
```

The server locks the passage and rechecks its current content before publication. It requires
at least one question, a key and complete explanation for every question, all question
verification checkboxes, and the final review confirmation. Any blocker returns 409 with
the complete field error map in ProblemDetails `errors`; a missing/false final confirmation
adds `reviewConfirmed` to that map. An empty JSON object therefore cannot publish a draft.
Missing or malformed JSON returns 400. A missing passage returns 404.

Success returns 204 and publishes the passage and every question in one transaction. A
repeat request for an already published passage returns 204 without changing its timestamp.
The final confirmation is required per publication request and is not stored as a reusable
draft flag. A minimum of 150 words, a maximum of 13 questions, the sample TFNG/completion
mix, and ONE WORD ONLY are **not enforced** in this release. `wordCount` counts nonempty
whitespace-separated tokens for display.

### Published read-only / Duplicate as draft

For Published and Archived details, `isReadOnly` is true and editing the passage, questions,
keys, explanations or verification returns 409. The existing DELETE passage route archives
a Published passage; it does not erase its historical content.

The design's Duplicate as draft button uses `POST /{passageId}/revisions` with `{}`. It returns
201 and the new draft's ID/Location. The text, questions, keys, variants and explanations are
copied with new IDs; verification is reset. The original remains published. Publishing the
revision archives the previous published version in the same transaction, preserving exams
and references to it. Only the latest previously published version can be revised; use
`canCreateRevision` to decide whether to show/enable the button. Archived versions follow
the same revision guard.

All mutations use `X-Requested-With: bandup` and `application/json`; send `{}` for bodyless
POST/DELETE actions. All question writes return 404 for a missing question or a question in
another passage, and 409 for a read-only parent.

The duration addition requires the `AddPassageAndWritingDurationMinutes` migration.
It adds a non-null duration_minutes column to content.passages, backfills existing passages
with 20, and enforces a positive value. The same migration adds the Writing duration column.

## Update time and migration

`AddPassageUpdatedAt` adds one nullable `content.passages.updated_at` column. No activity/log
table is introduced. Existing records keep null because their edit history is unknown;
the frontend should display an unknown date rather than invent one.

New passages and revisions receive an update time on save. Changes to the passage, its
questions or its answer keys refresh the parent timestamp in the same transaction, including
question deletion and publication/archive transitions. Reads and operations that make no
persisted change do not refresh it; failed commands roll back it along with the content.
Times come from TimeProvider and are stored in UTC at PostgreSQL microsecond precision.

Apply through the existing explicit migration command before deploying the updated API:

```powershell
dotnet run --project .\src\BandUp.Api -- --migrate
```
