# Return shape: lean array of objects with stringified big numbers; non-rowset status objects

`execute_sql` (and any tool returning rowsets) returns a JSON array of objects as `TextContent` — column names as keys, values coerced per a fixed type rule. No schema envelope, no columnar layout, no markdown for data tools.

**Non-rowset return shape** (DDL/DML in Unrestricted mode): DDL and DML statements don't return rowsets; returning `[]` is ambiguous (could mean "no rows matched" or "statement succeeded"). In Unrestricted mode, `execute_sql` returns a JSON array containing a single status object:

- DML: `[{"result": "success", "statement_type": "UPDATE", "rows_affected": 42}]`
- DDL: `[{"result": "success", "statement_type": "CREATE_TABLE", "object": "dbo.NewTable"}]`

This stays a JSON array of objects (does not violate the lean-array contract). `statement_type` is derived from the ScriptDom AST of the parsed statement (the Guard already parses it). `rows_affected` comes from `SqlDataReader.RecordsAffected`.

## Type coercion rule

| SQL type | JSON representation |
|---|---|
| `int`, `smallint`, `tinyint`, `bit` | JSON number |
| `bigint` | **string** (avoids 2^53 precision loss) |
| `decimal`, `numeric`, `money`, `smallmoney` | **string** (preserves precision) |
| `real`, `float` | JSON number (IEEE 754, exact) |
| `date`, `datetime`, `datetime2`, `smalldatetime`, `datetimeoffset`, `time` | ISO 8601 string |
| `uniqueidentifier` | string (canonical 8-4-4-4-12) |
| `varbinary`, `binary`, `image` | base64 string |
| `char`, `varchar`, `nchar`, `nvarchar`, `text`, `ntext` | JSON string |
| `geography`, `geometry`, `hierarchyid`, `xml` | string (`.ToString()`) |
| `NULL` | JSON `null` |

## Considered Options

- **A. Lean array of objects** ✅ — chosen. Smallest token footprint for typical results. Agents already trained on this shape.
- B. Typed envelope (`{columns, rowCount, rows}`) — rejected: columnar layout only wins on wide results (50+ cols, rare); schema metadata rarely used by agents who infer from data.
- C. Markdown table only — rejected: loses type info (agent can't tell `1` from `"1"`).
- D. Hybrid (JSON for data tools, markdown for discovery) — rejected: inconsistent shape across tools increases agent cognitive overhead beyond token savings.

## Consequences

- `bigint` and `decimal` arrive as strings — agents must cast when doing arithmetic. This is the standard pattern (Postgres `numeric` is stringified in JSON too); document in tool description.
- Empty result set returns `[]` (not `null` or an error).
- Single-row results still return an array (consistent shape; agent indexes `[0]`).
- Discovery tools (`list_databases`, `list_objects`, etc.) follow the same shape — array of objects with column names as keys.
- Unrestricted-mode DDL/DML returns status objects (not `[]`) to disambiguate success from no-rows.
- Multi-statement batches in Unrestricted mode return one status object per parsed statement. `rows_affected` is `-1` for each when more than one statement ran (SqlClient's `ExecuteNonQueryAsync` returns cumulative count, which cannot be attributed per-statement). Single-statement batches report the actual `rows_affected`.
- `get_object_details` is an exception: on zero rows, it returns a structured error object (not `[]`) per ADR-0010 — empty arrays are ambiguous for object lookup.
