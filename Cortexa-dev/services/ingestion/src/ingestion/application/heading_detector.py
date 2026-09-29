import re


class Heading:
    def __init__(self, text: str, start_char: int, end_char: int) -> None:
        self.text = text
        self.start_char = start_char
        self.end_char = end_char


def _is_markdown_atx_heading(line: str) -> bool:
    return bool(re.match(r"^#{1,6}\s+\S", line))


def _is_markdown_setext_heading(line: str, next_line: str | None) -> bool:
    if not next_line:
        return False
    is_underline = bool(re.match(r"^[=\-]{3,}$", next_line.strip()))
    has_content = bool(line.strip()) and len(line.strip()) < 100
    return is_underline and has_content


def _is_numbered_section(line: str) -> bool:
    pattern = r"^(?:\d+\.)+\d*\s+[A-Z]"
    return bool(re.match(pattern, line))


def _is_allcaps_section(line: str) -> bool:
    stripped = line.strip()
    if not stripped or len(stripped) < 4 or len(stripped) > 100:
        return False
    words = stripped.split()
    if not words:
        return False
    alpha_words = [w for w in words if any(c.isalpha() for c in w)]
    if not alpha_words:
        return False
    all_upper = all(w.isupper() for w in alpha_words)
    has_alpha = any(c.isalpha() for c in stripped)
    return all_upper and has_alpha


def _is_title_case_heading(line: str, next_line: str | None) -> bool:
    stripped = line.strip()
    if not stripped or len(stripped) < 10 or len(stripped) > 80:
        return False
    if stripped.endswith((".", "?", "!")):
        return False
    words = [w for w in stripped.split() if w.isalpha()]
    if len(words) < 3 or len(words) > 12:
        return False

    articles_and_prepositions = {
        "a",
        "an",
        "the",
        "and",
        "or",
        "but",
        "of",
        "to",
        "in",
        "on",
        "at",
        "for",
        "with",
        "by",
        "from",
    }
    significant_words = [
        w for w in words if w.lower() not in articles_and_prepositions or len(w) > 3
    ]

    if not significant_words:
        return False

    capitalized_count = sum(1 for w in significant_words if w[0].isupper())
    ratio = capitalized_count / len(significant_words)

    if ratio < 0.7:
        return False
    if next_line and next_line.strip() and not next_line.strip()[0].isupper():
        return False
    return True


def _extract_atx_text(line: str) -> str:
    return re.sub(r"^#{1,6}\s+", "", line).strip()


def _extract_plain_text(line: str) -> str:
    return line.strip()


def _classify_heading(line: str, next_line: str | None) -> tuple[bool, str]:
    rules = [
        (_is_markdown_atx_heading, _extract_atx_text, False),
        (_is_markdown_setext_heading, _extract_plain_text, True),
        (_is_numbered_section, _extract_plain_text, False),
        (_is_allcaps_section, _extract_plain_text, False),
        (_is_title_case_heading, _extract_plain_text, True),
    ]

    for predicate, extractor, needs_next in rules:
        args = (line, next_line) if needs_next else (line,)
        if predicate(*args):
            return True, extractor(line)

    return False, ""


def detect_headings(text: str) -> list[Heading]:
    if not text or not text.strip():
        return []

    lines = text.split("\n")
    headings: list[Heading] = []
    current_offset = 0

    i = 0
    while i < len(lines):
        line = lines[i]
        line_len = len(line)
        next_line = lines[i + 1] if i + 1 < len(lines) else None

        is_heading, heading_text = _classify_heading(line, next_line)

        if is_heading and heading_text:
            headings.append(
                Heading(
                    text=heading_text,
                    start_char=current_offset,
                    end_char=current_offset + line_len,
                )
            )

        current_offset += line_len + 1
        i += 1

    return headings


def find_heading_for_position(offset: int, headings: list[Heading]) -> str | None:
    if not headings:
        return None

    nearest_heading: Heading | None = None
    for heading in headings:
        if heading.start_char <= offset:
            nearest_heading = heading
        else:
            break

    return nearest_heading.text if nearest_heading else None
