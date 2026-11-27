# Admin writing prompts API

Scope: manage the Task 2 prompts shown in the admin Writing Prompts page. All routes require
an active Admin or SuperAdmin, checked against the current database role. Anonymous or
inactive accounts receive 401; ordinary users receive 403. Mutations require the existing
`X-Requested-With: bandup` header and JSON content type. Use `{}` for bodyless actions.

## List and card actions

`GET /api/admin/writing-prompts?page=1&pageSize=4&status=Draft&search=remote`

- page defaults to 1 and is clamped to at least 1; pageSize defaults to 20 and is clamped to 1–100.
- status is optional: Draft, Published, Archived. Omit it for All.
- search is a trimmed, case-insensitive title substring, maximum 320 characters. Blank means no filter.
- Filters combine with AND before counting/paging. Invalid filters return 400. An out-of-range
  page returns empty items with the filtered totalCount. Order: updatedAt descending, title, ID.
- Returns `{ items, page, pageSize, totalCount }`. Each item has id, title, essayType, minWords,
  maxWords, status, updatedAt and taskNumber (always 2). The list omits taskText.
- Draft cards use Edit draft, other cards View details; both load the same detail GET route.
  The illustration is frontend presentation.

## Routes

All paths below start with `/api/admin/writing-prompts`.

| Method / suffix | Endpoint name | Result |
|---|---|---|
| GET collection | Admin_ListWritingPrompts | Paged cards, 200 |
| POST collection | Admin_CreateWritingPrompt | Draft ID and Location, 201 |
| GET /{promptId} | Admin_GetWritingPrompt | Full details, 200; missing 404 |
| PUT /{promptId} | Admin_EditWritingPrompt | Save a draft, 204; missing 404; read-only 409 |
| POST /{promptId}/publish | Admin_PublishWritingPrompt | Publish Draft, 204; repeat Published 204; Archived 409 |
| POST /{promptId}/archive | Admin_ArchiveWritingPrompt | Archive Published, 204; repeat Archived 204; Draft 409 |
| POST /{promptId}/duplicate | Admin_DuplicateWritingPrompt | Independent new Draft ID and Location, 201 |
| DELETE /{promptId} | Admin_DeleteWritingPrompt | Delete Draft or archive Published, 204; repeat Archived 204 |

Every action on a missing prompt returns 404. Expected errors use ProblemDetails with code,
traceId and, for input validation, field-keyed errors. A referenced Draft cannot be deleted
and returns 409 (the same foreign-key protection used for other content).

Create/edit JSON:

```json
{
  "title": "Remote work and productivity",
  "taskText": "Some people believe remote work improves productivity. To what extent do you agree or disagree?",
  "essayType": "Opinion essay",
  "minWords": 100,
  "maxWords": 350
}
```

Title and taskText are required and nonblank. Title allows up to 320 characters. essayType
is a nonblank display label up to 100 characters; it is not a closed enumeration of essay
types. If omitted it defaults to Opinion essay. minWords/maxWords default to 100/350 as in
the supplied design. minWords must be positive, maxWords must be at least minWords. These
fields remain configurable on Draft create/edit for future use; publication currently requires
exactly minWords = 100 and maxWords = 350. This admin feature does not implement
Essay submission or its word-count validation. PUT replaces all editable fields, including defaults for omitted
optional fields. Text values are trimmed; saving identical normalized values preserves updatedAt.

The server supplies Draft status, creator and timestamps. taskNumber is fixed to 2 and is
not accepted as a configurable task setting. Writing Task 1 is outside the current scope.
Three prompts is the FS content-delivery target, not a database or API limit; no sample prompts
are automatically inserted into the development database.

Details additionally include taskText, createdBy, createdAt, publishedAt and isReadOnly.
GET details also supplies the admin preview. Only Draft is editable. Published means the admin
has approved the question content and permitted its use by learners. Calling Publish is that
approval action; no additional approval flag or AI-generated answer is required. Draft and
Archived prompts are not eligible for new learner use. Consuming learner features should
select only Published prompts; this change adds no learner endpoints.

Publish validates the saved prompt under its row lock and requires exactly minWords = 100
and maxWords = 350. A mismatch returns 409 CONFLICT with an explanatory message; status,
publishedAt, updatedAt and the configured word limits stay unchanged. Correct the Draft via
PUT, then publish again. The same limits are checked on repeat Publish requests, including
legacy Published records; existing data is not automatically rewritten. Valid repeated
publish/archive operations do not change timestamps. All mutations commit through the existing
transaction pipeline. Archived prompts cannot be republished or edited.

For editing published content, duplicate it, then edit/publish the copy. Duplication works
from any status, retains the editable fields, assigns a new ID and creator/current timestamps,
and resets PublishedAt and status. It does not automatically archive or replace the source.
This is an independent copy, not a version chain. Published/Archived originals are retained
by DELETE to preserve future Essay references. No learner API, Essay evaluation, RabbitMQ job,
credit charge, or AI provider is implemented by this admin feature.

## Database change

Migration `AddWritingPrompts` creates schema `writing` and table `writing_prompts` with its
creator FK, status and word-range CHECK constraints and status/update-time index. It does
not change existing lesson, passage, identity or billing data. Rolling it back drops the
new table and its prompt data. The schema itself may remain after rollback.

The migration has been generated but not applied to the development database. Apply it in
Package Manager Console from the repository directory:

```powershell
Set-Location C:\Git\bandup-backend
dotnet run --project .\src\BandUp.Api -- --migrate
```
