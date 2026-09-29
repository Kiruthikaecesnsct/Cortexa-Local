def char_range_to_line_range(text: str, start_char: int, end_char: int) -> tuple[int, int]:
    start_line = text[:start_char].count("\n") + 1
    span = text[start_char:end_char]
    span_newlines = span.count("\n")
    if span.endswith("\n"):
        span_newlines -= 1
    end_line = start_line + span_newlines
    return (start_line, end_line)
