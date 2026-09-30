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


# Any other markup-looking tag (<b>, </system>, <tool_call x="y">); "< 8000" is not a tag.
_MARKUP = re.compile(r"<\s*/?\s*[A-Za-z][\w:-]*[^<>]*>")


def strip_markup(text: str) -> str:
    """Our delimiters and any other tag removed, whitespace collapsed: plain text."""
    return " ".join(_MARKUP.sub(" ", strip_tags(text)).split())


def plain_text(text: str, limit: int) -> str:
    """strip_markup, then cut to limit characters (at the last space when there is one nearby)."""
    cleaned = strip_markup(text)
    if len(cleaned) <= limit:
        return cleaned
    cut = cleaned[:limit]
    space = cut.rfind(" ")
    return (cut[:space] if space > limit - 40 else cut).rstrip(" ,;:")


def wrap_officer_notes(text: str | None) -> str:
    """The officer's revise notes for the planner, in their own delimiter (preferences only)."""
    cleaned = strip_tags(text or "").strip()
    if not cleaned:
        return ""
    return f"{OFFICER_OPEN_TAG}\n{cleaned}\n{OFFICER_CLOSE_TAG}"
