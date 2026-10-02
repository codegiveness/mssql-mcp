'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const script = path.join(__dirname, '..', 'check-redistribution.js');
const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'redistribution-policy-'));
const inventory = path.join(directory, 'packages.lock.json');

function run(value) {
  fs.writeFileSync(inventory, JSON.stringify(value));
  return spawnSync(process.execPath, [script, inventory], { encoding: 'utf8' });
}

try {
  const ordinary = { 'Microsoft.Data.SqlClient': { resolved: '7.1.1' } };
  assert.equal(run({ dependencies: { 'net10.0': ordinary } }).status, 0);
  console.log('ok - inventory without the held component is admitted');

  for (const broker of [{ resolved: '0.20.6' }, null]) {
    const result = run({ dependencies: {
      'net10.0': ordinary,
      'net10.0/linux-x64': { ...ordinary, 'Microsoft.Identity.Client.NativeInterop': broker },
    } });
    assert.equal(result.status, 1);
    assert.equal(result.stdout, '');
  }
  console.log('ok - held dependency in any graph or version cannot authorize publication');

  for (const value of [{}, { dependencies: [] }, { dependencies: {} },
    { dependencies: { 'net10.0': null } }, { dependencies: { 'net10.0': {} } },
    { dependencies: { 'net10.0': 'invalid' } }]) {
    assert.equal(run(value).status, 1);
  }
  fs.writeFileSync(inventory, '{');
  assert.equal(spawnSync(process.execPath, [script, inventory]).status, 1);
  fs.rmSync(inventory);
  assert.equal(spawnSync(process.execPath, [script, inventory]).status, 1);
  console.log('ok - missing, malformed, and empty inventories fail closed');
} finally {
  fs.rmSync(directory, { recursive: true, force: true });
}
