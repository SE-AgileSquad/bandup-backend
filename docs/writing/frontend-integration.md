# Writing Task 2 frontend integration

Backend Master approved these six API contracts in Card 1. Card 2 owns runtime endpoint delivery and Card 3 owns real AI evaluation. Use the examples below as mocks during Card 1. Runtime availability depends on delivery of the corresponding cards.

## User flow

Sign in, choose an active, approved BandUp IELTS Academic Task 2 prompt, and write an essay of 100–350 words. Writing Mode A accepts a prompt ID and the learner's essay. Confirm the one-credit cost, submit, and follow the evaluation status to its final result. **My Essays** lists the learner's previous submissions and lets them reopen the original question and essay.

## Authentication and headers

All six endpoints require the browser-managed access cookie. For a separately configured frontend origin, use `credentials: 'include'`. The browser enforces the cookie's SameSite and Secure settings. For submission, send `Content-Type: application/json`, `X-Requested-With: bandup`, and a UUID in `Idempotency-Key`. GET requests need the access cookie and carry no request body. The GET routes are exempt from the CSRF header requirement.

On `401 UNAUTHORIZED`, follow the existing shared refresh flow and retry the protected request once if refresh succeeds. A submission retry must reuse the original key and body. A CSRF failure returns `400 VALIDATION_ERROR`. See the [browser session contract](../auth-client-contract.md).

## Approved endpoints and examples

The request lines below use the session settings above. All response bodies use camelCase and timestamps in UTC. Examples use synthetic data.

### 1. List approved prompts

`GET /api/writing/prompts` returns `200 OK` and the complete list of active, approved prompts ordered by ID ascending. An empty list is `{"items":[]}`.

~~~json
{
  "items": [
    {
      "id": "018fa524-bc4b-7e8a-8ee0-5a719b49e6b1",
      "title": "Public transport",
      "promptText": "Discuss the effect of public transport on congestion."
    }
  ]
}
~~~

### 2. Submit an essay

~~~http
POST /api/writing/submissions
Content-Type: application/json
X-Requested-With: bandup
Idempotency-Key: 38d3612b-5946-4120-8dce-1cbb32198ca6
~~~

~~~json
{
  "promptId": "018fa524-bc4b-7e8a-8ee0-5a719b49e6b1",
  "essayText": "Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion."
}
~~~

~~~http
HTTP/1.1 202 Accepted
Content-Type: application/json
Location: /api/writing/submissions/018fa525-5529-7770-8df2-b455acbf0ce9
~~~

~~~json
{
  "submissionId": "018fa525-5529-7770-8df2-b455acbf0ce9",
  "submittedAt": "2026-11-25T10:00:00+00:00",
  "status": "Queued"
}
~~~

An accepted submission costs **one credit**. The key is scoped to the signed-in learner and submission operation. Replay with the same key, prompt UUID and exact essay string returns the original receipt, even after evaluation finishes. A changed payload with an accepted key returns `409 IDEMPOTENCY_CONFLICT`. A rejection before acceptance leaves the key available.

For word counting, apply Unicode NFKC normalization and scan Unicode scalar values. A run of letters or numbers forms a word. Attached combining marks extend the run. A straight apostrophe, curly apostrophe or ASCII hyphen joins adjacent word parts when internal. Other punctuation, whitespace and formatting characters delimit words. For example, `don't` and `well-being` count as one each, while `3.5` and `word--word` count as two each. The bounds of 100 and 350 are inclusive. Send the original essay text.

### 3. List My Essays

`GET /api/writing/submissions?page=1&pageSize=20` returns `200 OK`. Defaults are page 1 and size 20. Page is at least 1 and size is clamped to 1–100. Rows and total count are scoped to the owner. Sort order is `submittedAt` descending, then `submissionId` descending. An empty page has an empty `items` array. Terminal rows include `completedAt`. Successful rows also include `estimatedOverallBand` and `scoreLabel`.

