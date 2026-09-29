import pytest
from qdrant_client.models import FieldCondition, Filter

from vector_router.domain.models import SearchFilter
from vector_router.infrastructure.backends.ai_search_backend import _build_odata_filter
from vector_router.infrastructure.backends.qdrant_backend import _build_qdrant_filter

# ---------- OData filter (AI Search) ----------


def test_build_odata_filter_none_input_returns_none():
    result = _build_odata_filter(None)

    assert result is None


def test_build_odata_filter_empty_list_returns_none():
    result = _build_odata_filter([])

    assert result is None


def test_build_odata_filter_single_filter_returns_correct_clause():
    filters = [SearchFilter(field="source", value="paper-1")]

    result = _build_odata_filter(filters)

    assert result == "source eq 'paper-1'"


def test_build_odata_filter_multiple_filters_joins_with_and():
    filters = [
        SearchFilter(field="batch_id", value="batch-x"),
        SearchFilter(field="doc_type", value="claim"),
    ]

    result = _build_odata_filter(filters)

    assert result == "batch_id eq 'batch-x' and doc_type eq 'claim'"


def test_build_odata_filter_unknown_field_raises_value_error():
    filters = [SearchFilter(field="unknown_field", value="v")]

    with pytest.raises(ValueError, match="Unknown filter field"):
        _build_odata_filter(filters)


def test_build_odata_filter_escapes_single_quotes_in_value():
    filters = [SearchFilter(field="source", value="o'malley")]

    result = _build_odata_filter(filters)

    assert result == "source eq 'o''malley'"


# ---------- Qdrant filter ----------


def test_build_qdrant_filter_none_input_returns_none():
    result = _build_qdrant_filter(None)

    assert result is None


def test_build_qdrant_filter_empty_list_returns_none():
    result = _build_qdrant_filter([])

    assert result is None


def test_build_qdrant_filter_single_filter_returns_filter_with_one_condition():
    filters = [SearchFilter(field="f", value="v")]

    result = _build_qdrant_filter(filters)

    assert isinstance(result, Filter)
    assert len(result.must) == 1
    condition = result.must[0]
    assert isinstance(condition, FieldCondition)
    assert condition.key == "f"
    assert condition.match.value == "v"


def test_build_qdrant_filter_multiple_filters_returns_filter_with_all_conditions():
    filters = [
        SearchFilter(field="a", value="x"),
        SearchFilter(field="b", value="y"),
    ]

    result = _build_qdrant_filter(filters)

    assert isinstance(result, Filter)
    assert len(result.must) == 2
    keys = {c.key for c in result.must}
    assert keys == {"a", "b"}
