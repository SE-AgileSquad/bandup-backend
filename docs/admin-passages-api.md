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
      "updatedAt": "2026-11-22T12:00:00Z"
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
  "usageBasis": "Original content"
}
```

All four fields are required and nonblank. Returns 201 with `{ "id": "..." }` and a Location
pointing to the detail route. The server supplies the creator, initial Draft status and
version 1. A new passage has zero questions until they are added separately.

## Edit draft / View details — existing routes

- `GET /api/admin/passages/{passageId}` (`Admin_GetPassage`): the full passage, questions,
  explanations and answer keys. Details now also include `updatedAt`. Returns 404 if missing.
- `PUT /api/admin/passages/{passageId}` (`Admin_EditPassage`): the same four fields as create;
  returns 204. Only drafts are editable. Published or archived content returns 409.
- `POST /api/admin/passages/{passageId}/revisions` (`Admin_CreatePassageRevision`): creates a
  draft revision of the latest previously published version. Existing published content stays
  available until its replacement is published. Copied answer keys must be verified again.

For the design's primary card action, use status Draft to display Edit draft; for Published
or Archived, display View details. Both load the same GET detail endpoint.

Mutations use the existing `X-Requested-With: bandup` header and `application/json` content
type. Publication, archive/delete, questions and answer-key routes retain their existing
contracts and draft/verification guards.

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
