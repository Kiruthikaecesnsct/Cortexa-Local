import re

# Split any run of two or more '=' or '-' with spaces, so no '==' or '--'
# substring can survive. A plain str.replace('==', '= =') re-forms the marker at
# the substitution seam on runs of 3+ ('===' -> '= =='), leaving a forged header
# intact; splitting the whole run defuses every length.
_MARKER_RUN = re.compile(r"[=-]{2,}")


def _defuse_marker_run(match: re.Match[str]) -> str:
    return " ".join(match.group())


def neutralize_untrusted(value: str) -> str:
    """Make untrusted source text safe to embed in a prompt.

    Uploaded papers, theses, and code legitimately contain newlines and dashes,
    so rejecting them is wrong. Instead collapse all whitespace to single spaces
    — so injected content cannot start a new logical prompt line — and break up
    the '==' / '--' section markers so it cannot forge a header.

    Copied per service on purpose: the monorepo forbids shared libraries, so
    evidence/seeding/scoring each keep their own copy of this small helper.
    """
    collapsed = " ".join(value.split())
    return _MARKER_RUN.sub(_defuse_marker_run, collapsed)
