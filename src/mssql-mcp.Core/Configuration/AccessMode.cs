namespace mssql_mcp.Core.Configuration;

/// <summary>
/// Server safety posture selected at startup.
/// Restricted = read-only with layered validation (default).
/// Unrestricted = full DML/DDL access (opt-in).
/// </summary>
public enum AccessMode
{
    Restricted,
    Unrestricted,
}
