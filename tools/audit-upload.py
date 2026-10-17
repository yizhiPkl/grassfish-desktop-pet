"""Check the exact upload tree without printing credentials or private values.

Use --staged after git add to inspect index bytes, not just working files.
Reports contain paths/rule names/hashes only. No network access is performed.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import zipfile

ap = argparse.ArgumentParser()
ap.add_argument('--root', type=Path, default=Path('.'))
ap.add_argument('--output', type=Path)
ap.add_argument('--staged', action='store_true')
ap.add_argument('--git', default='git')
args = ap.parse_args()
root = args.root.resolve()
issues, checked, archive_files = [], 0, 0
hashes = {}
skip = {'.git', 'release', 'dist', 'audit-results', '__pycache__', '.vs'}
text_ext = {'.cs', '.md', '.ps1', '.iss', '.isl', '.json', '.html', '.js', '.css', '.txt', '.py', '.config', '.manifest'}
private_names = {'auth.json', 'settings.json', 'settings.json.bak', 'codex-connection.json', 'session_index.jsonl', 'task-health.json', 'update-state.json', 'update-ticket.json'}
private_parts = {'vendor', 'qa-profile', 'native-qa-profile', 'webview-profile', 'components-cache', 'codex-components', 'native-task-webview', 'node_modules', '本机记录', '验证'}
rules = {
    'private-key': re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
    'rsa-private-xml': re.compile(r'<(?:D|P|Q|DP|DQ|InverseQ)>[A-Za-z0-9+/=]{8,}</'),
    'github-token': re.compile(r'(?:github_pat_|gh[pousr]_)[A-Za-z0-9_]{20,}'),
    'api-secret': re.compile(r'\bsk-[A-Za-z0-9_-]{24,}'),
    'personal-session-id': re.compile(r'\b01[0-9a-f]{6}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b', re.I),
    'user-absolute-path': re.compile(r'[A-Za-z]:[/\\]+Users[/\\]+([^/\\\s"\']+)', re.I),
}

def fail(path, rule):
    issues.append({'path': path, 'rule': rule})

def safe_path(name):
    path = PurePosixPath(name.replace('\\', '/'))
    return not path.is_absolute() and '..' not in path.parts and ':' not in name

def inspect(name, data, nested=False):
    global checked
    checked += 1
    path = PurePosixPath(name)
    lower = path.name.lower()
    if not safe_path(name): fail(name, 'unsafe-path')
    if lower in private_names or lower.startswith('.env') and lower != '.env.example': fail(name, 'personal-config')
    if any(part.lower() in private_parts for part in path.parts): fail(name, 'private-or-upstream-directory')
    if path.suffix.lower() in {'.pfx', '.p12', '.pem', '.key', '.jsonl', '.log', '.lnk'}: fail(name, 'private-file-type')
    if len(data) > 100 * 1024 * 1024: fail(name, 'oversized-git-file')
    if path.suffix.lower() in text_ext or lower in {'.gitignore', '.gitattributes'}:
        try: text = data.decode('utf-8-sig')
        except UnicodeDecodeError:
            fail(name, 'non-utf8-text'); return
        for rule, pattern in rules.items():
            for match in pattern.finditer(text):
                # A synthetic test path, not a developer profile.
                if rule == 'user-absolute-path' and path.name == 'DistributionTests.cs' and match.group(1) == 'another': continue
                fail(name, rule); break
        if path.suffix.lower() == '.json':
            try:
                doc = json.loads(text)
                if isinstance(doc, dict) and doc.get('contextThreadId'): fail(name, 'nonempty-conversation-pairing')
            except ValueError: fail(name, 'invalid-json')
    elif path.suffix.lower() not in {'.png', '.ico', '.dll', '.exe', '.zip'}:
        fail(name, 'unreviewed-file-type')

if args.staged:
    names = subprocess.check_output([args.git, '-C', str(root), 'ls-files', '--cached', '-z']).decode('utf-8').split('\0')
    names = [name for name in names if name]
    def read(name): return subprocess.check_output([args.git, '-C', str(root), 'show', ':' + name])
else:
    names = []
    for file in root.rglob('*'):
        rel = file.relative_to(root)
        if any(part in skip for part in rel.parts): continue
        if file.is_symlink(): fail(rel.as_posix(), 'symlink'); continue
        if file.is_file(): names.append(rel.as_posix())
    def read(name): return (root / name).read_bytes()

for name in sorted(names):
    data = read(name)
    inspect(name, data)
    hashes[name] = hashlib.sha256(data).hexdigest()
    if name.startswith('downloads/') and name.endswith('.zip'):
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            total = 0
            for entry in archive.infolist():
                if entry.is_dir(): continue
                total += entry.file_size
                if total > 128 * 1024 * 1024: fail(name, 'oversized-archive'); break
                if not safe_path(entry.filename): fail(name, 'unsafe-archive-entry'); continue
                inspect(entry.filename, archive.read(entry), True)
                archive_files += 1

manifest = 'downloads/SHA256SUMS.txt'
if manifest in names:
    for line in read(manifest).decode('utf-8-sig').splitlines():
        expected, file = line.split('  ', 1)
        if not safe_path(file) or hashes.get('downloads/' + file) != expected.lower(): fail(manifest, 'release-checksum-mismatch')
else: fail(manifest, 'release-checksums-missing')

for needed in ['README.md', '.gitignore', 'src/Program.cs', 'assets/native-task-ui/provenance.json', 'third-party/WebView2-LICENSE.txt', 'third-party/WebView2-NOTICE.txt']:
    if needed not in names: fail(needed, 'required-file-missing')

report = {'ok': not issues, 'mode': 'staged-index' if args.staged else 'working-tree', 'files': len(names), 'checkedIncludingArchiveEntries': checked, 'archiveEntries': archive_files, 'issues': issues, 'sha256': hashes}
if args.output:
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({key: val for key, val in report.items() if key != 'sha256'}, ensure_ascii=False))
raise SystemExit(0 if report['ok'] else 1)
