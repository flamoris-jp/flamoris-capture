#!/usr/bin/env python3
"""Explicit Nginx route repair; reuses one loaded exact Cutwork Hub location.

Run with sudo on the proxy host. No credentials or screenshots are read.
Refuses ambiguous/nested locations; backs up before test/reload and restores
the original on failure. Does not restart the Hub or rotate its credential.
"""
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time


LOCATION = re.compile(r'(?m)^[ \t]*location\s*=\s*/desktop/cutwork\s*\{[^{}]*\}')
CAPTURE = re.compile(r'(?m)^[ \t]*location\s+(?:=\s*)?/desktop/capture(?:\s|\{)')


def proposed(dump):
    if CAPTURE.search(dump):
        raise RuntimeError('capture_route_exists_review_instead')
    sections = re.split(r'(?m)^# configuration file (.+):\s*$', dump)
    found = [(Path(sections[i]), match.group())
             for i in range(1, len(sections), 2)
             for match in LOCATION.finditer(sections[i + 1])]
    if len(found) != 1:
        raise RuntimeError('expected_one_simple_cutwork_route')
    path, block = found[0]
    if not path.is_absolute() or not re.search(r'proxy_pass\s+http://127\.0\.0\.1:8769\s*;', block):
        raise RuntimeError('unverified_proxy_layout')
    return path.resolve(strict=True), block


def replace(path, data, metadata):
    fd, name = tempfile.mkstemp(prefix='.capture-route-', dir=path.parent)
    try:
        os.fchmod(fd, metadata.st_mode & 0o777)
        os.fchown(fd, metadata.st_uid, metadata.st_gid)
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


def repair(run=subprocess.run, backup_parent='/var/backups'):
    dump = run(['nginx', '-T'], check=True, capture_output=True, text=True).stdout
    path, block = proposed(dump)
    original = path.read_bytes()
    text = original.decode('utf-8')
    if text.count(block) != 1:
        raise RuntimeError('configuration_changed_retry')
    changed = text.replace(block, block + '\n\n' + block.replace('/desktop/cutwork', '/desktop/capture', 1), 1).encode('utf-8')
    metadata = path.stat()
    backup = Path(tempfile.mkdtemp(prefix='flamoris-capture-route-' + time.strftime('%Y%m%d-'), dir=backup_parent))
    (backup / 'original.conf').write_bytes(original)
    (backup / 'original.conf').chmod(0o600)
    replace(path, changed, metadata)
    try:
        run(['nginx', '-t'], check=True)
        run(['systemctl', 'reload', 'nginx'], check=True)
    except BaseException:
        replace(path, original, metadata)
        run(['nginx', '-t'], check=True)
        run(['systemctl', 'reload', 'nginx'], check=True)
        raise RuntimeError('route_failed_original_restored')
    print('Capture route added; Nginx reloaded. Backup: ' + str(backup))


if __name__ == '__main__':
    if os.geteuid() != 0:
        raise SystemExit('Run with sudo.')
    repair()
