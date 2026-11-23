# Admin lessons API

All routes require an active `Admin` or `SuperAdmin`. Anonymous/invalid/inactive sessions
receive 401; ordinary active users receive 403. The existing live database permission checks,
cookies and CSRF protection apply.

## Lesson list — `Admin_ListLessons`

`GET /api/admin/lessons?page=1&pageSize=4&status=Draft&search=given`

| Parameter | Contract |
|---|---|
| `page` | Default 1; clamped to at least 1. |
| `pageSize` | Default 20; clamped to 1–100. Send 4 to match the design. |
| `status` | Optional: `Draft`, `Published`, `Archived`. Omit for the All tab. |
| `search` | Optional case-insensitive substring of the title, trimmed; maximum 320 characters. |

Search and status combine with AND and apply before counting and pagination. Blank search
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
  order, ordered examples and `hasPractice`, including Draft/Archived content. Returns 404
  when missing. Both Edit draft and View details load this same endpoint.
- `PUT /api/admin/lessons/{lessonId}` (`Admin_EditLesson`): the existing edit contract
  `{ "title": "...", "body": "...", "sortOrder": 1 }`; returns 204.
- `POST /api/admin/lessons/{lessonId}/publish` (`Admin_PublishLesson`): publication requires
  at least one example. Returns 204; a repeated call for Published is a no-op. Archived
  lessons cannot be published through this action and return 409.

Draft cards show Edit draft; Published and Archived cards show View details. This list
change retains the existing lesson editor behavior; the lesson editor's detailed design can
be handled separately.

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
