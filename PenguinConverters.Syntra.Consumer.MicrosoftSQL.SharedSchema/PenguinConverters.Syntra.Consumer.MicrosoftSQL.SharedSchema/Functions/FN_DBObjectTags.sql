-- =============================================
-- Syntra - Penguin Converters AG
-- Function: FN_DBObjectTags
-- Description: Extracts XML comment tags from procedure definitions for frontend metadata
-- Tags follow pattern: <!-- @TagName: TagValue -->
-- =============================================
CREATE FUNCTION [dbo].[FN_DBObjectTags]
(
    @ObjectName NVARCHAR(256)
)
RETURNS @Tags TABLE
(
    [TagName] NVARCHAR(128),
    [TagValue] NVARCHAR(MAX)
)
AS
BEGIN
    DECLARE @Definition NVARCHAR(MAX);
    DECLARE @StartPos INT;
    DECLARE @EndPos INT;
    DECLARE @TagContent NVARCHAR(MAX);
    DECLARE @ColonPos INT;

    SELECT @Definition = OBJECT_DEFINITION(OBJECT_ID(@ObjectName));

    IF @Definition IS NULL
        RETURN;

    SET @StartPos = CHARINDEX('<!-- @', @Definition, 1);

    WHILE @StartPos > 0
    BEGIN
        SET @EndPos = CHARINDEX('-->', @Definition, @StartPos);

        IF @EndPos > 0
        BEGIN
            SET @TagContent = SUBSTRING(@Definition, @StartPos + 6, @EndPos - @StartPos - 7);
            SET @ColonPos = CHARINDEX(':', @TagContent);

            IF @ColonPos > 0
            BEGIN
                INSERT INTO @Tags ([TagName], [TagValue])
                VALUES (
                    LTRIM(RTRIM(LEFT(@TagContent, @ColonPos - 1))),
                    LTRIM(RTRIM(SUBSTRING(@TagContent, @ColonPos + 1, LEN(@TagContent))))
                );
            END
        END

        SET @StartPos = CHARINDEX('<!-- @', @Definition, @EndPos + 1);
    END

    RETURN;
END
GO
