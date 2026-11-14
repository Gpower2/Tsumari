"""Check repository Markdown links; optionally inventory every Markdown file, including ignored history.

No network access, builds, deployment, or writes to local settings/databases.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
LINK = re.compile(r'!?\[[^\]\n]*\]\((?:<([^>]+)>|([^\s)]+))(?:\s+"[^"]*")?\)')


def source_files():
    result = subprocess.run(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'],
                            cwd=ROOT, check=True, capture_output=True)
    return {ROOT / p.decode('utf-8') for p in result.stdout.split(b'\0') if p}


def all_markdown():
    for folder, directories, files in os.walk(ROOT):
        directories[:] = [d for d in directories if d != '.git']
        for name in files:
            if name.lower().endswith('.md'):
                yield Path(folder) / name


def anchors(path):
    result, counts = set(), {}
    for line in path.read_text(encoding='utf-8-sig').splitlines():
        match = re.match(r'^#{1,6}\s+(.+?)\s*#*$', line)
        if not match:
            continue
        title = re.sub(r'<[^>]+>', '', match[1]).strip().lower()
        slug = re.sub(r'[^\w\- ]', '', title).replace(' ', '-')
        count = counts.get(slug, 0)
        counts[slug] = count + 1
        result.add(slug + (f'-{count}' if count else ''))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inventory', type=Path)
    args = parser.parse_args()
    sources = source_files()
    files = sorted(all_markdown())
    inventory, errors = [], []
    for path in files:
        data = path.read_bytes()
        text = data.decode('utf-8-sig')
        maintained = path in sources
        inventory.append(dict(path=path.relative_to(ROOT).as_posix(), bytes=len(data),
                              sha256=hashlib.sha256(data).hexdigest(), ignored=not maintained,
                              headings=re.findall(r'^#{1,6}\s+.+$', text, flags=re.M)))
        # Ignored repair reports and build/private snapshots are historical evidence, not maintained docs.
        if not maintained:
            continue
        for match in LINK.finditer(text):
            target = match[1] or match[2]
            url = urlsplit(target)
            if url.scheme or target.startswith('//'):
                continue
            destination = (path.parent / unquote(url.path)).resolve() if url.path else path
            line = text.count('\n', 0, match.start()) + 1
            if not destination.exists():
                errors.append(f'{path.relative_to(ROOT)}:{line}: missing local link: {target}')
            elif url.fragment and destination.suffix.lower() == '.md' and unquote(url.fragment) not in anchors(destination):
                errors.append(f'{path.relative_to(ROOT)}:{line}: missing heading: {target}')
    if args.inventory:
        args.inventory.parent.mkdir(parents=True, exist_ok=True)
        args.inventory.write_text(json.dumps(inventory, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    for error in errors:
        print(error)
    print(f'Read {len(files)} Markdown files: {sum(not i["ignored"] for i in inventory)} maintained, '
          f'{sum(i["ignored"] for i in inventory)} ignored historical/private files; {len(errors)} link errors.')
    return 1 if errors else 0


if __name__ == '__main__':
    raise SystemExit(main())
