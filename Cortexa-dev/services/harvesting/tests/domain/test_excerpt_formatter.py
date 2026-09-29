from harvesting.domain.services.excerpt_formatter import build_clean_excerpt


def test_build_clean_excerpt_returns_empty_string_for_empty_text():
    assert build_clean_excerpt("", 0, 0) == ""


def test_build_clean_excerpt_expands_to_sentence_boundaries():
    text = "First sentence is here. Second sentence has the cited span. Third sentence follows."
    start = text.index("Second")
    end = start + len("Second sentence has the cited span.")

    excerpt = build_clean_excerpt(text, start, end)

    assert excerpt == "Second sentence has the cited span."


def test_build_clean_excerpt_expands_partial_span_within_sentence():
    text = "First sentence is here. Second sentence has the cited span. Third sentence follows."
    start = text.index("cited")
    end = start + len("cited")

    excerpt = build_clean_excerpt(text, start, end)

    assert excerpt == "Second sentence has the cited span."


def test_build_clean_excerpt_falls_back_to_full_text_when_no_sentence_boundary():
    text = "no punctuation anywhere in this run of words at all"

    excerpt = build_clean_excerpt(text, 3, 10)

    assert excerpt == text


def test_build_clean_excerpt_collapses_whitespace_runs_to_single_space():
    text = "Line one.\n\n   Line   two has\textra   whitespace. Line three."
    start = text.index("Line   two")
    end = start + len("Line   two has\textra   whitespace.")

    excerpt = build_clean_excerpt(text, start, end)

    assert "  " not in excerpt
    assert "\n" not in excerpt
    assert "\t" not in excerpt
    assert excerpt == "Line two has extra whitespace."


def test_build_clean_excerpt_never_cuts_mid_word_when_capped():
    text = ("word " * 300).strip() + "."

    excerpt = build_clean_excerpt(text, 0, len(text), max_chars=50)

    assert excerpt.endswith("…")
    body = excerpt[:-1].strip()
    assert not body.endswith("wor")
    assert all(part == "word" for part in body.split(" "))


def test_build_clean_excerpt_caps_length_with_ellipsis_marker():
    text = "Sentence number one is quite short. " * 40

    excerpt = build_clean_excerpt(text, 0, len(text), max_chars=100)

    assert len(excerpt) <= 101
    assert excerpt.endswith("…")


def test_build_clean_excerpt_no_ellipsis_when_under_cap():
    text = "A short sentence that fits easily within the cap."

    excerpt = build_clean_excerpt(text, 0, len(text), max_chars=600)

    assert excerpt == text
    assert not excerpt.endswith("…")


def test_build_clean_excerpt_clamps_out_of_range_span():
    text = "Only one sentence here."

    excerpt = build_clean_excerpt(text, -50, 5000)

    assert excerpt == text
