import re


def normalize_text(raw: str) -> str:
    if not raw:
        return ""

    text = raw.replace("\r\n", "\n").replace("\r", "\n")

    lines = text.split("\n")
    lines = [re.sub(r"[ \t]+", " ", line).rstrip() for line in lines]
    text = "\n".join(lines)

    text = re.sub(r"\n{3,}", "\n\n", text)

    return text.strip()
