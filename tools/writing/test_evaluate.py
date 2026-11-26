import copy
import json
import subprocess
import sys
import unittest
from pathlib import Path

from evaluate import CRITERIA, evaluate, parse, read, validator

EXAMPLES = Path(__file__).parent / "examples"
OUTPUT = read(EXAMPLES / "outputs.json")[0]["output"]
REFERENCE = read(EXAMPLES / "references.json")[0]


class FeedbackTests(unittest.TestCase):
    def test_exact_score_tokens(self):
        public = EXAMPLES.parents[2] / "docs/writing/examples/result.json"
        public_text = json.dumps(json.loads(public.read_text(encoding="utf-8"))["feedback"])
        valid = ("0", "0.5", "6.5", "9", "6.0", "6e0")
        invalid = ("-0.5", "9.5", "6.25", "0.1", "1e-9999999", "1e9999999",
                   "6.500000000000000000000000000000001", "1e-99999999999999999999999",
                   "NaN", "Infinity", "true", '"6"')
        templates = ((validator(), OUTPUT, '"score":6,'),
                     (validator(public=True), public_text, '"score": 6,'),
                     (validator(public=True), public_text, '"estimatedOverallBand": 6.5,'))
        for schema, sample, original in templates:
            self.assertIn(original, sample)
            field = original.split(":")[0]
            for token in (*valid, *invalid):
                with self.subTest(field=field, token=token):
                    payload = sample.replace(original, f"{field}:{token},", 1)
                    try:
                        parsed = parse(payload)
                    except ValueError:
                        self.assertIn(token, invalid)
                    else:
                        self.assertEqual(schema.is_valid(parsed), token in valid)

    def test_extreme_score_fails_human_gate(self):
        refs = [dict(id=str(i), essay="Synthetic", human_reference_scores=dict(tr=0, cc=6, lr=6, gra=6))
                for i in range(10)]
        for tr, expected in (("0", (10, 40, "passed")), ("1e-9999999", (0, 0, "failed"))):
            raw = OUTPUT.replace('"score":6,', f'"score":{tr},', 1)
            report = evaluate("human", refs, [dict(id=r["id"], output=raw) for r in refs])
            self.assertEqual(report["overall"]["denominator"], 40)
            self.assertEqual((report["schema_valid"], report["overall"]["within_one"],
                              report["automated_gate"]), expected)
        refs[0]["human_reference_scores"]["tr"] = parse("1e-9999999")
        with self.assertRaises(ValueError):
            evaluate("human", refs, [])

    def test_cli_fixture_metrics(self):
        run = subprocess.run([sys.executable, "-B", str(Path(__file__).with_name("evaluate.py")),
                              "proxy", str(EXAMPLES / "references.json"), str(EXAMPLES / "outputs.json")],
                             check=True, capture_output=True, text=True)
        report = json.loads(run.stdout)
        self.assertEqual((report["schema_valid"], report["essays"]), (1, 1))
        self.assertEqual(report["counts"], dict(valid=1, invalid=0, missing=0, failed=0, invalid_evidence=0))
        self.assertEqual(report["overall"], dict(within_one=4, denominator=4, accuracy=1,
                                               error_coverage=4, mae=0.125, mean_signed_error=-0.125))

    def test_rejects_unknown_mode_and_mixed_sources(self):
        labels = REFERENCE["reference_scores"]
        with self.assertRaises(ValueError):
            evaluate("humam", [REFERENCE], [])
        for mode in ("human", "proxy"):
            both = [dict(REFERENCE, id=str(i), human_reference_scores=labels) for i in range(10)]
            with self.assertRaises(ValueError):
                evaluate(mode, both, [])
        for invalid in (None, {}, dict(labels, TR=7), dict(labels, tr=True),
                        dict(labels, tr="6"), dict(labels, cc=10), dict(labels, gra=parse("6.25"))):
            with self.subTest(labels=invalid), self.assertRaises(ValueError):
                evaluate("proxy", [dict(REFERENCE, reference_scores=invalid)], [])

    def test_sample_identifiers_and_empty_inputs(self):
        record = {"id": REFERENCE["id"], "output": OUTPUT}
        for refs, records in (([], []), ([REFERENCE, REFERENCE], []),
                              ([REFERENCE], [record, record]), ([REFERENCE], [dict(record, id="unknown")])):
            with self.assertRaises(ValueError):
                evaluate("proxy", refs, records)
        second = dict(REFERENCE, id="second", reference_scores=dict.fromkeys(CRITERIA, 0))
        failed = {"id": "second", "failed": True}
        report = evaluate("proxy", [REFERENCE, second], [failed, record])
        self.assertEqual(report["overall"]["within_one"], 4)
        self.assertEqual(report, evaluate("proxy", [second, REFERENCE], [record, failed]))
        empty = evaluate("proxy", [REFERENCE, second], [failed])
        self.assertEqual(empty["overall"]["error_coverage"], 0)
        self.assertIsNone(empty["overall"]["mae"])
        self.assertIsNone(empty["overall"]["mean_signed_error"])

    def test_invalid_records(self):
        model = json.loads(OUTPUT)
        cases = ["{", "{}", OUTPUT.replace('"score":6', '"score":6,"score":6', 1)]
        for value in (None, "6", True, -1, 9.5, 6.25, float("nan"), float("inf")):
            sample = copy.deepcopy(model)
            sample["criteria"]["cc"]["score"] = value
            cases.append(json.dumps(sample))
        for key, value in (("unknown", 2), ("recommendations", []),
                           ("recommendations", [" "]), ("explanation", None)):
            sample = copy.deepcopy(model)
            sample["criteria"]["tr"][key] = value
            cases.append(json.dumps(sample))
        for key in CRITERIA:
            sample = copy.deepcopy(model)
            del sample["criteria"][key]
            cases.append(json.dumps(sample))
        for output in cases:
            with self.subTest(output=output):
                report = evaluate("proxy", [REFERENCE], [{"id": REFERENCE["id"], "output": output}])
                self.assertEqual(report["counts"]["invalid"], 1)

    def test_scores_and_excerpt(self):
        sample = json.loads(OUTPUT)
        for score in (0, 0.5, 9):
            sample["criteria"]["tr"]["score"] = score
            self.assertTrue(validator().is_valid(parse(json.dumps(sample))))
        sample["criteria"]["tr"]["evidence"] = [{"excerpt": "absent", "observation": "claim"}]
        record = {"id": REFERENCE["id"], "output": json.dumps(sample)}
        report = evaluate("proxy", [REFERENCE], [record])
        self.assertEqual(report["counts"]["invalid_evidence"], 1)
        self.assertEqual(report["overall"]["within_one"], 0)

    def test_examples(self):
        folder = EXAMPLES.parents[2] / "docs/writing/examples"
        feedback = read(folder / "result.json")["feedback"]
        self.assertTrue(validator(public=True).is_valid(feedback))
        model = parse(OUTPUT)
        self.assertEqual(feedback["criteria"], model["criteria"])
        self.assertEqual(read(folder / "submission.json")["essayText"], REFERENCE["essay"])
        self.assertFalse(validator().is_valid(feedback))
        self.assertFalse(validator(public=True).is_valid(model))

    def test_human_threshold(self):
        for expected in (31, 32):
            refs, records = [], []
            for i in range(10):
                labels = {key: 6 if 4*i+j < expected else 2 for j, key in enumerate(CRITERIA)}
                refs.append({"id": str(i), "essay": "Synthetic", "human_reference_scores": labels})
                records.append({"id": str(i), "output": OUTPUT})
            report = evaluate("human", refs, records)
            self.assertEqual(report["overall"]["within_one"], expected)
            self.assertEqual(report["automated_gate"], "passed" if expected == 32 else "failed")
        records.pop()
        incomplete = evaluate("human", refs, records)
        self.assertEqual(incomplete["overall"]["within_one"], 32)
        self.assertEqual(incomplete["automated_gate"], "failed")
        records[0] = {"id": "0", "failed": True}
        records[1]["output"] = "{"
        report = evaluate("human", refs, records)
        self.assertEqual(report["overall"]["denominator"], 40)
        self.assertEqual(report["overall"]["error_coverage"], 28)
        self.assertEqual([report["counts"][k] for k in ("missing", "failed", "invalid")], [1, 1, 1])
        refs[0]["human_reference_scores"]["cc"] = None
        self.assertEqual(evaluate("human", refs, records)["automated_gate"], "pending")
        with self.assertRaises(ValueError):
            evaluate("proxy", refs, records)

    def test_proxy_signed_errors(self):
        report = evaluate("proxy", [REFERENCE], [{"id": REFERENCE["id"], "output": OUTPUT}])
        self.assertEqual(report["criteria"]["cc"]["mean_signed_error"], -0.5)
        self.assertEqual(report["criteria"]["cc"]["mae"], 0.5)
        self.assertNotIn("automated_gate", report)


if __name__ == "__main__":
    unittest.main()
