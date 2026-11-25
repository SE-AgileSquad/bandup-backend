# Admin lessons API

All routes require an active `Admin` or `SuperAdmin`. Anonymous/invalid/inactive sessions
receive 401; ordinary active users receive 403. The existing live database permission checks,
cookies and CSRF protection apply.

## Lesson list — `Admin_ListLessons`

`GET /api/admin/lessons?page=1&pageSize=4&status=Draft&search=given&questionType=TrueFalseNotGiven`

| Parameter | Contract |
|---|---|
| `page` | Default 1; clamped to at least 1. |
| `pageSize` | Default 20; clamped to 1–100. Send 4 to match the design. |
| `status` | Optional: `Draft`, `Published`, `Archived`. Omit for the All tab. |
| `search` | Optional case-insensitive substring of the title, trimmed; maximum 320 characters. |
| `questionType` | Optional: `TrueFalseNotGiven` or `SentenceCompletion`. |

Search, status and question type combine with AND and apply before counting and pagination. Blank search
does not filter. Unknown status values and overlong search return 400. Pages beyond the end
return an empty `items` array with the filtered `totalCount`. Ordering is `sortOrder`, then
title, then ID for a stable result. The same ordering applies to all status tabs.

```json
{
  "items": [
    {
      "id": "019aef00-0000-7000-8000-000000000001",
      "title": "Not Given vs False",
      "questionType": "TrueFalseNotGiven",
      "sortOrder": 1,
      "status": "Draft",
      "exampleCount": 2,
      "hasPractice": true
    }
  ],
  "page": 1,
  "pageSize": 4,
  "totalCount": 1
}
```

`exampleCount` counts the lesson's current educational examples. As agreed for this screen,
`hasPractice` is true exactly when at least one example exists; the frontend uses it for
Practice included. No separate practice-content model or database flag is introduced. The
list omits the lesson body and example text; those are loaded through the detail endpoint.

Reading strategy and the illustration are frontend presentation for the current lessons.
Estimated reading time is intentionally deferred: no duration field, calculation or database
column is added, and the frontend should omit the minutes shown in the mockup for now.

## Card actions and new lesson — existing routes

- `POST /api/admin/lessons` (`Admin_CreateLesson`): creates a Draft and returns 201 with
  `{ "id": "..." }` and a Location header. Requires title, body, a supported `questionType`
  (`TrueFalseNotGiven` or `SentenceCompletion`); accepts `sortOrder` (default 0).
- `GET /api/admin/lessons/{lessonId}` (`Admin_GetLesson`): returns title, body, type, status,
  order, ordered examples, `exampleCount`, `hasPractice`, `isReadOnly`, `canChangeQuestionType`
  and `publishReadiness`, including Draft/Archived content. Returns 404
  when missing. Both Edit draft and View details load this same endpoint.
- `PUT /api/admin/lessons/{lessonId}` (`Admin_EditLesson`): the existing edit contract
  `{ "title": "...", "body": "...", "sortOrder": 1, "questionType": "SentenceCompletion" }`;
  returns 204. Omit `questionType` or send null to retain the current type.
- `POST /api/admin/lessons/{lessonId}/publish` (`Admin_PublishLesson`): publication requires
  at least one example. Returns 204; a repeated call for Published is a no-op. Archived
  lessons cannot be published through this action and return 409.

Draft cards show Edit draft; Published and Archived cards show View details. This list
and editor use the same detail route.

## Lesson editor and educational questions/examples

The FS describes educational examples with an evidence sentence and its paraphrase. These
are the lesson's teaching/practice items in this release, served by the existing `examples`
resource. They are distinct from passage exam questions and the standalone Paraphrase Trainer.
No separate lesson-question table, answer-key model or learner practice execution is added.

The editor can create a draft, save its text, manage examples and their order, inspect
publication readiness, publish, archive and delete an unreferenced lesson. All APIs require
the same Admin/SuperAdmin policy. Lesson mapping and learner progress APIs are outside this change.

### Save lesson details

Create a lesson with `POST /api/admin/lessons`:

```json
{
  "title": "False vs Not Given",
  "body": "Compare the claim with the source text...",
  "questionType": "TrueFalseNotGiven",
  "sortOrder": 1
}
```

The initial status is Draft. The title and body must be nonblank; the question type is
required on create. A draft may have zero examples and be saved while its content is prepared.
The body is stored and returned as supplied; rendering it safely is the frontend's responsibility.

Use PUT with the same text and order fields to save edits. An explicitly supplied different
question type is allowed only for Draft and only when no mapping references the lesson. This
guard protects existing references; it does not add or change any mappings. The type of a
Published/Archived lesson is fixed. Sending the same type is permitted wherever editing is
permitted. Forbidden state transitions return 409; unsupported enum values return 400.

