#!/usr/bin/env python3
"""Compatibility entry point for the canonical runtime pool validator."""
from pathlib import Path
import sys

workspace = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(workspace / 'agent_pool' / 'scripts'))
from pool import available, check, load

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
try:
    payload = load()
    byid = check(payload)
    ready = [t['id'] for t in payload['tasks'] if available(t, byid)]
    print(f"OK: {len(byid)} tasks; DAG/state/evidence valid. Free ready: {', '.join(ready) or 'none'}")
except (ValueError, KeyError, OSError) as exc:
    print(f'ERROR: {exc}', file=sys.stderr)
    sys.exit(1)
