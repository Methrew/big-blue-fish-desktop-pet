# Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
"""Print the edition identity from CompanionEdition.cs, the single source the packaging scripts share.

Usage: Get-EditionIdentity.py [--xml] NAME [NAME...]   one value per line (--xml: escaped for XML text)
       Get-EditionIdentity.py --json                    every constant as a JSON object
"""
import html
import json
import pathlib
import re
import sys

SOURCE = pathlib.Path(__file__).resolve().parents[1] / "code/BigBlueFish.Windows/Companion/CompanionEdition.cs"
CONSTANT = re.compile(r'^\s*internal\s+const\s+string\s+(\w+)\s*=')
TOKEN = re.compile(r'\s*(?:"((?:[^"\\]|\\.)*)"|(\w+)|(\+)|(;))')


def load(path=SOURCE):
    values = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        match = CONSTANT.match(line)
        if not match:
            continue
        name, parts, position, finished = match.group(1), [], match.end(), False
        while not finished:
            token = TOKEN.match(line, position)
            if not token:
                sys.exit("unsupported expression for " + name + ": " + line.strip())
            literal, reference, _, end = token.groups()
            if literal is not None:
                parts.append(json.loads('"' + literal + '"'))
            elif reference is not None:
                if reference not in values:
                    sys.exit("unknown constant " + reference + " in " + name)
                parts.append(values[reference])
            finished = end is not None
            position = token.end()
        values[name] = "".join(parts)
    return values


def main(arguments):
    values = load()
    if arguments == ["--json"]:
        print(json.dumps(values, ensure_ascii=False, indent=2))
        return 0
    escape = arguments[:1] == ["--xml"]
    if escape:
        arguments = arguments[1:]
    if not arguments:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    missing = [name for name in arguments if name not in values]
    if missing:
        print("unknown identity value: " + ", ".join(missing), file=sys.stderr)
        return 2
    for name in arguments:
        print(html.escape(values[name], quote=False) if escape else values[name])
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
