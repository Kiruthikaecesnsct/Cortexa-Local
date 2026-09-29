import tiktoken

from ingestion.application import token_counter


def test_count_known_string():
    enc = tiktoken.get_encoding("cl100k_base")
    text = "Hello, world!"
    expected = len(enc.encode(text))
    assert token_counter.count(text) == expected


def test_count_empty_string():
    assert token_counter.count("") == 0


def test_encode_empty_string():
    assert token_counter.encode("") == []


def test_roundtrip_encode_decode():
    text = "The quick brown fox jumps over the lazy dog."
    tokens = token_counter.encode(text)
    recovered = token_counter.decode(tokens)
    assert recovered == text


def test_roundtrip_cjk():
    text = "日本語のテキスト"
    tokens = token_counter.encode(text)
    recovered = token_counter.decode(tokens)
    assert recovered == text


def test_encode_special_token_literal_as_text():
    # Source repos (e.g. GPT training code) contain "<|endoftext|>" literally;
    # tiktoken raises on these by default, so encode must treat them as text.
    text = "sample = '<|endoftext|>'  # marker"
    tokens = token_counter.encode(text)
    assert token_counter.decode(tokens) == text


def test_count_special_token_literal_does_not_raise():
    assert token_counter.count("<|endoftext|><|fim_prefix|>") > 0
