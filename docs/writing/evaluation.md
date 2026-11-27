# Offline Writing evaluation

Run from the repository root using Python 3.12. Install the validator once where dependencies are available. Evaluation itself is offline. The CLI uses the Python convention of the existing preparation kit and registers the bundled Draft 2020-12 schemas locally.

```powershell
python -m pip install -r tools/writing/requirements.txt
python -B -m unittest discover -s tools/writing -v
python -B tools/writing/evaluate.py proxy tools/writing/examples/references.json tools/writing/examples/outputs.json
```

For the proposed Writing CI job, install the requirements and run the unittest discovery command above. Its CLI fixture test invokes the evaluator and asserts the JSON counters, denominators and numerical error metrics. A completed evaluation may exit successfully with poor scores. Shared workflow integration awaits approval.

Pass a JSON reference array and a recorded-output array. Use the prepared development, validation or test JSON directly in proxy mode. In human mode use a separately reviewed copy in the human-review-pending.json format with human_reference_scores. Preserve the original pending batch. Each reference needs id, essay and four matching criterion labels (uppercase or lowercase). Each row must have exactly one mode-specific score source. Criterion names are case-insensitive and unique after normalization. The optional overall label is ignored. Null human scores keep the automated gate pending. A recording needs id and output containing the original raw JSON **string**, or id and failed set to true. Omit absent recordings. Duplicate IDs, unknown output IDs and malformed input envelopes abort the run. Raw output strings preserve malformed JSON and duplicate keys for assessment. Keep reference files, recordings and essay text private.

```powershell
python -B tools/writing/evaluate.py proxy local-reference/Writing-Evaluation-Dataset/processed/test.json private-recordings.json > proxy-report.json
python -B tools/writing/evaluate.py human private-human-references.json private-recordings-for-ten.json > human-report.json
```

JSON goes to stdout and a summary to stderr. The CLI reports measurements even when a gate fails. Consumers should read automated_gate. The existing json_validity_rate equals schema_valid divided by all expected essays. It includes schema validation after strict JSON parsing (including duplicate-key rejection). The invalid count combines parsing and schema failures. No syntax-only rate is emitted. schema_valid counts syntactically valid outputs satisfying the schema. counts.valid additionally requires matching evidence excerpts. Missing, failed, invalid and invalid_evidence records contribute zero passing criteria. Accuracy divides passes by all expected scores. MAE and mean signed error (model minus reference) use only available matched scores, reporting error_coverage alongside denominator. No coverage yields null errors. Per-criterion results use one expected score per essay.

## Dataset and acceptance

The Release build of WritingEssayWordCount.Count matched word_count for all 100 prepared essays. Development spans 250–347 words, validation 253–348 and held-out test 250–343. All satisfy the inclusive 100–350 policy.

The prepared chillies/ielts-writing-task2-essays snapshot on Hugging Face contains Writing9 proxy labels. The preparation report records 8,049 source records and 6,224 eligible. Preserve 60 development, 20 validation and 20 held-out essays. Ten blind candidates come from held-out. Map task_achievement_band to TR, coherence_cohesion_band to CC, lexical_resource_band to LR and grammatical_range_band to GRA. Essay IDs and normalized essays were unique across the selected 100. The original filtering report records four duplicate ID or normalized-essay rejections and zero near-duplicate candidate rejections during selection. In the final 100, five-word shingle screening found zero near-duplicates under the preparation thresholds. Normalized prompt overlaps were 9 development/validation, 3 development/test and 4 validation/test. Semantic duplication and prior model exposure remain possible.

Tune only on development, select on validation and lock model, prompt and schema before testing. Pass only the approved prompt and essay to a future provider. Keep labels, IDs used for joining and prior GPT advice with the evaluator. The publisher declares CC BY 4.0. Manual relevance, completeness, privacy, underlying rights and external processing approval are pending. Use synthetic samples for tests. Independent human reference scores are pending. The optional original Band 9 samples are supplementary development material.

Provisional Feasibility Study §9.4 thresholds for ten independently rated essays require valid feedback on 10/10 and at least 32/40 criterion deviations of at most one band inclusive. One missing, invalid or failed output costs four comparisons. Report the Writing9 proxy separately. A qualified reviewer first scores the blind essays, then records specificity, actionable advice, consistency with the essay and fabricated error claims per essay. Allow fabricated claims in at most one of ten essays. Record unevaluable essays explicitly. Automated acceptance alone cannot establish these human judgments. No independent human acceptance result is available.
