'use strict';

// Publication hold approved during modernization. This checks one known restricted
// dependency, not general license compliance. Clear it only after owner review of
// redistribution rights or removal of the dependency from distributed artifacts.
const fs = require('node:fs');
const path = require('node:path');

try {
  const lockPath = process.argv[2] || path.join(__dirname, '..', 'src', 'mssql-mcp', 'packages.lock.json');
  const lock = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
  if (!lock.dependencies || typeof lock.dependencies !== 'object' || Array.isArray(lock.dependencies)) {
    throw new Error('Dependency inventory is invalid; cannot assess the publication hold.');
  }
  const graphs = Object.values(lock.dependencies);
  if (graphs.length === 0) throw new Error('Dependency inventory is empty; cannot assess the publication hold.');
  for (const graph of graphs) {
    if (!graph || typeof graph !== 'object' || Array.isArray(graph) || Object.keys(graph).length === 0) {
      throw new Error('Dependency graph is invalid; cannot assess the publication hold.');
    }
    const broker = graph['Microsoft.Identity.Client.NativeInterop'];
    if (Object.hasOwn(graph, 'Microsoft.Identity.Client.NativeInterop')) {
      throw new Error('Public distribution blocked: Microsoft.Identity.Client.NativeInterop ' +
        (broker?.resolved || '(unknown version)') + ' has not been cleared for redistribution. Its packaged license ' +
        'section 3(e) prohibits distribution. Owner licensing review is required.');
    }
  }
  console.log('Redistribution policy: known native broker hold not present.');
} catch (error) {
  console.error('Redistribution policy: ' + error.message);
  process.exitCode = 1;
}
