'use strict';

// Shared 0.x release gate for tag pushes, workflow_dispatch, and the manifest.
// Run: node scripts/check-release-policy.js v0.5.5
//      node scripts/check-release-policy.js --manifest [manifest-path]
//      node scripts/check-release-policy.js --github-event
// The event mode emits validated name/version outputs only after accepting the tag.

const fs = require('fs');
const path = require('path');

function validateReleaseTag(tag) {
  // SemVer identifiers: numeric prerelease identifiers cannot have leading zeroes.
  const number = '(?:0|[1-9][0-9]*)';
  const prerelease = '(?:' + number + '|[0-9]*[A-Za-z-][0-9A-Za-z-]*)';
  const semver = new RegExp('^v(' + number + ')\\.' + number + '\\.' + number +
    '(?:-' + prerelease + '(?:\\.' + prerelease + ')*)?' +
    '(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$');
  const match = typeof tag === 'string' ? semver.exec(tag) : null;
  // JavaScript $ can match before a final newline; tags must match every byte.
  if (!match || match[0] !== tag) {
    throw new Error('Release tag must be v-prefixed SemVer (for example v0.5.5).');
  }
  if (match[1] !== '0') {
    throw new Error('Release policy permits only 0.x versions; major versions >= 1 are blocked.');
  }
  return { name: tag, version: tag.slice(1) };
}

function resolveEventTag(env) {
  if (env.GITHUB_EVENT_NAME === 'workflow_dispatch') {
    // Missing manual input must not silently release the branch/ref instead.
    return validateReleaseTag(env.RELEASE_TAG);
  }
  if (env.GITHUB_EVENT_NAME === 'push' && env.GITHUB_REF_TYPE === 'tag') {
    return validateReleaseTag(env.GITHUB_REF_NAME);
  }
  throw new Error('Release requires a tag push or workflow_dispatch with a release tag.');
}

function main(args, env) {
  let release;
  if (args[0] === '--manifest' && args.length <= 2) {
    const manifestPath = args[1] || path.join(__dirname, '..', '.release-please-manifest.json');
    const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
    release = validateReleaseTag(typeof manifest['.'] === 'string' ? 'v' + manifest['.'] : null);
  } else if (args.length === 1 && args[0] === '--github-event') {
    release = resolveEventTag(env);
  } else if (args.length === 1) {
    release = validateReleaseTag(args[0]);
  } else {
    throw new Error('Usage: node scripts/check-release-policy.js <tag> | --manifest [path] | --github-event');
  }
  if (env.GITHUB_OUTPUT) {
    fs.appendFileSync(env.GITHUB_OUTPUT, 'name=' + release.name + '\nversion=' + release.version + '\n');
  }
  console.log('Release policy: accepted ' + release.name + '.');
}

module.exports = { validateReleaseTag, resolveEventTag };

if (require.main === module) {
  try {
    main(process.argv.slice(2), process.env);
  } catch (error) {
    console.error('Release policy: ' + error.message);
    process.exitCode = 1;
  }
}
