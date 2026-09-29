"""Delimiters for untrusted requester text (plan §10.13, Lab 07 Part 5).

Delimiters only help (an attacker can type the closing tag), so wrap_notes first strips every tag
variant from the user text. The real defences are in code: V01–V12, per-agent tool allow-lists and
the human gate.
"""

import re

OPEN_TAG = "<requester_notes>"
CLOSE_TAG = "</requester_notes>"
OFFICER_OPEN_TAG = "<officer_revision_notes>"
OFFICER_CLOSE_TAG = "</officer_revision_notes>"

# <requester_notes>, </requester_notes>, < /Requester_Notes >, <requester_notes attr="x"> ...
# Both delimiters: text in one block must not open or close the other.
_ANY_TAG = re.compile(r"<\s*/?\s*(requester_notes|officer_revision_notes)\b[^>]*>", re.IGNORECASE)


def wrap_notes(text: str | None) -> str:
    """Strip requester_notes tags from user text and wrap it: notes are data, never instructions."""
    if text is None:
        return ""
    cleaned = strip_tags(text).strip()
    if not cleaned:
        return ""
    return f"{OPEN_TAG}\n{cleaned}\n{CLOSE_TAG}"


def strip_tags(text: str) -> str:
    """Remove every requester_notes / officer_revision_notes tag variant."""
    return _ANY_TAG.sub("", text)


def wrap_officer_notes(text: str | None) -> str:
    """The officer's revise notes for the planner, in their own delimiter (preferences only)."""
    cleaned = strip_tags(text or "").strip()
    if not cleaned:
        return ""
    return f"{OFFICER_OPEN_TAG}\n{cleaned}\n{OFFICER_CLOSE_TAG}"
