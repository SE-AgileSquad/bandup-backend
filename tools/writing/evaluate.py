"""Offline scoring of recorded raw feedback. No model or network access."""
import argparse
import json
import sys
from decimal import Decimal, InvalidOperation
from pathlib import Path

from jsonschema import Draft202012Validator
from referencing import Registry, Resource

CRITERIA = ("tr", "cc", "lr", "gra")
SCHEMAS = Path(__file__).resolve().parents[2] / "docs/writing/schemas"


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON property")
        result[key] = value
    return result


def invalid_number(_):
    raise ValueError("Non-finite number")


def parse(text):
    try:
        return json.loads(text, object_pairs_hook=unique_object, parse_float=Decimal,
                          parse_constant=invalid_number)
    except InvalidOperation as exc:
        raise ValueError("Number exceeds Decimal representation limits") from exc


def read(path):
    return parse(path.read_text(encoding="utf-8"))


def validator(public=False):
    raw = read(SCHEMAS / "writing-feedback.model.v1.schema.json")
    registry = Registry().with_resource(raw["$id"], Resource.from_contents(raw))
    schema = read(SCHEMAS / "writing-feedback.public.v1.schema.json") if public else raw
    Draft202012Validator.check_schema(schema)
    return Draft202012Validator(schema, registry=registry)


def indexed(rows):
    if not isinstance(rows, list):
        raise ValueError("Expected a record array")
    result = {}
    for row in rows:
        key = row["id"]
        if not isinstance(key, str) or not key.strip() or key in result:
            raise ValueError("Expected unique non-empty sample IDs")
        result[key] = row
    return result


def metrics(errors, total):
    count = len(errors)
    within = sum(abs(error) <= 1 for error in errors)
    return {"within_one": within, "denominator": total, "accuracy": within / total,
            "error_coverage": count, "mae": float(sum(map(abs, errors)) / count) if count else None,
            "mean_signed_error": float(sum(errors) / count) if count else None}


def evaluate(mode, references, outputs):
    if mode not in ("proxy", "human"):
        raise ValueError("Expected proxy or human evaluation mode")
    refs, records = indexed(references), indexed(outputs)
    if not refs or records.keys() - refs.keys():
        raise ValueError("Empty reference set or unknown output IDs")
    human = mode == "human"
    if human and len(refs) != 10:
        raise ValueError("Human evaluation requires ten reference samples")
    schema = validator()
    permitted_scores = schema.schema["$defs"]["score"]["enum"]
    errors = {criterion: [] for criterion in CRITERIA}
    counts = dict(valid=0, invalid=0, missing=0, failed=0, invalid_evidence=0)
    pending = 0
    for key, reference in refs.items():
        essay = reference["essay"]
        if not isinstance(essay, str) or not essay.strip():
            raise ValueError("Expected original essay")
        field = "human_reference_scores" if human else "reference_scores"
        if reference.keys() & {"reference_scores", "human_reference_scores"} != {field}:
            raise ValueError("Expected exactly the reference source selected by mode")
        labels = reference[field]
        if not isinstance(labels, dict):
            raise ValueError("Expected a reference score object")
        if any(not isinstance(key, str) for key in labels):
            raise ValueError("Expected string criterion identifiers")
        normalized = {key.lower(): value for key, value in labels.items()}
        if len(normalized) != len(labels) or set(normalized) - {"overall"} != set(CRITERIA):
            raise ValueError("Expected four unambiguous criterion labels and optional overall")
        scores = {}
        for criterion in CRITERIA:
            score = normalized[criterion]
            if human and score is None:
                pending += 1
                continue
            if type(score) not in (int, Decimal) or not Decimal(score).is_finite() or score not in permitted_scores:
                raise ValueError("Invalid reference score")
            scores[criterion] = score
        if key not in records:
            counts["missing"] += 1
            continue
        record = records[key]
        if set(record) == {"id", "failed"} and record["failed"] is True:
            counts["failed"] += 1
            continue
        if set(record) != {"id", "output"} or not isinstance(record["output"], str):
            raise ValueError("Expected recorded output string or failed flag")
        try:
            feedback = parse(record["output"])
        except ValueError:
            counts["invalid"] += 1
            continue
        if not schema.is_valid(feedback):
            counts["invalid"] += 1
            continue
        criteria = feedback["criteria"]
        if any(item["excerpt"] not in essay for entry in criteria.values()
               for item in entry.get("evidence", [])):
            counts["invalid_evidence"] += 1
            continue
        counts["valid"] += 1
        for criterion, reference_score in scores.items():
            errors[criterion].append(criteria[criterion]["score"] - reference_score)

    total = len(refs)
    overall = metrics([error for group in errors.values() for error in group], total * 4)
    schema_valid = counts["valid"] + counts["invalid_evidence"]
    report = {"mode": mode, "essays": total, "counts": counts, "schema_valid": schema_valid,
              "json_validity_rate": schema_valid / total, "missing_reference_scores": pending,
              "overall": overall, "criteria": {k: metrics(v, total) for k, v in errors.items()}}
    if human:
        report["automated_gate"] = ("pending" if pending else
                                    "passed" if counts["valid"] == 10 and overall["within_one"] >= 32 else "failed")
        report["feedback_quality_review"] = "requires independent reviewer"
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["proxy", "human"])
    parser.add_argument("references", type=Path)
    parser.add_argument("outputs", type=Path)
    args = parser.parse_args()
    try:
        report = evaluate(args.mode, read(args.references), read(args.outputs))
    except (ValueError, KeyError, TypeError, OSError) as exc:
        parser.exit(2, f"Invalid evaluation input ({type(exc).__name__}).\n")
    print(json.dumps(report, indent=2))
    overall = report["overall"]
    print(f"{args.mode}: valid JSON {report['schema_valid']}/{report['essays']}, "
          f"within one band {overall['within_one']}/{overall['denominator']}, "
          f"MAE {overall['mae']}, signed error {overall['mean_signed_error']} "
          f"over {overall['error_coverage']} comparisons.", file=sys.stderr)


if __name__ == "__main__":
    main()
