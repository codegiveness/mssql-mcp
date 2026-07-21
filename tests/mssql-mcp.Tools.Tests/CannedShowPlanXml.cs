namespace mssql_mcp.Tools.Tests;

/// <summary>
/// Canned SHOWPLAN_XML strings used by <see cref="ExplainQueryTests"/>. Modeled on real
/// SQL Server output for a simple <c>SELECT * FROM sys.objects</c>-style query. The XML
/// uses the showplan namespace <c>http://schemas.microsoft.com/sqlserver/2004/07/showplan</c>
/// and exercises <c>&lt;QueryPlan&gt;</c>, <c>&lt;RelOp&gt;</c> (multiple, with EstimateCPU),
/// <c>&lt;MissingIndex&gt;</c>, and <c>&lt;Warnings&gt;</c> elements so the summary parser
/// can be verified field-by-field.
/// </summary>
internal static class CannedShowPlanXml
{
    public const string ShowPlanNamespace = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    /// <summary>
    /// Plan with three RelOp nodes (Index Scan + Clustered Index Seek + Nested Loops Join).
    /// Total estimated cost (sum of top-level RelOp EstimateCPU + EstimateIO) = 0.0065.
    /// Missing index on [AppDb].[dbo].[Orders].[IX_Orders_Status] with impact 95.0.
    /// One warning (NO_JOIN_PREDICATE).
    /// </summary>
    public const string FullPlan = """
        <?xml version="1.0" encoding="UTF-16"?>
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.5" Build="16.0.1000.6">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple StatementText="SELECT * FROM dbo.Orders WHERE Status = 'PENDING'" StatementId="1" StatementCompId="1" StatementType="SELECT" StatementSubTreeCost="0.0065" StatementEstRows="100">
                  <QueryPlan CachedPlanSize="24" CompileTime="1" CompileCPU="1" CompileMemory="184">
                    <MissingIndexes>
                      <MissingIndexGroup Impact="95.0">
                        <MissingIndex Database="[AppDb]" Schema="[dbo]" Table="[Orders]">
                          <ColumnGroup Usage="EQUALITY">
                            <Column Name="[Status]" ColumnId="3" />
                          </ColumnGroup>
                          <ColumnGroup Usage="INCLUDE">
                            <Column Name="[OrderId]" ColumnId="1" />
                            <Column Name="[CustomerEmail]" ColumnId="4" />
                          </ColumnGroup>
                        </MissingIndex>
                      </MissingIndexGroup>
                    </MissingIndexes>
                    <RelOp NodeId="0" PhysicalOp="Nested Loops" LogicalOp="Inner Join" EstimateRows="100" EstimateIO="0" EstimateCPU="0.0001" AvgRowSize="42" EstimatedTotalSubtreeCost="0.0065" Parallel="0" EstimateRebinds="0">
                      <OutputList />
                      <Warnings NoJoinPredicate="true" />
                      <RelOp NodeId="1" PhysicalOp="Index Scan" LogicalOp="Index Scan" EstimateRows="1000" EstimateIO="0.0033" EstimateCPU="0.0002" AvgRowSize="18" EstimatedTotalSubtreeCost="0.0035" Parallel="0" EstimateRebinds="0">
                        <OutputList />
                        <Object Database="[AppDb]" Schema="[dbo]" Table="[Orders]" Index="[IX_Orders_Status]" IndexKind="NonClustered" />
                      </RelOp>
                      <RelOp NodeId="2" PhysicalOp="Clustered Index Seek" LogicalOp="Clustered Index Seek" EstimateRows="1" EstimateIO="0.0031" EstimateCPU="0.0002" AvgRowSize="11" EstimatedTotalSubtreeCost="0.0033" Parallel="0" EstimateRebinds="0">
                        <OutputList />
                        <Object Database="[AppDb]" Schema="[dbo]" Table="[Orders]" Index="[PK_Orders]" IndexKind="Clustered" />
                      </RelOp>
                    </RelOp>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

    /// <summary>
    /// Plan with no missing indexes and no warnings — only cost + RelOp extraction.
    /// </summary>
    public const string SimplePlan = """
        <?xml version="1.0" encoding="UTF-16"?>
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.5" Build="16.0.1000.6">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple StatementText="SELECT TOP 5 * FROM sys.objects" StatementId="1" StatementCompId="1" StatementType="SELECT" StatementSubTreeCost="0.0033" StatementEstRows="5">
                  <QueryPlan CachedPlanSize="16" CompileTime="1" CompileCPU="1" CompileMemory="160">
                    <RelOp NodeId="0" PhysicalOp="Index Scan" LogicalOp="Index Scan" EstimateRows="5" EstimateIO="0.0031" EstimateCPU="0.0002" AvgRowSize="12" EstimatedTotalSubtreeCost="0.0033" Parallel="0" EstimateRebinds="0">
                      <OutputList />
                      <Object Database="[mssqlsystemresource]" Schema="[sys]" Table="[sysobjects]" Index="[clst]" IndexKind="Clustered" />
                    </RelOp>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

