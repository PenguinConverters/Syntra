-- =============================================
-- Syntra - Penguin Converters AG
-- Stored Procedure: SP_AZSignIn_DELETE
-- Description: Azure SQL specific - Deletes old sign-in records beyond retention period
-- Note: This is an Azure SQL specific procedure that extends the shared S1 schema
-- =============================================
CREATE PROCEDURE [dbo].[SP_AZSignIn_DELETE]
    @RetentionDays INT = 90
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM [dbo].[S1Shadow]
    WHERE [S1ShadowInserted] < DATEADD(DAY, -@RetentionDays, GETUTCDATE())
      AND [S1ShadowDeleted] IS NOT NULL;
END
GO
