"""Delimiters for untrusted requester text (plan §10.13, Lab 07 Part 5).

Delimiters only help (an attacker can type the closing tag), so wrap_notes first strips every tag
variant from the user text. The real defences are in code: V01–V12, per-agent tool allow-lists and
the human gate.
"""

import re

OPEN_TAG = "<requester_notes>"
CLOSE_TAG = "</requester_notes>"

# <requester_notes>, </requester_notes>, < /Requester_Notes >, <requester_notes attr="x"> ...
_TAG = re.compile(r"<\s*/?\s*requester_notes\b[^>]*>", re.IGNORECASE)


def wrap_notes(text: str | None) -> str:
    """Strip requester_notes tags from user text and wrap it: notes are data, never instructions."""
    if text is None:
        return ""
    cleaned = _TAG.sub("", text).strip()
    if not cleaned:
        return ""
    return f"{OPEN_TAG}\n{cleaned}\n{CLOSE_TAG}"
