'use strict';

// Deliberately small YAML-frontmatter subset used by the knowledge tooling (ported from zonio_app tools/_lib, 2026-10-11).
// It supports scalar values plus block/inline scalar lists and rejects nested
// mappings instead of silently interpreting them incorrectly.

function unquote(value) {
  const trimmed = value.trim();
  if (trimmed.length >= 2 && trimmed[0] === trimmed[trimmed.length - 1]
      && (trimmed[0] === '"' || trimmed[0] === "'")) {
    const inner = trimmed.slice(1, -1);
    return trimmed[0] === '"'
      ? inner.replace(/\\n/g, '\n').replace(/\\"/g, '"').replace(/\\\\/g, '\\')
      : inner.replace(/''/g, "'");
  }
  return trimmed;
}

function parseInlineList(value) {
  const trimmed = value.trim();
  if (!trimmed.startsWith('[') || !trimmed.endsWith(']')) return null;
  const body = trimmed.slice(1, -1).trim();
  if (!body) return [];
  return body.split(',').map((item) => unquote(item)).filter(Boolean);
}

function parseFrontmatter(text) {
  const match = text.match(/^---\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)/);
  if (!match) return { data: {}, errors: ['missing YAML frontmatter'] };
  const data = {};
  const errors = [];
  let currentKey = null;
  match[1].split(/\r?\n/).forEach((line, idx) => {
    if (!line.trim() || /^\s*#/.test(line)) return;
    const keyValue = line.match(/^([A-Za-z_][A-Za-z0-9_-]*):\s*(.*)$/);
    if (keyValue) {
      const [, key, raw] = keyValue;
      if (Object.prototype.hasOwnProperty.call(data, key)) {
        errors.push(`duplicate frontmatter key '${key}'`);
      }
      currentKey = key;
      if (!raw.trim()) data[key] = [];
      else data[key] = parseInlineList(raw) ?? unquote(raw);
      return;
    }
    const listItem = line.match(/^\s+-\s+(.+)$/);
    if (listItem && currentKey) {
      if (!Array.isArray(data[currentKey])) {
        errors.push(`key '${currentKey}' mixes scalar and list values`);
        data[currentKey] = [];
      }
      data[currentKey].push(unquote(listItem[1]));
      return;
    }
    errors.push(`unsupported frontmatter syntax on line ${idx + 2}`);
  });
  return { data, errors };
}

module.exports = { parseFrontmatter };
