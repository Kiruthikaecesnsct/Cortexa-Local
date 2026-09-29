from ingestion.infrastructure.git.clone_cleanup import remove_clone, sweep_workdir


def test_remove_clone_deletes_existing_directory(tmp_path):
    clone_dir = tmp_path / "clone123"
    clone_dir.mkdir()
    (clone_dir / "file.txt").write_text("content")

    remove_clone(str(clone_dir))

    assert not clone_dir.exists()


def test_remove_clone_does_not_raise_if_directory_missing(tmp_path):
    nonexistent = tmp_path / "nonexistent"

    remove_clone(str(nonexistent))


def test_remove_clone_does_not_raise_if_directory_already_gone(tmp_path):
    clone_dir = tmp_path / "clone456"
    clone_dir.mkdir()
    clone_dir.rmdir()

    remove_clone(str(clone_dir))


def test_sweep_workdir_removes_all_subdirectories(tmp_path):
    workdir = tmp_path / "clones"
    workdir.mkdir()
    (workdir / "clone1").mkdir()
    (workdir / "clone2").mkdir()
    (workdir / "clone3").mkdir()

    sweep_workdir(str(workdir))

    remaining = list(workdir.iterdir())
    assert len(remaining) == 0


def test_sweep_workdir_does_not_remove_files(tmp_path):
    workdir = tmp_path / "clones"
    workdir.mkdir()
    (workdir / "clone1").mkdir()
    file_path = workdir / "file.txt"
    file_path.write_text("keep me")

    sweep_workdir(str(workdir))

    assert file_path.exists()


def test_sweep_workdir_creates_missing_workdir(tmp_path):
    nonexistent = tmp_path / "nested" / "nonexistent_workdir"

    sweep_workdir(str(nonexistent))

    assert nonexistent.exists()


def test_sweep_workdir_does_not_raise_on_empty_workdir(tmp_path):
    workdir = tmp_path / "clones"
    workdir.mkdir()

    sweep_workdir(str(workdir))

    assert workdir.exists()


def test_sweep_workdir_logs_removed_count(tmp_path, caplog):
    import logging

    caplog.set_level(logging.INFO)
    workdir = tmp_path / "clones"
    workdir.mkdir()
    (workdir / "clone1").mkdir()
    (workdir / "clone2").mkdir()

    sweep_workdir(str(workdir))

    assert "Swept 2 orphaned clone(s)" in caplog.text


def test_remove_clone_nested_directory(tmp_path):
    clone_dir = tmp_path / "clone789"
    nested = clone_dir / "a" / "b" / "c"
    nested.mkdir(parents=True)
    (nested / "deep.txt").write_text("deep file")

    remove_clone(str(clone_dir))

    assert not clone_dir.exists()
