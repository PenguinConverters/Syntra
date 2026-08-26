// -----------------------------------------------------------------------
// <copyright file="SqlStatementBuilderTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using PenguinConverters.Syntra.Consumer.AzureSQL.Target;

namespace PenguinConverters.Syntra.Consumer.AzureSQL.Tests.Target;

[TestFixture]
public class SqlStatementBuilderTests
{
    #region Methods

    [Test]
    public void QuoteIdentifier_WrapsInBrackets()
    {
        //Arrange

        //Act
        string quoted = SqlStatementBuilder.QuoteIdentifier("Users");

        //Assert
        Assert.That(quoted, Is.EqualTo("[Users]"));
    }

    [Test]
    public void QuoteIdentifier_EscapesEmbeddedClosingBracket()
    {
        //Arrange
        string hostile = "Users]; DROP TABLE Users; --";

        //Act
        string quoted = SqlStatementBuilder.QuoteIdentifier(hostile);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(quoted, Is.EqualTo("[Users]]; DROP TABLE Users; --]"));
            // The injected close-bracket is doubled, so the identifier never terminates early.
            Assert.That(quoted, Does.Not.Match(@"(?<!\])\](?!\])(?!$)"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void QuoteIdentifier_RejectsEmptyIdentifier(string? identifier)
    {
        //Arrange

        //Act
        Action action = () => SqlStatementBuilder.QuoteIdentifier(identifier);

        //Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Test]
    public void BuildTempTableName_SanitisesNonAlphanumericCharacters()
    {
        //Arrange

        //Act
        string name = SqlStatementBuilder.BuildTempTableName("Contoso.Users-Sync 01");

        //Assert
        Assert.That(name, Is.EqualTo("#S1_Contoso_Users_Sync_01"));
    }

    [Test]
    public void BuildTempTableName_AppliesSuffix()
    {
        //Arrange

        //Act
        string upsert = SqlStatementBuilder.BuildTempTableName(
            "ActiveDirectory.groups2AzureSQL.ADGroup_DELTA", SqlStatementBuilder.UpsertSuffix);
        string delete = SqlStatementBuilder.BuildTempTableName(
            "ActiveDirectory.groups2AzureSQL.ADGroup_DELTA", SqlStatementBuilder.DeleteSuffix);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(upsert, Is.EqualTo("#S1_ActiveDirectory_groups2AzureSQL_ADGroup_DELTA_U"));
            Assert.That(delete, Is.EqualTo("#S1_ActiveDirectory_groups2AzureSQL_ADGroup_DELTA_D"));
            Assert.That(upsert, Is.Not.EqualTo(delete));
        });
    }

    [Test]
    public void BuildTempTableName_LongInputWithSuffix_KeepsSuffixDistinct()
    {
        //Arrange - truncation must not eat the suffix, or both tables collide.
        string longName = new string('a', 400);

        //Act
        string upsert = SqlStatementBuilder.BuildTempTableName(longName, SqlStatementBuilder.UpsertSuffix);
        string delete = SqlStatementBuilder.BuildTempTableName(longName, SqlStatementBuilder.DeleteSuffix);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(upsert, Does.EndWith(SqlStatementBuilder.UpsertSuffix));
            Assert.That(delete, Does.EndWith(SqlStatementBuilder.DeleteSuffix));
            Assert.That(upsert, Is.Not.EqualTo(delete));
            Assert.That(upsert.Length, Is.LessThanOrEqualTo(116));
        });
    }

