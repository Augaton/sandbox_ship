#!/usr/bin/env python3
"""The game's French texts, and their English translations in Game/lang/en.po.

  python3 godot/tools/lang.py --list     every text the code shows, as the catalog keys it
  python3 godot/tools/lang.py --check    those the catalog lacks (exit 1 if any)
  python3 godot/tools/lang.py --add      adds them to en.po, with an empty translation

A text interpolated in the code ($"Coque {a} / {b}") is keyed with numbered holes
("Coque {0} / {1}"): the game matches the French text it shows against these templates
(Game/scripts/Core/Lang.cs). Logs, file paths and identifiers are left out.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCES = [ROOT / "Game" / "scripts", ROOT / "Sim"]
PO = ROOT / "Game" / "lang" / "en.po"

FRENCH = re.compile(
    r"[àâçéèêëîïôûùüÿœÀÂÇÉÈÊÎÔÛ«»]|\b(le|la|les|des|du|de|une|un|est|pour|sur|dans|avec|sans|plus|tes|ton|ta|ses|son|sa"
    r"|aux|au|et|ou|par|qui|que|pas|ne|en|à)\b", re.I)
# A word of the interface on its own: "Options", "Pause", "Retour".
WORD = re.compile(r"^[A-ZÀ-Ý][a-zà-ÿ'’-]+(?: [a-zà-ÿ'’-]+)*[.!?…]?$")
# Lines whose texts are for logs, errors no player reads, or the developer.
SKIP_LINE = re.compile(
    r"GD\.Print|GD\.PushWarning|GD\.PushError|PrintErr|\bLog\(|Console\.|throw new|Debug\.|nameof\(|\bDev\.|"
    r"FormatException|InvalidDataException|ArgumentException|InvalidOperationException")
IDENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_.:/-]*$")
# Capitalized words of the code, not of the interface: Godot's types and the like.
CODE_WORDS = {"Button", "Label", "LineEdit", "Panel", "PanelContainer", "PopupMenu", "Linux", "Windows", "Mono", "Bold",
              "Default", "Master", "Dummy", "Shader", "Text", "Control", "Node"}
# Texts no player reads: shaders, date formats, the measuring probe, dev and server traces.
IGNORE = re.compile(r"shader_type|^dd/MM|HH:mm|^battle process$|musique prête|^avant la musique$")
CHAR = re.compile(r"'(\\.|[^\\'])'")


def literals(text):
    """(interpolated, value, start of its line) for each string literal of C# source, comments skipped."""
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i)
            i = n if j < 0 else j + 2
            continue
        if c == "'":
            # A char literal: '"' must not open a string.
            m = CHAR.match(text, i)
            i += len(m.group(0)) if m else 1
            continue
        m = OPEN.match(text, i)
        if m:
            prefix = m.group(1) or ""
            j = m.end()
            nested.clear()
            value, i = read(text, j, "$" in prefix, "@" in prefix)
            value, i = chain(text, value, i)
            line = text.rfind("\n", 0, j)
            yield "$" in prefix, value, line
            # Texts inside its holes: {(n > 0 ? $", +{n}/s de la station" : "")}.
            for inner in list(nested):
                yield True, inner, line
            continue
        i += 1


# The texts met inside the holes of the literal being read.
nested = []


OPEN = re.compile(r'(\$@|@\$|\$|@)?"')
PLUS = re.compile(r"\s*\+\s*")
# A value concatenated between texts: a name, a member, a call or an index, no text inside.
TERM = re.compile(r'[A-Za-z_]\w*(?:\([^()"]*\)|\[[^\]"]*\])?(?:\.[A-Za-z_]\w*(?:\([^()"]*\)|\[[^\]"]*\])?)*')


def chain(text, value, i):
    """A text concatenated with others ("a " + b + " c") reads as one: the values become holes."""
    while True:
        m = PLUS.match(text, i)
        if not m:
            return value, i
        k = m.end()
        o = OPEN.match(text, k)
        holes = len(re.findall(r"\{\d+\}", value))
        if o:
            p = o.group(1) or ""
            more, k = read(text, o.end(), "$" in p, "@" in p)
            value += re.sub(r"\{(\d+)\}", lambda h: "{%d}" % (int(h.group(1)) + holes), more)
            i = k
            continue
        t = TERM.match(text, k)
        if not t:
            return value, i
        value += "{%d}" % holes
        i = t.end()


def read(text, j, interp, verbatim):
    """The literal from j (just after its opening quote) to its closing quote; holes as {0}, {1}..."""
    out, holes, n = [], 0, len(text)
    while j < n:
        c = text[j]
        if verbatim and c == '"' and j + 1 < n and text[j + 1] == '"':
            out.append('"')
            j += 2
            continue
        if c == '"':
            return "".join(out), j + 1
        if c == "\\" and not verbatim:
            esc = text[j + 1]
            if esc == "u":
                out.append(chr(int(text[j + 2:j + 6], 16)))
                j += 6
                continue
            out.append({"n": "\n", "t": "\t", '"': '"', "\\": "\\", "'": "'", "0": "\0"}.get(esc, esc))
            j += 2
            continue
        if interp and c == "{":
            if text.startswith("{{", j):
                out.append("{")
                j += 2
                continue
            depth, k = 1, j + 1
            while k < n and depth:
                m = OPEN.match(text, k)
                if m:
                    p = m.group(1) or ""
                    inner, k = read(text, m.end(), "$" in p, "@" in p)
                    nested.append(inner)
                    continue
                if text[k] == "'":
                    m = CHAR.match(text, k)
                    k += len(m.group(0)) if m else 1
                    continue
                if text[k] == "{":
                    depth += 1
                elif text[k] == "}":
                    depth -= 1
                k += 1
            out.append("{%d}" % holes)
            holes += 1
            j = k
            continue
        if interp and text.startswith("}}", j):
            out.append("}")
            j += 2
            continue
        out.append(c)
        j += 1
    return "".join(out), j


