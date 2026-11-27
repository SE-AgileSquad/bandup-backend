# Admin lesson mappings API

Scope: the admin Lesson mapping page's default lesson for each supported Reading question
type. This implements the FS's simple question-type-to-lesson mapping. Error-pattern-specific
rules and learner progress are outside this admin page.

Both routes require a currently active Admin or SuperAdmin. Anonymous and inactive accounts
receive 401; ordinary users receive 403. PUT requires JSON and `X-Requested-With: bandup`.

## Load the page

`GET /api/admin/lesson-mappings` — endpoint name `Admin_GetLessonMappings`, 200.

Returns an array with one row per supported question type, including unconfigured types:

```json
[
  {
    "questionType": "TrueFalseNotGiven",
    "selectedLesson": {
      "id": "019b0000-0000-7000-8000-000000000001",
      "title": "Not Given vs False",
      "status": "Published"
    },
    "options": [
      { "id": "019b0000-0000-7000-8000-000000000001", "title": "Not Given vs False" }
    ],
    "isEnabled": true
  },
  {
    "questionType": "SentenceCompletion",
    "selectedLesson": null,
    "options": [],
    "isEnabled": false
  }
]
```

Options contain every Published lesson of that row's question type, ordered by lesson sort
order, title, then ID. No pagination is needed for these dropdowns. If there are no eligible
lessons, options is empty. A missing mapping has selectedLesson null and isEnabled false.
An existing mapping to an Archived (or legacy Draft) lesson remains visible with its status,
but isEnabled is false and that lesson is absent from options. The existing learner query
already ignores unpublished lessons; archiving does not silently select a replacement.

The page's example question numbers and preview sentence are presentation copy, not actual
learner mistakes or an additional backend setting. The frontend can link selectedLesson.id
to the existing admin lesson details endpoint.

## Save both selections

`PUT /api/admin/lesson-mappings` — endpoint name `Admin_SaveLessonMappings`, 204.

```json
{
  "mappings": [
    { "questionType": "TrueFalseNotGiven", "lessonId": "019b0000-0000-7000-8000-000000000001" },
    { "questionType": "SentenceCompletion", "lessonId": null }
  ]
}
```

Supply both supported types exactly once, in either order. null explicitly clears that type's
default. Empty GUIDs, omitted/null arrays, unknown types, duplicate types and incomplete
sets return 400 with ProblemDetails. Each non-null lesson must exist, be Published and have
the corresponding question type. Invalid lesson selections return 400 with field errors
such as `mappings[1].lessonId`; neither selection is changed.

Saving creates or updates the existing default rows (`error_pattern IS NULL`) in
`learning.lesson_map`; clearing removes only that default row. Other error-pattern mappings
remain unchanged. Repeating a save does not create duplicates or change mapping IDs.
All selections commit atomically. An advisory transaction lock serializes concurrent saves,
including initial creation. Selected lesson rows are locked in ID order, using the same row
locks as admin lesson editing/archiving. Concurrent valid saves use last-committed-save wins
for the complete pair; this page does not implement optimistic revision checks.

## Database

No new table, column, migration, seed or development-database update is required. The existing
unique index on question type and error pattern (NULLS NOT DISTINCT), foreign key and learner
default-lesson query are reused. This feature does not modify learner endpoints.
