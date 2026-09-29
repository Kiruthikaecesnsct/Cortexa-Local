from evidence.infrastructure.patent_apis.epo_query_builder import build_epo_cql_query


def test_builds_cql_from_natural_language():
    text = "method for sparse tensor quantization"
    query = build_epo_cql_query(text, max_terms=12, max_length=4000)
    assert query == 'txt=("method" OR "sparse" OR "tensor" OR "quantization")'


def test_removes_stopwords():
    text = "the method for the sparse and tensor"
    query = build_epo_cql_query(text, max_terms=12, max_length=4000)
    assert "the" not in query
    assert "for" not in query
    assert "and" not in query
    assert "method" in query
    assert "sparse" in query
    assert "tensor" in query


def test_deduplicates_preserving_order():
    text = "neural network network neural"
    query = build_epo_cql_query(text, max_terms=12, max_length=4000)
    assert query == 'txt=("neural" OR "network")'


def test_enforces_term_cap():
    text = "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike"
    query = build_epo_cql_query(text, max_terms=5, max_length=4000)
    terms = query.split(" OR ")
    assert len(terms) == 5


def test_enforces_length_limit():
    text = "quantum superconductivity nanotechnology bioengineering cryptography"
    query = build_epo_cql_query(text, max_terms=10, max_length=50)
    assert len(query) <= 50


def test_returns_empty_when_no_usable_terms():
    text = "the a an it"
    query = build_epo_cql_query(text, max_terms=12, max_length=4000)
    assert query == ""


def test_returns_empty_when_only_short_tokens():
    text = "ab cd ef gh"
    query = build_epo_cql_query(text, max_terms=12, max_length=4000)
    assert query == ""


def test_handles_empty_input():
    query = build_epo_cql_query("", max_terms=12, max_length=4000)
    assert query == ""


def test_handles_punctuation_and_case():
    text = "METHOD! for SPARSE-tensor (Quantization)"
    query = build_epo_cql_query(text, max_terms=12, max_length=4000)
    assert "method" in query.lower()
    assert "sparse" in query.lower()
    assert "tensor" in query.lower()
    assert "quantization" in query.lower()


def test_length_limit_reduces_term_count():
    text = "superconductivity nanotechnology bioengineering cryptography"
    query = build_epo_cql_query(text, max_terms=10, max_length=70)
    assert len(query) <= 70
    assert "txt=(" in query