def shown(value):
    """Whether a literal reads as text for a player."""
    letters = re.sub(r"\{\d+\}", "", value)
    if IGNORE.search(value) or len(letters.strip()) < 2 or not re.search(r"[A-Za-zÀ-ÿ]{2}", letters):
        return False
    # A capitalized word alone is the interface's ("Pause", "Options"), not an identifier.
    if WORD.match(value.strip()) and value not in CODE_WORDS:
        return True
    # A key of the code: no space, holes among letters and signs ("seat:{0}:kind", "margin_{0}").
    if " " not in value and re.match(r"^[\w{}:.|-]+$", value) and re.search(r"[:_{|]", value):
        return False
    if IDENT.match(value) or "://" in value or value.startswith("user:") \
            or value.endswith((".json", ".png", ".svg", ".wav", ".po", ".cs")):
        return False
    # Two words or more: a phrase, whatever its words; or a word with a value ("Astuce : {0}").
    words = re.findall(r"[A-Za-zÀ-ÿ'’]{2,}", letters)
    phrase = (len(words) >= 2 and " " in letters.strip()) or (letters != value and any(len(w) >= 3 for w in words))
    return bool(FRENCH.search(letters) or WORD.match(value.strip()) or phrase)


def texts():
    found = {}
    for src in SOURCES:
        for f in sorted(src.rglob("*.cs")):
            if "/bin/" in str(f) or "/obj/" in str(f):
                continue
            text = f.read_text(encoding="utf-8")
            for _, value, line_start in literals(text):
                end = text.find("\n", line_start + 1)
                line = text[line_start + 1:end if end >= 0 else len(text)]
                if SKIP_LINE.search(line) or not shown(value):
                    continue
                found.setdefault(value, str(f.relative_to(ROOT)))
    return found


def unquote(s):
    out, i = [], 1
    while i < len(s) - 1:
        if s[i] == "\\":
            out.append({"n": "\n", "t": "\t", '"': '"', "\\": "\\"}.get(s[i + 1], s[i + 1]))
            i += 2
        else:
            out.append(s[i])
            i += 1
    return "".join(out)


def po_entries():
    entries, msgid, msgstr, cur = {}, None, None, None
    if not PO.exists():
        return entries
    for line in PO.read_text(encoding="utf-8").splitlines():
        if line.startswith("msgid "):
            if msgid is not None:
                entries[msgid] = msgstr
            msgid, msgstr, cur = unquote(line[6:]), "", "id"
        elif line.startswith("msgstr "):
            msgstr, cur = unquote(line[7:]), "str"
        elif line.startswith('"'):
            if cur == "id":
                msgid += unquote(line)
            elif cur == "str":
                msgstr += unquote(line)
    if msgid is not None:
        entries[msgid] = msgstr
    entries.pop("", None)
    return entries


def quote(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n").replace("\t", "\\t") + '"'


def main():
    found = texts()
    po = po_entries()
    if "--list" in sys.argv:
        for k, f in found.items():
            print(f"{f}\t{k!r}")
        print(len(found), "texts", file=sys.stderr)
    missing = [k for k in found if not po.get(k)]
    if "--check" in sys.argv:
        for k in missing:
            print(f"{found[k]}\t{k!r}")
        print(f"{len(missing)} texts without a translation, of {len(found)}", file=sys.stderr)
        sys.exit(1 if missing else 0)
    if "--prune" in sys.argv:
        # Untranslated entries the code no longer shows; translated ones stay (texts made at run time).
        text = PO.read_text(encoding="utf-8")
        blocks = re.split(r"\n(?=#: |msgid )", text)
        kept = []
        for b in blocks:
            m = re.search(r'^msgid (".*")$', b, re.M)
            s = re.search(r'^msgstr (".*")$', b, re.M)
            if m and s and unquote(m.group(1)) and not unquote(s.group(1)) and unquote(m.group(1)) not in found:
                continue
            kept.append(b)
        PO.write_text("\n".join(kept), encoding="utf-8")
        print(len(blocks) - len(kept), "pruned", file=sys.stderr)
    if "--add" in sys.argv:
        new = [k for k in found if k not in po]
        with PO.open("a", encoding="utf-8") as out:
            for k in new:
                out.write(f"\n#: {found[k]}\nmsgid {quote(k)}\nmsgstr \"\"\n")
        print(len(new), "added", file=sys.stderr)


if __name__ == "__main__":
    main()
