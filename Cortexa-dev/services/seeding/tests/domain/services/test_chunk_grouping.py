from seeding.domain.services.chunk_grouping import (
    group_chunks,
    page_number,
    section_key,
    section_label,
)

BATCH_ID = "batch-1"
DOCUMENT_ID = "doc-1"


def _chunk(order_index: int, tokens: int = 10, **overrides) -> dict:
    chunk = {
        "id": f"chunk-{order_index}",
        "order_index": order_index,
        "token_count": tokens,
        "section_hint": "Intro",
    }
    chunk.update(overrides)
    return chunk


def test_section_label_prefers_section_hint():
    assert section_label({"section_hint": "Methods"}) == "Methods"


def test_section_label_falls_back_to_file_basename():
    chunk = {"section_hint": "", "metadata": {"file_path": "src\\pkg\\module.py"}}
    assert section_label(chunk) == "module.py"


def test_section_label_empty_when_no_hint_or_path():
    assert section_label({}) == ""


def test_page_number_null_becomes_minus_one():
    assert page_number({"page_number": None}) == -1
    assert page_number({"page_number": 3}) == 3


def test_section_key_is_deterministic_and_id_legal():
    first = section_key(BATCH_ID, DOCUMENT_ID, "Intro#part0")
    second = section_key(BATCH_ID, DOCUMENT_ID, "Intro#part0")
    assert first == second
    assert "/" not in first and " " not in first


def test_section_key_varies_with_partition():
    a = section_key(BATCH_ID, DOCUMENT_ID, "Intro#part0")
    b = section_key(BATCH_ID, DOCUMENT_ID, "Intro#part1")
    assert a != b


def test_single_group_gets_part0():
    groups = group_chunks([_chunk(0), _chunk(1)], max_group_tokens=1000)
    assert len(groups) == 1
    assert groups[0].partition_key == "Intro#part0"
    assert groups[0].chunk_ids == ["chunk-0", "chunk-1"]


def test_chunks_ordered_by_order_index_within_group():
    groups = group_chunks([_chunk(2), _chunk(0), _chunk(1)], max_group_tokens=1000)
    assert groups[0].chunk_ids == ["chunk-0", "chunk-1", "chunk-2"]


def test_groups_ordered_by_min_order_index():
    early = _chunk(0, section_hint="A")
    late = _chunk(5, section_hint="B")
    groups = group_chunks([late, early], max_group_tokens=1000)
    assert [g.group_key for g in groups] == ["A", "B"]


def test_oversize_group_is_split_greedily():
    chunks = [_chunk(i, tokens=40) for i in range(3)]
    groups = group_chunks(chunks, max_group_tokens=60)
    assert [g.partition_key for g in groups] == ["Intro#part0", "Intro#part1", "Intro#part2"]
    assert [g.chunk_ids for g in groups] == [["chunk-0"], ["chunk-1"], ["chunk-2"]]


def test_single_oversized_chunk_becomes_its_own_partition():
    chunks = [_chunk(0, tokens=500), _chunk(1, tokens=10)]
    groups = group_chunks(chunks, max_group_tokens=100)
    assert [g.chunk_ids for g in groups] == [["chunk-0"], ["chunk-1"]]


def test_grouping_is_deterministic_for_fixed_input():
    chunks = [_chunk(i, tokens=30) for i in range(4)]
    first = [g.partition_key for g in group_chunks(chunks, 50)]
    second = [g.partition_key for g in group_chunks(chunks, 50)]
    assert first == second
