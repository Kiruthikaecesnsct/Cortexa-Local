from seeding.domain.models.invention_lattice import InventionLattice, LatticeEntry

_LEVEL_LABELS = (
    ("continuations", "CONTINUATIONS"),
    ("platform", "PLATFORM"),
    ("system", "SYSTEM"),
)


def _render_entry_lines(entry: LatticeEntry, indent: str) -> list[str]:
    return [
        f"{indent}- {entry.title}",
        f"{indent}  scope: {entry.scope}",
    ]


def _render_level(label: str, entries: list[LatticeEntry]) -> list[str]:
    lines = [f"  [{label}]"]
    for entry in entries:
        lines.extend(_render_entry_lines(entry, "    "))
    return lines


def render_lattice_text(lattice: InventionLattice) -> str:
    lines = [
        "[CORE]",
        f"- {lattice.core.title}",
        f"  scope: {lattice.core.scope}",
        "|",
    ]
    for attr_name, label in _LEVEL_LABELS:
        entries = getattr(lattice, attr_name)
        lines.append("+--" + label)
        lines.extend(_render_level(label, entries))
    return "\n".join(lines)