    [Test]
    public void BuildDeleteFromStaging_JoinsTargetToStagedKeys()
    {
        //Arrange
        List<string> keys = new List<string> { "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildDeleteFromStaging("Users", "#Stage_D", keys);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.StartWith("DELETE target"));
            Assert.That(sql, Does.Contain("FROM [Users] AS target"));
            Assert.That(sql, Does.Contain("INNER JOIN [#Stage_D] AS staged"));
            Assert.That(sql, Does.Contain("target.[Id] = staged.[Id]"));
            Assert.That(sql, Does.EndWith(";"));
        });
    }

    [Test]
    public void BuildDeleteFromStaging_CompositeKey_AndsEveryColumn()
    {
        //Arrange
        List<string> keys = new List<string> { "Tenant", "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildDeleteFromStaging("Users", "#Stage_D", keys);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("target.[Tenant] = staged.[Tenant]"));
            Assert.That(sql, Does.Contain("AND target.[Id] = staged.[Id]"));
        });
    }

    [Test]
    public void BuildDeleteFromStaging_HostileIdentifier_IsEscaped()
    {
        //Arrange
        List<string> keys = new List<string> { "I]d" };

        //Act
        string sql = SqlStatementBuilder.BuildDeleteFromStaging("Users", "#Stage_D", keys);

        //Assert
        Assert.That(sql, Does.Contain("[I]]d]"));
    }

    [Test]
    public void BuildDeleteFromStaging_NoPrimaryKeys_Throws()
    {
        //Arrange

        //Act
        Action action = () => SqlStatementBuilder.BuildDeleteFromStaging(
            "Users", "#Stage_D", new List<string>());

        //Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Test]
    public void BuildTempTableName_EmptyInput_FallsBackToDefault()
    {
        //Arrange

        //Act
        string name = SqlStatementBuilder.BuildTempTableName(null);

        //Assert
        Assert.That(name, Is.EqualTo("#S1_Sync"));
    }

    [Test]
    public void BuildTempTableName_LongInput_IsTruncated()
    {
        //Arrange
        string longName = new string('a', 400);

        //Act
        string name = SqlStatementBuilder.BuildTempTableName(longName);

        //Assert
        Assert.That(name.Length, Is.LessThanOrEqualTo(116));
    }

    [Test]
    public void BuildCreateTempTable_EmitsQuotedColumnsAndTypes()
    {
        //Arrange
        List<KeyValuePair<string, string>> columns = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Id", "UNIQUEIDENTIFIER"),
            new KeyValuePair<string, string>("Name", "NVARCHAR(255)")
        };

        //Act
        string sql = SqlStatementBuilder.BuildCreateTempTable("#Stage", columns);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.StartWith("CREATE TABLE [#Stage] ("));
            Assert.That(sql, Does.Contain("[Id] UNIQUEIDENTIFIER,"));
            Assert.That(sql, Does.Contain("[Name] NVARCHAR(255)"));
            Assert.That(sql, Does.EndWith(");"));
        });
    }

    [Test]
    public void BuildCreateTempTable_NoColumns_Throws()
    {
        //Arrange

        //Act
        Action action = () => SqlStatementBuilder.BuildCreateTempTable(
            "#Stage", new List<KeyValuePair<string, string>>());

        //Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Test]
    public void BuildMerge_MatchesOnPrimaryKeyAndUpdatesNonKeyColumns()
    {
        //Arrange
        List<string> allColumns = new List<string> { "Id", "Name", "Mail" };
        List<string> keys = new List<string> { "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildMerge("Users", "#Stage", allColumns, keys);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("MERGE INTO [Users] AS target"));
            Assert.That(sql, Does.Contain("USING [#Stage] AS source"));
            Assert.That(sql, Does.Contain("ON target.[Id] = source.[Id]"));
            Assert.That(sql, Does.Contain("WHEN MATCHED THEN"));
            Assert.That(sql, Does.Contain("target.[Name] = source.[Name]"));
            Assert.That(sql, Does.Contain("target.[Mail] = source.[Mail]"));
            Assert.That(sql, Does.Contain("INSERT ([Id], [Name], [Mail])"));
            Assert.That(sql, Does.EndWith(";"));
        });
    }

    [Test]
    public void BuildMerge_DoesNotAssignPrimaryKeyInUpdateSet()
    {
        //Arrange
        List<string> allColumns = new List<string> { "Id", "Name" };
        List<string> keys = new List<string> { "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildMerge("Users", "#Stage", allColumns, keys);
        string updateClause = sql[sql.IndexOf("UPDATE SET", StringComparison.Ordinal)..];
        updateClause = updateClause[..updateClause.IndexOf("WHEN NOT MATCHED", StringComparison.Ordinal)];

        //Assert
        Assert.That(updateClause, Does.Not.Contain("[Id]"));
    }

    [Test]
    public void BuildMerge_CompositeKey_AndsEveryKeyColumn()
    {
        //Arrange
        List<string> allColumns = new List<string> { "Tenant", "Id", "Name" };
        List<string> keys = new List<string> { "Tenant", "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildMerge("Users", "#Stage", allColumns, keys);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("target.[Tenant] = source.[Tenant]"));
            Assert.That(sql, Does.Contain("AND target.[Id] = source.[Id]"));
        });
    }

    [Test]
    public void BuildMerge_NeverEmitsNotMatchedBySource()
    {
        //Arrange - the source is one batch, not the whole synchronization, so a
        //BY SOURCE clause would match every row outside the current batch.
        List<string> allColumns = new List<string> { "Id", "Name" };
        List<string> keys = new List<string> { "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildMerge("Users", "#Stage_U", allColumns, keys);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Not.Contain("NOT MATCHED BY SOURCE").IgnoreCase,
                "THEN DELETE on this clause empties the table down to the last batch flushed");
            Assert.That(sql, Does.Not.Contain("DELETE").IgnoreCase,
                "the batch MERGE must not delete; deletions have their own two paths");
        });
    }

    [Test]
    public void BuildMerge_KeyOnlyTable_StillEmitsNoDeleteClause()
    {
        //Arrange - the key-only shape omits UPDATE, which must not tempt a DELETE branch in.
        List<string> allColumns = new List<string> { "Tenant", "Id" };
        List<string> keys = new List<string> { "Tenant", "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildMerge("Links", "#Stage_U", allColumns, keys);

        //Assert
        Assert.That(sql, Does.Not.Contain("DELETE").IgnoreCase);
    }

    [Test]
    public void BuildMerge_KeyOnlyTable_OmitsUpdateClause()
    {
        //Arrange — every column is part of the key, so there is nothing to SET.
        List<string> allColumns = new List<string> { "Tenant", "Id" };
        List<string> keys = new List<string> { "Tenant", "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildMerge("Links", "#Stage", allColumns, keys);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Not.Contain("WHEN MATCHED THEN"), "an empty UPDATE SET is a syntax error");
            Assert.That(sql, Does.Contain("WHEN NOT MATCHED BY TARGET THEN"));
        });
    }

    [Test]
    public void BuildMerge_HostileColumnName_IsEscapedEverywhere()
    {
        //Arrange
        List<string> allColumns = new List<string> { "Id", "Na]me" };
        List<string> keys = new List<string> { "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildMerge("Users", "#Stage", allColumns, keys);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("[Na]]me]"));
            Assert.That(sql, Does.Not.Contain("[Na]me]"));
        });
    }

    [Test]
    public void BuildMerge_NoPrimaryKeys_Throws()
    {
        //Arrange

        //Act
        Action action = () => SqlStatementBuilder.BuildMerge(
            "Users", "#Stage", new List<string> { "Id" }, new List<string>());

        //Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Test]
    public void BuildColumnSchemaQuery_ParameterisesTableNameAndResolvesBaseType()
    {
        //Arrange

        //Act
        string sql = SqlStatementBuilder.BuildColumnSchemaQuery();

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("OBJECT_ID(@TableName)"), "table name must be a parameter");
            // Alias types resolve to a name that does not exist in tempdb, where the staging
            // table is created, so the base system type is required.
            Assert.That(sql, Does.Contain("TYPE_NAME(c.system_type_id)"));
            Assert.That(sql, Does.Not.Contain("user_type_id"));
            Assert.That(sql, Does.Contain("c.is_computed = 0"));
            Assert.That(sql, Does.Contain("<> 'timestamp'"));
            Assert.That(sql, Does.Contain("ORDER BY c.column_id"));
        });
    }

    [TestCase("int", (short)4, (byte)10, (byte)0, "INT")]
    [TestCase("bigint", (short)8, (byte)19, (byte)0, "BIGINT")]
    [TestCase("bit", (short)1, (byte)1, (byte)0, "BIT")]
    [TestCase("uniqueidentifier", (short)16, (byte)0, (byte)0, "UNIQUEIDENTIFIER")]
    [TestCase("date", (short)3, (byte)10, (byte)0, "DATE")]
    [TestCase("xml", (short)-1, (byte)0, (byte)0, "XML")]
    public void RenderSqlType_FixedWidthTypes_HaveNoSpecifier(
        string typeName, short maxLength, byte precision, byte scale, string expected)
    {
        //Arrange

        //Act
        string rendered = SqlStatementBuilder.RenderSqlType(typeName, maxLength, precision, scale);

        //Assert
        Assert.That(rendered, Is.EqualTo(expected));
    }

    [Test]
    public void RenderSqlType_NationalCharTypes_ConvertBytesToCharacters()
    {
        //Arrange — sys.columns.max_length is bytes; NVARCHAR stores 2 bytes per character.

        //Act
        string rendered = SqlStatementBuilder.RenderSqlType("nvarchar", 510, 0, 0);

        //Assert
        Assert.That(rendered, Is.EqualTo("NVARCHAR(255)"));
    }

    [Test]
    public void RenderSqlType_NonNationalCharTypes_UseBytesDirectly()
    {
        //Arrange

        //Act
        string rendered = SqlStatementBuilder.RenderSqlType("varchar", 255, 0, 0);

        //Assert
        Assert.That(rendered, Is.EqualTo("VARCHAR(255)"));
    }

    [TestCase("nvarchar", "NVARCHAR(MAX)")]
    [TestCase("varchar", "VARCHAR(MAX)")]
    [TestCase("varbinary", "VARBINARY(MAX)")]
    public void RenderSqlType_NegativeMaxLength_RendersMax(string typeName, string expected)
    {
        //Arrange

        //Act
        string rendered = SqlStatementBuilder.RenderSqlType(typeName, -1, 0, 0);

        //Assert
        Assert.That(rendered, Is.EqualTo(expected));
    }

    [Test]
    public void RenderSqlType_Decimal_CarriesPrecisionAndScale()
    {
        //Arrange

        //Act
        string rendered = SqlStatementBuilder.RenderSqlType("decimal", 9, 18, 4);

        //Assert
        Assert.That(rendered, Is.EqualTo("DECIMAL(18,4)"));
    }

    [TestCase("datetime2", (byte)7, "DATETIME2(7)")]
    [TestCase("datetimeoffset", (byte)3, "DATETIMEOFFSET(3)")]
    [TestCase("time", (byte)0, "TIME(0)")]
    public void RenderSqlType_TemporalTypes_CarryScale(string typeName, byte scale, string expected)
    {
        //Arrange

        //Act
        string rendered = SqlStatementBuilder.RenderSqlType(typeName, 8, 0, scale);

        //Assert
        Assert.That(rendered, Is.EqualTo(expected));
    }

    [Test]
    public void RenderSqlType_RoundTripsIntoCreateTempTable()
    {
        //Arrange — the rendered type must be valid where it is actually used.
        List<KeyValuePair<string, string>> columns = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>(
                "Name", SqlStatementBuilder.RenderSqlType("nvarchar", 510, 0, 0)),
            new KeyValuePair<string, string>(
                "Amount", SqlStatementBuilder.RenderSqlType("decimal", 9, 18, 4))
        };

        //Act
        string sql = SqlStatementBuilder.BuildCreateTempTable("#Stage", columns);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("[Name] NVARCHAR(255)"));
            Assert.That(sql, Does.Contain("[Amount] DECIMAL(18,4)"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public void RenderSqlType_MissingTypeName_Throws(string? typeName)
    {
        //Arrange

        //Act
        Action action = () => SqlStatementBuilder.RenderSqlType(typeName!, 4, 0, 0);

        //Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Test]
    public void BuildTruncate_QuotesTableName()
    {
        //Arrange

        //Act
        string sql = SqlStatementBuilder.BuildTruncate("#Stage");

        //Assert
        Assert.That(sql, Is.EqualTo("TRUNCATE TABLE [#Stage];"));
    }

    [Test]
    public void BuildDropTempTableIfExists_GuardsOnObjectId()
    {
        //Arrange

        //Act
        string sql = SqlStatementBuilder.BuildDropTempTableIfExists("#Stage");

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("OBJECT_ID(N'tempdb..#Stage')"));
            Assert.That(sql, Does.Contain("IS NOT NULL DROP TABLE [#Stage];"));
        });
    }

    [Test]
    public void BuildDropTempTableIfExists_EscapesSingleQuote()
    {
        //Arrange

        //Act
        string sql = SqlStatementBuilder.BuildDropTempTableIfExists("#Sta'ge");

        //Assert
        Assert.That(sql, Does.Contain("N'tempdb..#Sta''ge'"));
    }

    #endregion
}
