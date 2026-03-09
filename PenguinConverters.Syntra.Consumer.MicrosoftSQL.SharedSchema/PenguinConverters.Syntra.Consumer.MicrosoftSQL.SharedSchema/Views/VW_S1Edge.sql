-- =============================================
-- Syntra - Penguin Converters AG
-- View: VW_S1Edge
-- Description: Extended edge view with node details
-- =============================================
CREATE VIEW [dbo].[VW_S1Edge]
AS
SELECT
    e.[S1EdgeId],
    e.[S1EdgeIdentity],
    e.[NodeToIdentifier],
    e.[NodeFromIdentifier],
    e.[TableName],
    e.[Type],
    e.[S1EdgeTypeId],
    et.[Name] AS [EdgeTypeName],
    et.[Factor],
    nTo.[S1NodeId] AS [NodeToId],
    nTo.[DisplayName] AS [NodeToDisplayName],
    nTo.[Type] AS [NodeToType],
    nTo.[CFactor] AS [NodeToCFactor],
    nTo.[AFactor] AS [NodeToAFactor],
    nFrom.[S1NodeId] AS [NodeFromId],
    nFrom.[DisplayName] AS [NodeFromDisplayName],
    nFrom.[Type] AS [NodeFromType],
    nFrom.[CFactor] AS [NodeFromCFactor],
    nFrom.[AFactor] AS [NodeFromAFactor],
    e.[S1EdgeInserted],
    e.[S1EdgeUpdated]
FROM [dbo].[S1Edge] e
LEFT JOIN [dbo].[S1EdgeType] et ON e.[S1EdgeTypeId] = et.[S1EdgeTypeId]
LEFT JOIN [dbo].[S1Node] nTo ON e.[NodeToIdentifier] = nTo.[Identifier]
LEFT JOIN [dbo].[S1Node] nFrom ON e.[NodeFromIdentifier] = nFrom.[Identifier]
WHERE e.[S1EdgeDeleted] IS NULL
GO
