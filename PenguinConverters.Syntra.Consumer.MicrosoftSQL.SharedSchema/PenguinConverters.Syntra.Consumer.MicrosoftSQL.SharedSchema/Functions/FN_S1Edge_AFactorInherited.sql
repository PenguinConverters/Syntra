-- =============================================
-- Syntra - Penguin Converters AG
-- Function: FN_S1Edge_AFactorInherited
-- Description: Recursive CTE calculating inherited AFactor through the edge graph
-- =============================================
CREATE FUNCTION [dbo].[FN_S1Edge_AFactorInherited]
(
    @NodeIdentifier VARCHAR(256)
)
RETURNS TINYINT
AS
BEGIN
    DECLARE @InheritedFactor TINYINT;

    ;WITH EdgeCTE AS
    (
        -- Anchor: start from the given node
        SELECT
            n.[Identifier],
            n.[AFactor] AS [CurrentFactor],
            CAST(n.[AFactor] AS INT) AS [AccumulatedFactor],
            0 AS [Depth]
        FROM [dbo].[S1Node] n
        WHERE n.[Identifier] = @NodeIdentifier
          AND n.[S1NodeDeleted] IS NULL

        UNION ALL

        -- Recursive: traverse edges to connected nodes
        SELECT
            nFrom.[Identifier],
            nFrom.[AFactor] AS [CurrentFactor],
            CASE
                WHEN cte.[AccumulatedFactor] + (nFrom.[AFactor] * ISNULL(et.[Factor], 75) / 100) > 255 THEN 255
                ELSE cte.[AccumulatedFactor] + (nFrom.[AFactor] * ISNULL(et.[Factor], 75) / 100)
            END AS [AccumulatedFactor],
            cte.[Depth] + 1 AS [Depth]
        FROM EdgeCTE cte
        INNER JOIN [dbo].[S1Edge] e ON cte.[Identifier] = e.[NodeToIdentifier]
            AND e.[S1EdgeDeleted] IS NULL
        INNER JOIN [dbo].[S1Node] nFrom ON e.[NodeFromIdentifier] = nFrom.[Identifier]
            AND nFrom.[S1NodeDeleted] IS NULL
        LEFT JOIN [dbo].[S1EdgeType] et ON e.[S1EdgeTypeId] = et.[S1EdgeTypeId]
        WHERE cte.[Depth] < 10
    )
    SELECT @InheritedFactor = CASE
        WHEN MAX([AccumulatedFactor]) > 255 THEN 255
        ELSE CAST(MAX([AccumulatedFactor]) AS TINYINT)
    END
    FROM EdgeCTE;

    RETURN ISNULL(@InheritedFactor, 0);
END
GO