Draft and Published content can be edited. Archived content, including its examples and
their order, is read-only and writes return 409. This release keeps direct editing of
Published lessons; it does not introduce passage-style revision/version management.

### Manage examples

Use `/api/admin/lessons/{lessonId}/examples` as the collection path.

| Method and suffix | Contract |
|---|---|
| `GET` collection — `Admin_ListLessonExamples` | Returns the ordered array of example details; an empty lesson returns `[]`, a missing lesson returns 404. |
| `POST` collection — `Admin_CreateLessonExample` | Creates an example; returns 201 `{ "id": "..." }` and a Location header. |
| `GET /{exampleId}` — `Admin_GetLessonExample` | Returns the example's ID, evidence sentence, paraphrase and order. |
| `PUT /{exampleId}` — `Admin_EditLessonExample` | Replaces the example fields; returns 204. |
| `DELETE /{exampleId}` — `Admin_DeleteLessonExample` | Removes an example; returns 204. The last Published example is protected with 409. |
| `PUT /order` — `Admin_ReorderLessonExamples` | Atomically reorders every example in the lesson; returns 204. |

Create/edit example JSON:

```json
{
  "evidenceSentence": "Some birds nest on tall buildings.",
  "paraphrase": "High-rise nesting is common among some birds.",
  "sortOrder": 1
}
```

Both text fields must be nonblank, including on drafts; the API reports validation errors
by field. Example detail GET, edit and delete return 404 when an ID belongs to another lesson,
so an example cannot be manipulated through a different parent. Reads work for every lesson status.
Examples are returned by sortOrder, then ID to resolve ties.

Reorder JSON:

```json
{ "exampleIds": ["019aef00-0000-7000-8000-000000000002", "019aef00-0000-7000-8000-000000000001"] }
```

Supply every current example exactly once, in the desired order. The server assigns
sortOrder 1..N in one transaction while holding the lesson lock. Null, duplicate, empty GUID,
missing or foreign IDs return 400 with `errors.exampleIds`, leaving every order unchanged.
An empty array is accepted only when the lesson has no examples. Archived lessons return 409.

### Publication readiness and preview

The existing GET detail response includes the complete text and ordered examples, so it
also supplies an admin preview without another preview-specific endpoint. Additional fields:

```json
{
  "exampleCount": 0,
  "hasPractice": false,
  "isReadOnly": false,
  "canChangeQuestionType": true,
  "publishReadiness": {
    "isReady": false,
    "errors": {
      "examples": ["Add at least one example before publishing the lesson."]
    }
  }
}
```

This snippet contains only the additional editor fields. Readiness requires Draft status,
a nonblank title/body and at least one complete example. Example-specific errors use stable
keys such as `examples.{exampleId}.paraphrase`. The publish command locks the lesson and
rechecks readiness; a blocked publish returns 409 with the same field map in ProblemDetails
`errors`, without changing status or content. Published/Archived readiness is false because
they cannot transition from Draft to Published; repeated publish requests on Published still
return 204. No final-review checkbox or manual per-example verification is required for lessons.

## Archive lesson — `Admin_ArchiveLesson`

`POST /api/admin/lessons/{lessonId}/archive` with `{}`.

- Published becomes Archived and returns 204. Repeating it for Archived also returns 204.
- A draft returns 409; a missing lesson returns 404.
- The command locks the lesson and changes its status in the existing transaction pipeline.
  It keeps the original lesson, examples, progress and mappings.
- Learner lesson lists/details and default lesson recommendations only expose Published
  lessons, so an archived lesson disappears from those surfaces. Admins can still view it
  and find it through the Archived status filter.

The existing `DELETE /api/admin/lessons/{lessonId}` remains permanent deletion, with 409 when
learner progress or mappings reference the lesson. Use the archive action to retain history.

All mutations require `X-Requested-With: bandup` and JSON content; send `{}` for actions
without input fields. The dashboard's total lesson count continues to include all statuses.

## Migration

`AddLessonArchivedStatus` expands the existing `learning.lessons` status CHECK constraint
to allow `Archived`. No columns or tables are added and existing rows keep their status.
The migration files are generated; apply them explicitly before using the updated API:

```powershell
Set-Location C:\Git\bandup-backend
dotnet run --project .\src\BandUp.Api -- --migrate
```

The migration has not been applied to the development database by this change. Rolling it
back requires resolving any Archived rows before restoring the old status constraint.

The lesson-editor completion adds only API/domain rules and derived response fields. It
introduces no additional database changes or migration beyond `AddLessonArchivedStatus`.
