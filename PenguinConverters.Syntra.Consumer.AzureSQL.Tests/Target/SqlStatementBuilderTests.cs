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
        TestDelegate action = () => SqlStatementBuilder.QuoteIdentifier(identifier);

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
        TestDelegate action = () => SqlStatementBuilder.BuildCreateTempTable(
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
        TestDelegate action = () => SqlStatementBuilder.BuildMerge(
            "Users", "#Stage", new List<string> { "Id" }, new List<string>());

        //Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Test]
    public void BuildDeleteReconciliation_WithoutDeletedColumn_EmitsHardDelete()
    {
        //Arrange
        List<string> keys = new List<string> { "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildDeleteReconciliation("Users", "#Seen", keys, null);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.StartWith("DELETE target"));
            Assert.That(sql, Does.Contain("FROM [Users] AS target"));
            Assert.That(sql, Does.Contain("NOT EXISTS ("));
            Assert.That(sql, Does.Contain("SELECT 1 FROM [#Seen] AS seen"));
            Assert.That(sql, Does.Contain("target.[Id] = seen.[Id]"));
        });
    }

    [Test]
    public void BuildDeleteReconciliation_WithDeletedColumn_EmitsSoftDelete()
    {
        //Arrange
        List<string> keys = new List<string> { "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildDeleteReconciliation("Users", "#Seen", keys, "Deleted");

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.StartWith("UPDATE target"));
            Assert.That(sql, Does.Contain("SET target.[Deleted] = SYSUTCDATETIME()"));
            // Already-deleted rows must not be re-stamped on every run.
            Assert.That(sql, Does.Contain("WHERE target.[Deleted] IS NULL"));
            Assert.That(sql, Does.Not.Contain("DELETE target"));
        });
    }

    [Test]
    public void BuildDeleteCandidateCount_ReturnsTotalAndCandidateColumns()
    {
        //Arrange
        List<string> keys = new List<string> { "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildDeleteCandidateCount("Users", "#Seen", keys, null);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("COUNT_BIG(*) AS TotalRows"));
            Assert.That(sql, Does.Contain("AS DeleteCandidates"));
            Assert.That(sql, Does.Contain("FROM [Users] AS target"));
        });
    }

    [Test]
    public void BuildDeleteCandidateCount_WithDeletedColumn_ExcludesAlreadyDeletedRows()
    {
        //Arrange
        List<string> keys = new List<string> { "Id" };

        //Act
        string sql = SqlStatementBuilder.BuildDeleteCandidateCount("Users", "#Seen", keys, "Deleted");

        //Assert
        Assert.That(sql, Does.Contain("WHERE target.[Deleted] IS NULL"));
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
}
