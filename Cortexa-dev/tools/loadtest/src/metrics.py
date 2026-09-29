import json
import subprocess
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from settings import LoadTestSettings


@dataclass
class LoadTestMetrics:
    total_wall_clock_s: float
    total_documents: int
    total_batches: int
    batches_completed: int
    batches_failed: int
    batches_partial: int
    throughput_docs_per_min: float
    pass_threshold_met: bool
    pass_reasons: list[str]
    fail_reasons: list[str]


def _run_az_command(cmd: list[str]) -> str:
    try:
        result = subprocess.run(cmd, capture_output=True, text=True, check=True, timeout=30)
        return result.stdout.strip()
    except subprocess.CalledProcessError, subprocess.TimeoutExpired, FileNotFoundError:
        return ""


def collect_metrics(
    settings: LoadTestSettings,
    wall_clock_s: float,
    batch_states: dict[str, str],
) -> LoadTestMetrics:
    total_batches = len(batch_states)
    completed = sum(1 for s in batch_states.values() if s == "Completed")
    failed = sum(1 for s in batch_states.values() if s == "Failed")
    partial = sum(1 for s in batch_states.values() if s == "PartiallyFailed")

    throughput = (settings.document_count / wall_clock_s) * 60.0 if wall_clock_s > 0 else 0.0

    pass_reasons = []
    fail_reasons = []

    wall_clock_threshold_s = 90 * 60
    if wall_clock_s <= wall_clock_threshold_s:
        pass_reasons.append(
            f"Wall-clock {wall_clock_s:.1f}s <= {wall_clock_threshold_s}s threshold"
        )
    else:
        fail_reasons.append(
            f"Wall-clock {wall_clock_s:.1f}s exceeds {wall_clock_threshold_s}s threshold"
        )

    if failed == 0:
        pass_reasons.append("Zero failed batches")
    else:
        fail_reasons.append(f"{failed} batches failed completely")

    if partial == 0:
        pass_reasons.append("Zero partially failed batches")
    else:
        fail_reasons.append(f"{partial} batches partially failed")

    pass_threshold_met = len(fail_reasons) == 0

    return LoadTestMetrics(
        total_wall_clock_s=wall_clock_s,
        total_documents=settings.document_count,
        total_batches=total_batches,
        batches_completed=completed,
        batches_failed=failed,
        batches_partial=partial,
        throughput_docs_per_min=throughput,
        pass_threshold_met=pass_threshold_met,
        pass_reasons=pass_reasons,
        fail_reasons=fail_reasons,
    )


def save_report(metrics: LoadTestMetrics, output_path: Path) -> None:
    report = {
        "timestamp": datetime.utcnow().isoformat() + "Z",
        "total_wall_clock_s": metrics.total_wall_clock_s,
        "total_documents": metrics.total_documents,
        "total_batches": metrics.total_batches,
        "batches_completed": metrics.batches_completed,
        "batches_failed": metrics.batches_failed,
        "batches_partial": metrics.batches_partial,
        "throughput_docs_per_min": metrics.throughput_docs_per_min,
        "pass_threshold_met": metrics.pass_threshold_met,
        "pass_reasons": metrics.pass_reasons,
        "fail_reasons": metrics.fail_reasons,
    }
    output_path.write_text(json.dumps(report, indent=2))


def print_summary(metrics: LoadTestMetrics) -> None:
    print("\n" + "=" * 80)
    print("LOAD TEST SUMMARY")
    print("=" * 80)
    print(f"Total wall-clock time: {metrics.total_wall_clock_s:.1f}s")
    print(f"Total documents: {metrics.total_documents}")
    print(f"Total batches: {metrics.total_batches}")
    print(f"Batches completed: {metrics.batches_completed}")
    print(f"Batches failed: {metrics.batches_failed}")
    print(f"Batches partial: {metrics.batches_partial}")
    print(f"Throughput: {metrics.throughput_docs_per_min:.2f} docs/min")
    print(f"\nPass threshold met: {metrics.pass_threshold_met}")
    if metrics.pass_reasons:
        print("\nPass reasons:")
        for reason in metrics.pass_reasons:
            print(f"  - {reason}")
    if metrics.fail_reasons:
        print("\nFail reasons:")
        for reason in metrics.fail_reasons:
            print(f"  - {reason}")
    print("=" * 80)
