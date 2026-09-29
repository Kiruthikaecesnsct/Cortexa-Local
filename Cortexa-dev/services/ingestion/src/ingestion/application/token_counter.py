import tiktoken

_encoding_cache: dict[str, tiktoken.Encoding] = {}


def _get_encoding(encoding_name: str) -> tiktoken.Encoding:
    if encoding_name not in _encoding_cache:
        _encoding_cache[encoding_name] = tiktoken.get_encoding(encoding_name)
    return _encoding_cache[encoding_name]


def encode(text: str, encoding_name: str = "cl100k_base") -> list[int]:
    # Treat any special-token strings (e.g. "<|endoftext|>") that appear in the
    # source as ordinary text. tiktoken raises by default; source repos such as
    # GPT training code contain these literals, and we only tokenize to chunk.
    return _get_encoding(encoding_name).encode(text, disallowed_special=())


def decode(tokens: list[int], encoding_name: str = "cl100k_base") -> str:
    return _get_encoding(encoding_name).decode(tokens)


def count(text: str, encoding_name: str = "cl100k_base") -> int:
    return len(encode(text, encoding_name))
