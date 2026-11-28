# Writing operations

## Deployment

Deploy the API and Worker against the same PostgreSQL database and RabbitMQ virtual host. Configure the existing database, broker and cache settings. The API declares RabbitMQ topology at startup. The Worker reconnects during temporary broker outages and can continue PostgreSQL-only expiration. The publisher and expiration service run independently of the POST flag. Keep `Features:WritingSubmissionEnabled` false until the Card 3 consumer and recovery are ready.

Coordinate migration order with development per [ADR 0015](../adr/0015-module-schemas-and-sequential-migrations.md). Review and apply the committed migrations through the existing API `--migrate` mode before starting the new services. For environments with historical Writing ledger entries, reconcile each charge by submission and owner against refunds and persisted state. Confirm reserved capacity equals the count of outstanding Queued and Processing submissions per account. Resolve discrepancies before activation. A charge entry alone does not establish that a submission remains refundable.

The Admin catalog is authoritative. Publish three approved Task 2 prompts with the 100–350 word bounds through the Admin workflow. Provisioning the approved catalog remains an outstanding content task.

## Acceptance and settlement

Acceptance locks the credit account before the eligible prompt. One transaction saves the Queued submission, original essay, immutable prompt snapshot, one-credit debit and one refund reservation. The account must keep both balance and reserved capacity nonnegative, and their sum must fit in a signed 32-bit integer. A positive Admin adjustment that would consume reserved capacity is rejected in full. The reservation count must match the account's outstanding submissions before a new acceptance or settlement.

Settlement locks the account before the submission and verifies the original charge and settlement eligibility. Success releases one reserved unit while the credit remains spent. Terminal failure or expiration credits one unit, records the refund and releases the reservation in the same transaction. Repeated settlement preserves the stored outcome.

For PostgreSQL SQLSTATE 23505, recovery applies only to `ix_writing_submissions_user_id_idempotency_key` on `writing.writing_submissions`. After successful rollback and disposal, the API uses a fresh scope and DbContext to read the committed row by authenticated owner and key. The same prompt ID and ordinal essay text return the original 202 receipt and Location. A different payload returns `409 IDEMPOTENCY_CONFLICT`. A missing row preserves the failure. The API never retries the financial command or treats an uncertain commit as a replay.

## Publication and expiration

PostgreSQL retains the publication state across restarts. The Worker leases one eligible Queued submission at a time with `FOR UPDATE SKIP LOCKED` and commits the attempt before broker I/O. It sends persistent `WritingEvaluationRequestedV1` messages containing only `submissionId`, with a fresh message ID per attempt. The durable direct exchange is `bandup.jobs`, the durable queue is `bandup.writing.evaluation.v1`, and the routing key is `writing.evaluation.requested.v1`.

Publishing is mandatory and uses publisher confirms. Outcome commands require the current lease ID, an unexpired lease and a still-Queued unpublished row before its deadline. Failure or an uncertain outcome schedules a retry. Failed outcome writes recover via lease expiry. Duplicate delivery remains possible when a send reaches RabbitMQ without a recorded confirmation. An exhausted submission remains Queued until expiration.

Expiration scans in bounded batches and rechecks eligibility under settlement locks. At or after the deadline, still-Queued submissions expire even if publication was confirmed, attempts are exhausted or a lease is active. The resulting Failed state and one-credit refund commit atomically. Processing is outside queue expiration.

| Configuration key | Default |
|---|---|
| `Features:WritingSubmissionEnabled` | false |
| `Writing:Acceptance:QueueTimeout` | 5 minutes |
| `Writing:Publishing:PollInterval` | 1 second |
| `Writing:Publishing:BatchSize` | 10 |
| `Writing:Publishing:MaxAttempts` | 5 |
| `Writing:Publishing:PublishTimeout` | 5 seconds |
| `Writing:Publishing:LeaseDuration` | 30 seconds |
| `Writing:Publishing:InitialRetryDelay` | 1 second |
| `Writing:Publishing:BackoffMultiplier` | 2 |
| `Writing:Publishing:ExpirationPollInterval` | 1 second |
| `Writing:Publishing:ExpirationBatchSize` | 10 |

Publish timeout must be shorter than the lease. Default retry delays are 1, 2, 4 and 8 seconds. Configured exponential delays are capped at one hour.

## Privacy and Card 3

Writing operational logs retain safe failure categories and route templates. Private Writing scopes suppress unsafe diagnostics and filter Sentry events, breadcrumbs, transactions, logs and metrics. API Sentry body capture is disabled, and request-path filtering covers captures after the private scope unwinds.

Redis check/handle/mark provides best-effort deduplication. Card 3 must enforce database-backed idempotency by SubmissionId, processing-attempt fencing, Processing recovery, cancellation and bounded execution, and implement the real evaluation consumer and LLM integration. The publication lease fences publication bookkeeping only. Card 2 does not run real-model evaluation.
