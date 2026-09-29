import hashlib
import random
from pathlib import Path

_MINIMAL_PDF_TEMPLATE = b"""%PDF-1.4
1 0 obj<</Type /Catalog /Pages 2 0 R>>endobj
2 0 obj<</Type /Pages /Kids [3 0 R] /Count 1>>endobj
3 0 obj<</Type /Page /Parent 2 0 R /MediaBox [0 0 612 792]
/Contents 4 0 R /Resources<</Font<</F1 5 0 R>>>>>>endobj
4 0 obj<</Length {length}>>
stream
BT /F1 12 Tf 50 700 Td ({content}) Tj ET
endstream
endobj
5 0 obj<</Type /Font /Subtype /Type1 /BaseFont /Helvetica>>endobj
xref
0 6
0000000000 65535 f\r
0000000009 00000 n\r
0000000058 00000 n\r
0000000115 00000 n\r
0000000266 00000 n\r
{xref_content}
trailer<</Size 6 /Root 1 0 R>>
startxref
{startxref}
%%EOF"""

_INVENTION_TOPICS = [
    "distributed consensus algorithm for edge computing networks",
    "quantum-resistant encryption method using lattice-based cryptography",
    "neural network pruning technique for resource-constrained devices",
    "self-healing distributed database with automatic partition recovery",
    "real-time code vulnerability detection using static analysis and LLM",
    "zero-knowledge proof system for privacy-preserving authentication",
    "federated learning protocol with differential privacy guarantees",
    "compiler optimization for vectorized cryptographic operations",
    "adaptive load balancing for heterogeneous containerized workloads",
    "just-in-time memory compression for high-throughput data pipelines",
    "blockchain-based audit trail for distributed machine learning models",
    "automated test generation from natural language specifications",
    "semantic code search using embedding-based similarity ranking",
    "continuous integration system with predictive flakiness detection",
    "distributed tracing framework with anomaly detection and root cause analysis",
    "serverless function orchestration with automatic dependency resolution",
    "data lineage tracking for multi-cloud data warehouses",
    "incremental view maintenance for real-time analytics dashboards",
    "cache coherence protocol for disaggregated memory systems",
    "speculative execution engine for reactive data processing pipelines",
]

_DESCRIPTION_TEMPLATES = [
    "proposes a novel approach that improves throughput by 3x over prior art",
    "introduces a method reducing latency to sub-millisecond levels under load",
    "describes a system achieving 99.99% availability during network partitions",
    "discloses an algorithm with logarithmic complexity compared to quadratic baseline",
    "presents a technique minimizing memory overhead while maintaining accuracy",
    "details an architecture supporting horizontal scaling without coordinator bottlenecks",
    "explains a protocol enabling zero-downtime upgrades in production environments",
    "outlines a process for automatic recovery from cascading failures",
    "defines a schema allowing backward-compatible schema evolution",
    "demonstrates a mechanism for adaptive resource allocation under dynamic workloads",
]


def _build_pdf(content: str) -> bytes:
    content_bytes = content.encode("utf-8")
    length = len(content_bytes)
    xref_content = f"0000000{360 + length} 00000 n\r"
    startxref = 441 + length
    return (
        _MINIMAL_PDF_TEMPLATE.replace(b"{length}", str(length).encode())
        .replace(b"{content}", content_bytes)
        .replace(b"{xref_content}", xref_content.encode())
        .replace(b"{startxref}", str(startxref).encode())
    )


def generate_synthetic_dataset(count: int, seed: int = 42) -> dict[str, bytes]:
    random.seed(seed)
    dataset = {}

    for i in range(count):
        topic = random.choice(_INVENTION_TOPICS)
        description = random.choice(_DESCRIPTION_TEMPLATES)
        digest = hashlib.sha256(str(i).encode()).hexdigest()[:8]
        extra = f" Variant {i % 10} with parameter tuning {digest}."
        content = f"Research Document {i + 1}: {topic} which {description}.{extra}"
        pdf_bytes = _build_pdf(content)
        filename = f"document_{i + 1:03d}.pdf"
        dataset[filename] = pdf_bytes

    return dataset


def save_dataset_to_disk(dataset: dict[str, bytes], output_dir: Path) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    for filename, content in dataset.items():
        (output_dir / filename).write_bytes(content)
