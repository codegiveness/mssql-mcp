'use strict';

// Run: node scripts/test/check-release-policy.test.js
// Pure validation and real CLI/event entrypoints; no releases or network calls.
const assert = require('assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const { validateReleaseTag, resolveEventTag } = require('../check-release-policy.js');
const SCRIPT = path.join(__dirname, '..', 'check-release-policy.js');

let failures = 0;
function check(name, fn) {
  try {
    fn();
    console.log('ok - ' + name);
  } catch (error) {
    failures++;
    console.error('FAIL - ' + name + ': ' + error.message);
  }
}

check('0.x stable and prerelease tags preserve publish version', () => {
  for (const tag of ['v0.0.0', 'v0.5.5', 'v0.10.123', 'v0.6.0-preview.1', 'v0.6.0-0.alpha+build.1']) {
    assert.deepStrictEqual(validateReleaseTag(tag), { name: tag, version: tag.slice(1) });
  }
});

check('1.x, higher majors, and their release candidates are blocked', () => {
  for (const tag of ['v1.0.0', 'v1.0.0-rc.1', 'v2.0.0', 'v10.2.3', 'v999999999999999999999.0.0']) {
    assert.throws(() => validateReleaseTag(tag), /only 0\.x/);
  }
});

check('malformed and shell/output-injection tags are rejected', () => {
  for (const tag of [undefined, null, '', '0.5.5', 'v0.5', 'v00.5.5', 'v0.05.5',
    'v0.5.05', 'v0.5.5-01', 'v0.5.5-', 'v0.5.5+', 'v0.5.5 preview',
    'v0.5.5\n', 'v0.5.5\nversion=1.0.0', 'v0.5.5; echo injected', 'v0.5.5$(id)']) {
    assert.throws(() => validateReleaseTag(tag), /v-prefixed SemVer/);
  }
});

check('tag pushes use the pushed tag and reject branch pushes', () => {
  const env = { GITHUB_EVENT_NAME: 'push', GITHUB_REF_TYPE: 'tag', GITHUB_REF_NAME: 'v0.5.5', RELEASE_TAG: 'v1.0.0' };
  assert.strictEqual(resolveEventTag(env).version, '0.5.5');
  assert.throws(() => resolveEventTag({ ...env, GITHUB_REF_TYPE: 'branch' }), /tag push/);
  assert.throws(() => resolveEventTag({ ...env, GITHUB_REF_NAME: 'v1.0.0' }), /only 0\.x/);
});

check('manual dispatch uses input, never falls back to the branch or tag ref', () => {
  const env = { GITHUB_EVENT_NAME: 'workflow_dispatch', RELEASE_TAG: 'v0.5.5', GITHUB_REF_NAME: 'v1.0.0' };
  assert.strictEqual(resolveEventTag(env).version, '0.5.5');
  assert.throws(() => resolveEventTag({ ...env, RELEASE_TAG: 'v1.0.0' }), /only 0\.x/);
  assert.throws(() => resolveEventTag({ ...env, RELEASE_TAG: '' }), /SemVer/);
  assert.throws(() => resolveEventTag({ ...env, RELEASE_TAG: undefined }), /SemVer/);
});

check('manual CLI emits publish outputs only for admitted tags', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'release-policy-'));
  try {
    const output = path.join(dir, 'outputs');
    for (const tag of ['v0.5.5', 'v1.0.0', 'v1.0.0-rc.1', 'v0.5.5\n', '']) {
      fs.writeFileSync(output, '');
      const result = spawnSync(process.execPath, [SCRIPT, '--github-event'], {
        encoding: 'utf8',
        env: { ...process.env, GITHUB_EVENT_NAME: 'workflow_dispatch', RELEASE_TAG: tag, GITHUB_OUTPUT: output },
      });
      if (tag === 'v0.5.5') {
        assert.strictEqual(result.status, 0, result.stderr);
        assert.strictEqual(fs.readFileSync(output, 'utf8'), 'name=v0.5.5\nversion=0.5.5\n');
      } else {
        assert.strictEqual(result.status, 1);
        assert.strictEqual(fs.readFileSync(output, 'utf8'), '');
        assert.match(result.stderr, /Release policy:/);
      }
    }
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

check('manifest CLI admits 0.x and blocks forced major or malformed releases', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'release-manifest-'));
  try {
    const manifest = path.join(dir, 'manifest.json');
    for (const version of ['0.5.5', '1.0.0', '1.0.0-rc.1', '0.05.5', null]) {
      fs.writeFileSync(manifest, JSON.stringify({ '.': version }));
      const result = spawnSync(process.execPath, [SCRIPT, '--manifest', manifest], {
        encoding: 'utf8', env: { ...process.env, GITHUB_OUTPUT: '' },
      });
      assert.strictEqual(result.status, version === '0.5.5' ? 0 : 1, result.stderr);
    }
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

check('direct CLI blocks major and malformed tags', () => {
  for (const tag of ['v0.5.5', 'v1.0.0', 'not-a-tag']) {
    const result = spawnSync(process.execPath, [SCRIPT, tag], {
      encoding: 'utf8', env: { ...process.env, GITHUB_OUTPUT: '' },
    });
    assert.strictEqual(result.status, tag === 'v0.5.5' ? 0 : 1, result.stderr);
  }
});

process.exitCode = failures ? 1 : 0;