~~~json
{
  "items": [
    {
      "submissionId": "018fa525-5529-7770-8df2-b455acbf0ce9",
      "promptId": "018fa524-bc4b-7e8a-8ee0-5a719b49e6b1",
      "title": "Public transport",
      "status": "Succeeded",
      "submittedAt": "2026-11-25T10:00:00+00:00",
      "completedAt": "2026-11-25T10:00:00+00:00",
      "estimatedOverallBand": 6.5,
      "scoreLabel": "estimated"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
~~~

### 4. Read evaluation status

`GET /api/writing/submissions/018fa525-5529-7770-8df2-b455acbf0ce9` returns `200 OK` for the owner.

~~~json
{
  "submissionId": "018fa525-5529-7770-8df2-b455acbf0ce9",
  "submittedAt": "2026-11-25T10:00:00+00:00",
  "status": "Succeeded",
  "completedAt": "2026-11-25T10:00:00+00:00",
  "resultUrl": "/api/writing/submissions/018fa525-5529-7770-8df2-b455acbf0ce9/result"
}
~~~

| State | Meaning and response fields |
|---|---|
| `Queued` | Submission and charge accepted. The evaluation is waiting to start. |
| `Processing` | Evaluation is underway. |
| `Succeeded` | Terminal. Adds `completedAt` and `resultUrl`. |
| `Failed` | Terminal. Adds `completedAt` and `failure` with `EVALUATION_FAILED` and `creditRefunded: true`. The status request itself returns 200. |

An example terminal failure response follows.

~~~json
{
  "submissionId": "018fa525-5529-7770-8df2-b455acbf0ce9",
  "status": "Failed",
  "submittedAt": "2026-11-25T10:00:00+00:00",
  "completedAt": "2026-11-25T10:01:20+00:00",
  "failure": {
    "code": "EVALUATION_FAILED",
    "creditRefunded": true
  }
}
~~~

### 5. Read submission detail

`GET /api/writing/submissions/018fa525-5529-7770-8df2-b455acbf0ce9/detail` returns `200 OK` for the owner. My Essays titles and detail questions come from the immutable submission-time prompt snapshot, including after the prompt is edited or retired. Fetch detail when the learner opens a submission.

~~~json
{
  "submissionId": "018fa525-5529-7770-8df2-b455acbf0ce9",
  "essayText": "Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion. Public transport reduces congestion.",
  "promptSnapshot": {
    "id": "018fa524-bc4b-7e8a-8ee0-5a719b49e6b1",
    "title": "Public transport",
    "promptText": "Discuss the effect of public transport on congestion."
  }
}
~~~

### 6. Read successful feedback

`GET /api/writing/submissions/018fa525-5529-7770-8df2-b455acbf0ce9/result` returns `200 OK` for the owner after success. Feedback version `1.0` includes an estimated overall band, a summary and all four criteria. TR is Task Response, CC is Coherence and Cohesion, LR is Lexical Resource, and GRA is Grammatical Range and Accuracy. Scores are 0–9 in half-band steps. Each criterion carries an explanation, strengths, areas for improvement and at least one actionable recommendation. Strengths and improvement arrays can be empty. Optional `evidence` entries contain an `excerpt` and `observation`. Display the server-supplied scores and label the feedback and all scores as estimated. Render feedback strings as plain text. See the [public feedback schema](schemas/writing-feedback.public.v1.schema.json).

~~~json
{
  "submissionId": "018fa525-5529-7770-8df2-b455acbf0ce9",
  "completedAt": "2026-11-25T10:00:00+00:00",
  "feedback": {
    "schemaVersion": "1.0",
    "scoreLabel": "estimated",
    "estimatedOverallBand": 6.5,
    "summary": "Develop the claim with a concrete example.",
    "criteria": {
      "tr": {
        "score": 6,
        "explanation": "The position is explicit.",
        "strengths": [],
        "areasForImprovement": [],
        "recommendations": [
          "Explain how bus frequency changes travel choices."
        ]
      },
      "cc": {
        "score": 6.5,
        "explanation": "The same claim is repeated.",
        "strengths": [],
        "areasForImprovement": [],
        "recommendations": [
          "Develop a second paragraph with an example."
        ]
      },
      "lr": {
        "score": 6,
        "explanation": "Transport vocabulary fits the topic.",
        "strengths": [],
        "areasForImprovement": [],
        "recommendations": [
          "Describe a specific transport service."
        ]
      },
      "gra": {
        "score": 6.5,
        "explanation": "The sentence uses correct agreement.",
        "strengths": [],
        "areasForImprovement": [],
        "recommendations": [
          "Use a conditional clause to develop the claim."
        ]
      }
    }
  }
}
~~~

## Errors and credit handling

Errors use `application/problem+json`. Use `code` for branching and `traceId` for support.

| HTTP | Code | Condition |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Invalid submission input or CSRF failure. |
| 401 | `UNAUTHORIZED` | Session is missing or invalid. |
| 402 | `INSUFFICIENT_CREDIT` | Submission requires one credit and was not accepted. |
| 404 | `NOT_FOUND` | Prompt unavailable, or submission ID unknown or non-owned on status, detail or result. |
| 409 | `IDEMPOTENCY_CONFLICT` | Accepted key replayed with a different payload. |
| 409 | `EVALUATION_NOT_READY` | Result requested while Queued or Processing. |
| 409 | `EVALUATION_FAILED` | Result requested after terminal failure. |

An insufficient-credit example follows.

~~~json
{
  "type": "https://bandup.app/errors/insufficient-credit",
  "title": "Payment Required",
  "status": 402,
  "detail": "One credit is required for this evaluation.",
  "instance": "/api/writing/submissions",
  "code": "INSUFFICIENT_CREDIT",
  "traceId": "writing-request-42"
}
~~~

An example response when the evaluation is still pending follows.

~~~json
{
  "type": "https://bandup.app/errors/evaluation-not-ready",
  "title": "Conflict",
  "status": 409,
  "detail": "The evaluation result is not ready.",
  "instance": "/api/writing/submissions/018fa525-5529-7770-8df2-b455acbf0ce9/result",
  "code": "EVALUATION_NOT_READY",
  "traceId": "writing-request-42"
}
~~~

## UI recommendations and checklist

These recommendations are non-binding. Back off between status polls and allow one poll at a time. Stop status polling after either terminal state, when leaving the screen, or when the session cannot be recovered. Resume later from My Essays or the stored submission URL. A transient network error can back off and retry. A slow queue is still pending until the server reports a terminal state.

- [ ] Show loading and empty states for prompts and My Essays. Preview the word count and one-credit cost, and disable duplicate submit actions while a request is in flight.
- [ ] Preserve the draft if submission fails. After a timeout, reuse the same key and payload to recover the original receipt. After a known terminal failure, an intentional new evaluation uses a new key and costs one credit if accepted.
- [ ] Show waiting or evaluating for Queued and Processing. On Succeeded follow `resultUrl` and show the estimated feedback. On Failed show the failure and refund from `creditRefunded`.
- [ ] Keep the essay visible on a 402. Show that a credit is required before another submission can be accepted.
- [ ] Test owner-only reads, empty pages, reopening historical prompts, refresh and safe replay, each lifecycle state, and each distinct 409 code against mocks.

## Card 2 input limits for runtime integration

The approved POST limit is 64 KiB (65,536 bytes) for the entire JSON body, including JSON escapes. The future HTTP endpoint returns 413 for a larger body. The decoded essay may contain at most 8,000 Unicode scalar values and must contain 100–350 words inclusive. Invalid essay content, including malformed surrogate sequences and U+0000, returns 400. Count supplementary characters as one scalar. Preserve the original string when retrying. These limits are the Frontend handoff for runtime integration. Public submission stays disabled until the real evaluation consumer is ready.
