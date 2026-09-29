import re

# US pre-grant publication numbers are canonically a 4-digit year followed by a
# 7-digit zero-padded serial (11 digits total), e.g. 2021 + 0345925 -> 20210345925.
_US_PREGRANT_CANONICAL_LENGTH = 11
_US_PREGRANT_YEAR_LENGTH = 4
_US_PREGRANT_SERIAL_LENGTH = 7


def canonical_google_patents_url(reference: str) -> str:
    if not reference or not reference.strip():
        return ""

    normalized = reference.upper().replace("-", "").replace(" ", "").replace("/", "")

    match = re.match(r"^([A-Z]+)(\d+)([A-Z]\d*)?$", normalized)
    if not match:
        return ""

    country, body, kind = match.groups()
    kind = kind or ""

    if _is_us_pregrant(country, kind):
        body = _pad_us_pregrant_serial(body)

    normalized = f"{country}{body}{kind}"
    return f"https://patents.google.com/patent/{normalized}"


def _is_us_pregrant(country: str, kind: str) -> bool:
    return country == "US" and kind.startswith("A")


def _pad_us_pregrant_serial(body: str) -> str:
    if len(body) == _US_PREGRANT_CANONICAL_LENGTH:
        return body

    year = body[:_US_PREGRANT_YEAR_LENGTH]
    serial = body[_US_PREGRANT_YEAR_LENGTH:]
    padded_serial = serial.zfill(_US_PREGRANT_SERIAL_LENGTH)
    return f"{year}{padded_serial}"
