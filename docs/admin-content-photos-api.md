# Admin content photos

Each Passage, Writing Prompt and Lesson has one optional image. Existing content starts with no
photo. This feature is scoped to the admin panel; it does not add learner media routes.

## Routes

| Method | Passage | Writing Prompt | Result |
|---|---|---|---|
| POST | `/api/admin/passages/{passageId}/photo` | `/api/admin/writing-prompts/{promptId}/photo` | Upload or replace, 200 |
| GET | `/api/admin/passages/{passageId}/photo` | `/api/admin/writing-prompts/{promptId}/photo` | Image bytes, 200 |
| DELETE | `/api/admin/passages/{passageId}/photo` | `/api/admin/writing-prompts/{promptId}/photo` | Remove association, 204 |

Lessons use `POST`, `GET` and `DELETE /api/admin/lessons/{lessonId}/photo` with the same
upload, download and removal contracts (`Admin_UploadLessonPhoto`, `Admin_GetLessonPhoto`,
`Admin_DeleteLessonPhoto`). Swagger exposes the required UUID path parameter on every route.

All nine routes require the existing session cookie and an active Admin or SuperAdmin,
checked against the current database role. Anonymous/inactive accounts receive 401; ordinary
users receive 403. For Passages and Writing Prompts, POST/DELETE allow Draft only and
return 409 for Published or Archived. Lesson photos follow the existing Lesson editor:
Draft and Published are editable; Archived returns 409. Photos remain optional.
GET permits all statuses for admins, returns 404 for absent content/photo, and sets
`Cache-Control: private, no-store` and `X-Content-Type-Options: nosniff`.

POST uses **multipart/form-data**, with exactly the image file in field **file**. Send
`X-Requested-With: bandup`; let the browser set Content-Type and its boundary. This multipart
exception is limited to these three upload endpoints. Other authenticated mutations still
require JSON and the same custom header. DELETE uses an empty JSON body `{}` as elsewhere.
The development Swagger UI adds the custom header for multipart uploads too.

Create the Draft with the existing JSON API first, then POST its photo. Repeating POST
replaces its photo; it is not an idempotent upload. Repeating DELETE on editable content
without a photo succeeds. Editing the content's normal JSON fields preserves the saved photo.

```javascript
const body = new FormData();
body.append('file', selectedFile);
const response = await fetch(`${apiBase}/api/admin/writing-prompts/${promptId}/photo`, {
  method: 'POST', credentials: 'include',
  headers: { 'X-Requested-With': 'bandup' }, body
});
```

Successful POST returns this object, also available as `photo` in both list items and
details. `photo` is null when absent. `sizeBytes` is the original uploaded file size,
before Cloudinary processing. Do not use it as the download Content-Length.

```json
{
  "id": "019b0000-0000-7000-8000-000000000001",
  "url": "/api/admin/writing-prompts/019b0000-0000-7000-8000-000000000002/photo",
  "contentType": "image/png",
  "sizeBytes": 83240
}
```

Resolve the relative URL against the API origin. GET needs the admin session cookie;
for a separate frontend origin, use `fetch` with `credentials: 'include'`, create a blob
URL for display and revoke it when no longer used. The API never returns storage keys,
credentials or signed Cloudinary delivery URLs. Photos are uploaded with Cloudinary's
authenticated delivery type; the backend signs and fetches the image after authorizing GET.
See [Cloudinary access control](https://cloudinary.com/documentation/control_access_to_media).

## Validation and errors

- JPEG, PNG and WebP only, at most 5 MiB (5,242,880 bytes). Filename and claimed MIME type
  are not trusted; file signatures and dimensions are checked, then Cloudinary decodes it.
- At most 10,000 pixels per side and 24 megapixels. Images are resized to fit within
  1600 × 1600, preserving aspect ratio without upscaling; metadata profiles are stripped.
- Empty, unsupported or out-of-bounds images return 400 with a `file` validation error.
  Cloudinary rejecting a corrupt file after preliminary validation returns 502.
- Whole multipart requests are capped at 5 MiB + 128 KiB overhead; oversized requests
  return 413. A streaming request rejected by the server may use its own 413 response.
- Missing owner returns 404. Storage/configuration/network errors return 502
  `IMAGE_STORAGE_UNAVAILABLE` with a safe message; provider details are not exposed.
- For Passages and Writing Prompts, a changed photo refreshes updatedAt. Removing an
  absent photo preserves updatedAt. Lessons do not have an updatedAt field; this addition
  does not introduce one.
- Photos remain optional at publication. Passage revisions and Writing duplicates retain
  the same immutable image; replacing/removing one owner's photo does not affect the other.

## Storage lifecycle

The database stores metadata in `media.images` and nullable `photo_id` foreign keys on
Passages, Writing Prompts and Lessons. Actual image bytes live in Cloudinary, not PostgreSQL.
Before upload, a reservation is committed in its own database scope. The existing command
transaction links the new photo only after successful upload. A failed upload or save
leaves the previous link intact and a durable reservation for cleanup; this also handles
process interruption or an uncertain outer transaction commit.

A background worker checks every five minutes for unreferenced reservations/images older
than one hour. It locks and rechecks references in all three content tables before removing the
Cloudinary object and database record. Failed deletion retains the record for retry. Old
photos are therefore deleted asynchronously, not immediately. Referenced images, including
archived and shared photos, are retained. Cleanup requires a running host with working
Cloudinary configuration. The delay also protects in-flight uploads, whose provider timeout
is 30 seconds. A provider object missing during cleanup is treated as already deleted.

## Local configuration and migration

Credentials belong in User Secrets for the API project (or environment variables in
deployment). Never commit real values. Configure:

```text
Cloudinary:CloudName
Cloudinary:ApiKey
Cloudinary:ApiSecret
```

Environment variable equivalents are `Cloudinary__CloudName`, `Cloudinary__ApiKey` and
`Cloudinary__ApiSecret`. Missing credentials do not prevent the API/worker from starting;
photo operations return 502 and cleanup is disabled when CloudName is absent.

The `AddContentPhotos` migration adds the media table, nullable photo foreign keys and
indexes. The subsequent `AddLessonPhoto` migration adds nullable `learning.lessons.photo_id`
with its foreign key and index, reusing the existing media table. Neither migration rewrites
existing content or requires an image for it. Apply pending migrations from
the repository root, including in Visual Studio Package Manager Console:

```powershell
dotnet run --project .\src\BandUp.Api -- --migrate
```

The implementation does not run this command against the developer's database automatically.
