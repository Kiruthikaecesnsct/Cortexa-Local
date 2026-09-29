from pathlib import Path

from ingestion.application.code_walker import MAX_FILE_BYTES, walk_code_files


def test_walks_text_files(tmp_path: Path) -> None:
    (tmp_path / "hello.py").write_text("print('hello')", encoding="utf-8")
    results = list(walk_code_files(tmp_path))
    assert len(results) == 1
    rel_path, text = results[0]
    assert rel_path == "hello.py"
    assert "print" in text


def test_skips_binary_files(tmp_path: Path) -> None:
    binary = tmp_path / "data.bin"
    binary.write_bytes(b"some\x00binary\x00data")
    results = list(walk_code_files(tmp_path))
    assert results == []


def test_skips_oversized_files(tmp_path: Path) -> None:
    large = tmp_path / "big.txt"
    large.write_bytes(b"a" * (MAX_FILE_BYTES + 1))
    results = list(walk_code_files(tmp_path))
    assert results == []


def test_relative_paths_use_forward_slashes(tmp_path: Path) -> None:
    subdir = tmp_path / "pkg" / "sub"
    subdir.mkdir(parents=True)
    (subdir / "mod.py").write_text("x = 1", encoding="utf-8")
    results = list(walk_code_files(tmp_path))
    assert len(results) == 1
    rel_path, _ = results[0]
    assert "/" in rel_path
    assert "\\" not in rel_path
    assert rel_path == "pkg/sub/mod.py"


def test_nested_directory_traversal(tmp_path: Path) -> None:
    (tmp_path / "a.py").write_text("a = 1", encoding="utf-8")
    sub = tmp_path / "nested"
    sub.mkdir()
    (sub / "b.py").write_text("b = 2", encoding="utf-8")
    paths = {rel for rel, _ in walk_code_files(tmp_path)}
    assert paths == {"a.py", "nested/b.py"}


def test_text_content_is_returned(tmp_path: Path) -> None:
    expected = "def foo():\n    return 42\n"
    (tmp_path / "foo.py").write_text(expected, encoding="utf-8")
    results = list(walk_code_files(tmp_path))
    _, text = results[0]
    assert text == expected