    /// <summary>
    /// Plan with six RelOp nodes — verifies top-5 sorting truncates at 5.
    /// </summary>
    public const string PlanWithSixRelOps = """
        <?xml version="1.0" encoding="UTF-16"?>
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.5" Build="16.0.1000.6">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple StatementText="SELECT 1" StatementId="1" StatementCompId="1" StatementType="SELECT" StatementSubTreeCost="0.01" StatementEstRows="1">
                  <QueryPlan CachedPlanSize="8">
                    <RelOp NodeId="0" PhysicalOp="Compute Scalar" LogicalOp="Compute Scalar" EstimateRows="1" EstimateIO="0" EstimateCPU="0.00001" EstimatedTotalSubtreeCost="0.00001">
                      <OutputList />
                      <RelOp NodeId="1" PhysicalOp="Index Scan" LogicalOp="Index Scan" EstimateRows="100" EstimateIO="0.0050" EstimateCPU="0.0005" EstimatedTotalSubtreeCost="0.0055">
                        <OutputList />
                        <Object Database="[AppDb]" Schema="[dbo]" Table="[T1]" Index="[IX1]" IndexKind="NonClustered" />
                      </RelOp>
                      <RelOp NodeId="2" PhysicalOp="Index Seek" LogicalOp="Index Seek" EstimateRows="50" EstimateIO="0.0040" EstimateCPU="0.0004" EstimatedTotalSubtreeCost="0.0044">
                        <OutputList />
                        <Object Database="[AppDb]" Schema="[dbo]" Table="[T2]" Index="[IX2]" IndexKind="NonClustered" />
                      </RelOp>
                      <RelOp NodeId="3" PhysicalOp="Clustered Index Seek" LogicalOp="Clustered Index Seek" EstimateRows="25" EstimateIO="0.0030" EstimateCPU="0.0003" EstimatedTotalSubtreeCost="0.0033">
                        <OutputList />
                        <Object Database="[AppDb]" Schema="[dbo]" Table="[T3]" Index="[C1]" IndexKind="Clustered" />
                      </RelOp>
                      <RelOp NodeId="4" PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimateRows="10" EstimateIO="0.0020" EstimateCPU="0.0002" EstimatedTotalSubtreeCost="0.0022">
                        <OutputList />
                        <Object Database="[AppDb]" Schema="[dbo]" Table="[T4]" Index="NULL" IndexKind="Heap" />
                      </RelOp>
                      <RelOp NodeId="5" PhysicalOp="Sort" LogicalOp="Sort" EstimateRows="5" EstimateIO="0.0010" EstimateCPU="0.0001" EstimatedTotalSubtreeCost="0.0011">
                        <OutputList />
                      </RelOp>
                      <RelOp NodeId="6" PhysicalOp="Filter" LogicalOp="Filter" EstimateRows="1" EstimateIO="0" EstimateCPU="0.00001" EstimatedTotalSubtreeCost="0.00001">
                        <OutputList />
                      </RelOp>
                    </RelOp>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;
}
