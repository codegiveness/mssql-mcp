'use strict';

// Run after ReportGenerator's JsonSummary report:
// node scripts/coverage-summary.js artifacts/coverage/report/Summary.json artifacts/coverage/npm/coverage-summary.json
const fs = require('fs');
const path = require('path');

const criticalSources = [
  ['src/mssql-mcp.Core/Guard/SqlGuard.cs', 'mssql_mcp.Core.Guard.SqlGuard'],
  ['src/mssql-mcp.Core/Logging/PasswordObfuscator.cs', 'mssql_mcp.Core.Logging.PasswordObfuscator'],
  ['src/mssql-mcp.Core/SqlQueryResult.cs', 'mssql_mcp.Core.ResultByteBudget'],
  ['src/mssql-mcp.Core/SqlExecutor.cs', 'mssql_mcp.Core.SqlExecutor'],
  ['src/mssql-mcp.Tools/ToolErrors.cs', 'mssql_mcp.Tools.ToolErrors'],
];

function counts(metric) {
  const fields = ['coveredlines', 'coverablelines', 'coveredbranches', 'totalbranches'];
  const result = {};
  for (const field of fields) {
    if (!Number.isSafeInteger(metric[field]) || metric[field] < 0) {
      throw new Error('Missing or invalid coverage count: ' + field);
    }
    result[field] = metric[field];
  }
  if (result.coveredlines > result.coverablelines || result.coveredbranches > result.totalbranches) {
    throw new Error('Covered counts exceed coverable counts.');
  }
  return result;
}

function ratio(covered, total) {
  return total === 0 ? 'N/A (0/0)' : (100 * covered / total).toFixed(1) + '% (' + covered + '/' + total + ')';
}

function row(label, metric) {
  return '| ' + label + ' | ' + ratio(metric.coveredlines, metric.coverablelines) +
    ' | ' + ratio(metric.coveredbranches, metric.totalbranches) + ' |';
}

function summarize(report, npmCoverage) {
  const overall = counts(report.summary);
  if (overall.coverablelines === 0 || overall.totalbranches === 0) {
    throw new Error('Report contains no production line/branch instrumentation.');
  }
  const assemblies = report.coverage.assemblies;
  for (const name of ['mssql-mcp.Core', 'mssql-mcp.Tools']) {
    if (!assemblies.some(assembly => assembly.name === name)) {
      throw new Error('Missing production assembly: ' + name);
    }
  }
  const classes = assemblies.flatMap(assembly => assembly.classesinassembly);
  const rows = [row('Production Core + Tools', overall)];
  for (const [source, type] of criticalSources) {
    // Nested Guard visitors and async state machines belong to their declaring source.
    const matches = classes.filter(item => item.name === type ||
      ['+', '/', '.'].some(separator => item.name.startsWith(type + separator)));
    if (matches.length === 0) {
      throw new Error('Critical source is absent from coverage: ' + source);
    }
    const total = { coveredlines: 0, coverablelines: 0, coveredbranches: 0, totalbranches: 0 };
    for (const item of matches) {
      for (const [field, value] of Object.entries(counts(item))) total[field] += value;
    }
    if (total.coverablelines === 0 || total.totalbranches === 0) {
      throw new Error('Critical source has no line/branch instrumentation: ' + source);
    }
    rows.push(row('`' + source + '`', total));
  }
  const npmSource = Object.entries(npmCoverage).find(([source]) =>
    source.replace(/\\/g, '/').endsWith('/npm/bin/mssql-mcp.js'));
  if (!npmSource) throw new Error('npm download/cache source is absent from coverage.');
  const npmMetric = npmSource[1];
  const npmCounts = counts({
    coveredlines: npmMetric.lines.covered,
    coverablelines: npmMetric.lines.total,
    coveredbranches: npmMetric.branches.covered,
    totalbranches: npmMetric.branches.total,
  });
  if (npmCounts.coverablelines === 0 || npmCounts.totalbranches === 0) {
    throw new Error('npm download/cache source has no line/branch instrumentation.');
  }
  rows.push(row('`npm/bin/mssql-mcp.js` (download/cache/archive boundaries)', npmCounts));
  return '## Targeted security coverage\n\n' +
    'Measured from live SQL tests and native V8 npm security regressions, not a coverage threshold. Counts show covered/coverable items.\n\n' +
    '| Production source | Line coverage | Branch coverage |\n| --- | --- | --- |\n' +
    rows.join('\n') + '\n\n' +
    'The uploaded coverage artifact includes Cobertura, raw collector data, and HTML source reports with uncovered lines/branches.\n';
}


if (require.main === module) {
  try {
    if (process.argv.length !== 4) {
      throw new Error('Usage: node scripts/coverage-summary.js <ReportGenerator Summary.json> <npm coverage-summary.json>');
    }
    const summaryPath = process.argv[2];
    const markdown = summarize(
      JSON.parse(fs.readFileSync(summaryPath, 'utf8')),
      JSON.parse(fs.readFileSync(process.argv[3], 'utf8'))
    );
    fs.writeFileSync(path.join(path.dirname(summaryPath), 'critical-summary.md'), markdown);
    if (process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, markdown);
    process.stdout.write(markdown);
  } catch (error) {
    console.error('Coverage summary: ' + error.message);
    process.exitCode = 1;
  }
}
