using PenguinConverters.Syntra.Consumer.AzureSQL.Target;

namespace PenguinConverters.Syntra.Consumer.AzureSQL.Tests.Target;

/// <summary>
/// Verifies the T-SQL text produced by <see cref="SqlStatementBuilder"/>.
/// </summary>
[TestFixture]
public class SqlStatementBuilderTests
{
    #region Methods

    /// <summary>
    /// Creates a column description the way the catalog would report it.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <param name="baseType">The upper-case system type name.</param>
    /// <param name="declaration">The full type declaration; the base type when omitted.</param>
    /// <param name="collation">The column collation, if any.</param>
    /// <returns>The column description.</returns>
    private static TargetColumn Column(string name, string baseType, string? declaration = null, string? collation = null)
    {
        return new TargetColumn(name, baseType, declaration ?? baseType, collation);
    }

    /// <summary>
    /// An identifier is wrapped in brackets.
    /// </summary>
    [Test]
    public void QuoteIdentifier_WrapsInBrackets()
    {
        //Arrange

        //Act
        string quoted = SqlStatementBuilder.QuoteIdentifier("Users");

        //Assert
        Assert.That(quoted, Is.EqualTo("[Users]"));
    }

    /// <summary>
    /// A closing bracket inside an identifier is doubled so the identifier cannot end early.
    /// </summary>
    [Test]
    public void QuoteIdentifier_EscapesEmbeddedClosingBracket()
    {
        //Arrange
        string hostile = "Users]; DROP TABLE Users; --";

        //Act
        string quoted = SqlStatementBuilder.QuoteIdentifier(hostile);

        //Assert
        Assert.That(quoted, Is.EqualTo("[Users]]; DROP TABLE Users; --]"));
    }

    /// <summary>
    /// A missing identifier is rejected.
    /// </summary>
    /// <param name="identifier">The identifier under test.</param>
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

    /// <summary>
    /// Schema and object are quoted separately and joined with a dot.
    /// </summary>
    [Test]
    public void QuoteObjectName_QuotesBothParts()
    {
        //Arrange

        //Act
        string quoted = SqlStatementBuilder.QuoteObjectName("stage", "AD]User");

        //Assert
        Assert.That(quoted, Is.EqualTo("[stage].[AD]]User]"));
    }

    /// <summary>
    /// The staging table is named after the synchronization, with punctuation replaced.
    /// </summary>
    [Test]
    public void BuildTempTableName_SanitisesObjectNamespace()
    {
        //Arrange

        //Act
        string name = SqlStatementBuilder.BuildTempTableName("ActiveDirectory.Users-DELTA");

        //Assert
        Assert.That(name, Is.EqualTo("#S1_ActiveDirectory_Users_DELTA"));
    }

    /// <summary>
    /// An empty synchronization name still yields a valid staging table name.
    /// </summary>
    /// <param name="objectNamespace">The synchronization name under test.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void BuildTempTableName_EmptyInput_FallsBackToDefault(string? objectNamespace)
    {
        //Arrange

        //Act
        string name = SqlStatementBuilder.BuildTempTableName(objectNamespace);

        //Assert
        Assert.That(name, Is.EqualTo("#S1_Sync"));
    }

    /// <summary>
    /// A long synchronization name is cut to the maximum staging table name length.
    /// </summary>
    [Test]
    public void BuildTempTableName_LongInput_IsTruncated()
    {
        //Arrange
        string longName = new string('a', 300);

        //Act
        string name = SqlStatementBuilder.BuildTempTableName(longName);

        //Assert
        Assert.That(name, Has.Length.EqualTo(SqlStatementBuilder.MaxTempTableNameLength));
    }

    /// <summary>
    /// Every staged column is nullable and carries the target's type and collation, and the
    /// control columns follow.
    /// </summary>
    [Test]
    public void BuildCreateTempTable_MirrorsTypesAndCollations()
    {
        //Arrange
        List<TargetColumn> columns = new List<TargetColumn>
        {
            Column("Id", "UNIQUEIDENTIFIER"),
            Column("Na]me", "NVARCHAR", "NVARCHAR(50)", "Latin1_General_100_CS_AS")
        };

        //Act
        string sql = SqlStatementBuilder.BuildCreateTempTable("#S1_Users", columns);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.StartWith("CREATE TABLE [#S1_Users] ("));
            Assert.That(sql, Does.Contain("[Id] UNIQUEIDENTIFIER NULL,"));
            Assert.That(sql, Does.Contain("[Na]]me] NVARCHAR(50) COLLATE Latin1_General_100_CS_AS NULL,"));
            Assert.That(sql, Does.Contain("[__S1Deleted] BIT NOT NULL,"));
            Assert.That(sql, Does.Contain("[__S1Sequence] BIGINT NOT NULL"));
        });
    }

    /// <summary>
    /// A staging table without columns is rejected.
    /// </summary>
    [Test]
    public void BuildCreateTempTable_NoColumns_Throws()
    {
        //Arrange
        List<TargetColumn> columns = new List<TargetColumn>();

        //Act
        Action action = () => SqlStatementBuilder.BuildCreateTempTable("#S1_Users", columns);

        //Assert
        Assert.Throws<ArgumentException>(action);
    }

    /// <summary>
    /// The match condition compares the bare key columns of target and source, with no function
    /// or conversion applied to either side.
    /// </summary>
    [Test]
    public void BuildMerge_MatchConditionUsesBareKeyColumns()
    {
        //Arrange
        List<TargetColumn> columns = new List<TargetColumn>
        {
            Column("TenantId", "INT"),
            Column("Name", "NVARCHAR", "NVARCHAR(50)"),
            Column("Mail", "NVARCHAR", "NVARCHAR(320)")
        };

        //Act
        string sql = SqlStatementBuilder.BuildMerge(
            "[dbo].[Users]", "#S1_Users", columns, new List<string> { "TenantId", "Name" }, null);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("MERGE INTO [dbo].[Users] WITH (HOLDLOCK) AS target"));
            Assert.That(sql, Does.Contain("ON target.[TenantId] = source.[TenantId]"));
            Assert.That(sql, Does.Contain("AND target.[Name] = source.[Name]"));
        });
    }

    /// <summary>
    /// The source is reduced to the latest staged row per primary key.
    /// </summary>
    [Test]
    public void BuildMerge_SourceKeepsLatestRowPerKey()
    {
        //Arrange
        List<TargetColumn> columns = new List<TargetColumn> { Column("Id", "INT"), Column("Name", "NVARCHAR") };

        //Act
        string sql = SqlStatementBuilder.BuildMerge(
            "[dbo].[Users]", "#S1_Users", columns, new List<string> { "Id" }, null);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("ROW_NUMBER() OVER (PARTITION BY [Id] ORDER BY [__S1Sequence] DESC)"));
            Assert.That(sql, Does.Contain("WHERE staged.[__S1Rank] = 1"));
        });
    }

    /// <summary>
    /// Without a soft-delete column a staged deletion deletes, and an update needs a difference.
    /// </summary>
    [Test]
    public void BuildMerge_HardDelete_DeletesAndUpdatesOnlyChangedRows()
    {
        //Arrange
        List<TargetColumn> columns = new List<TargetColumn>
        {
            Column("Id", "INT"),
            Column("Name", "NVARCHAR"),
            Column("Age", "INT")
        };

        //Act
        string sql = SqlStatementBuilder.BuildMerge(
            "[dbo].[Users]", "#S1_Users", columns, new List<string> { "Id" }, null);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("WHEN MATCHED AND source.[__S1Deleted] = 1 THEN" + Environment.NewLine + "    DELETE"));
            Assert.That(sql, Does.Contain(
                "WHEN MATCHED AND EXISTS (SELECT CAST(source.[Name] AS VARBINARY(MAX)), source.[Age] " +
                "EXCEPT SELECT CAST(target.[Name] AS VARBINARY(MAX)), target.[Age]) THEN"));
            Assert.That(sql, Does.Contain("target.[Name] = source.[Name]"));
            Assert.That(sql, Does.Not.Contain("target.[Id] = source.[Id],"));
            Assert.That(sql, Does.Contain("WHEN NOT MATCHED BY TARGET AND source.[__S1Deleted] = 0 THEN"));
            Assert.That(sql, Does.Contain("INSERT ([Id], [Name], [Age])"));
        });
    }

    /// <summary>
    /// With a soft-delete column a staged deletion stamps the column and keeps the other columns.
    /// </summary>
    [Test]
    public void BuildMerge_SoftDelete_StampsColumnAndNeverDeletes()
    {
        //Arrange
        List<TargetColumn> columns = new List<TargetColumn> { Column("Id", "INT"), Column("Name", "NVARCHAR") };

        //Act
        string sql = SqlStatementBuilder.BuildMerge(
            "[dbo].[Users]", "#S1_Users", columns, new List<string> { "Id" }, "UsersDeleted");

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Not.Contain("DELETE"));
            Assert.That(sql, Does.Contain("(source.[__S1Deleted] = 1 AND target.[UsersDeleted] IS NULL)"));
            Assert.That(sql, Does.Contain("OR (source.[__S1Deleted] = 0 AND (target.[UsersDeleted] IS NOT NULL OR EXISTS ("));
            Assert.That(sql, Does.Contain(
                "target.[Name] = CASE WHEN source.[__S1Deleted] = 1 THEN target.[Name] ELSE source.[Name] END"));
            Assert.That(sql, Does.Contain(
                "target.[UsersDeleted] = CASE WHEN source.[__S1Deleted] = 1 THEN SYSUTCDATETIME() ELSE NULL END"));
            Assert.That(sql, Does.Contain("INSERT ([Id], [Name])"));
        });
    }

    /// <summary>
    /// The statement never acts on target rows that are absent from the staged batch.
    /// </summary>
    /// <param name="softDeleteColumn">The soft-delete column, or <c>null</c> for physical deletion.</param>
    [TestCase(null)]
    [TestCase("Deleted")]
    public void BuildMerge_NeverEmitsNotMatchedBySource(string? softDeleteColumn)
    {
        //Arrange
        List<TargetColumn> columns = new List<TargetColumn> { Column("Id", "INT"), Column("Name", "NVARCHAR") };

        //Act
        string sql = SqlStatementBuilder.BuildMerge(
            "[dbo].[Users]", "#S1_Users", columns, new List<string> { "Id" }, softDeleteColumn);

        //Assert
        Assert.That(sql, Does.Not.Contain("NOT MATCHED BY SOURCE"));
    }

    /// <summary>
    /// A table consisting of key columns only has nothing to update.
    /// </summary>
    [Test]
    public void BuildMerge_KeyOnlyTable_OmitsUpdateClause()
    {
        //Arrange
        List<TargetColumn> columns = new List<TargetColumn> { Column("GroupId", "INT"), Column("MemberId", "INT") };

        //Act
        string sql = SqlStatementBuilder.BuildMerge(
            "[dbo].[Members]", "#S1_Members", columns, new List<string> { "GroupId", "MemberId" }, null);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Not.Contain("UPDATE SET"));
            Assert.That(sql, Does.Not.Contain("EXCEPT"));
            Assert.That(sql, Does.Contain("INSERT ([GroupId], [MemberId])"));
        });
    }

    /// <summary>
    /// Types that cannot be compared directly are compared through a binary conversion.
    /// </summary>
    /// <param name="baseType">The system type of the compared column.</param>
    /// <param name="expected">The expression expected for the source side.</param>
    [TestCase("XML", "CAST(CAST(source.[Data] AS NVARCHAR(MAX)) AS VARBINARY(MAX))")]
    [TestCase("NTEXT", "CAST(CAST(source.[Data] AS NVARCHAR(MAX)) AS VARBINARY(MAX))")]
    [TestCase("TEXT", "CAST(CAST(source.[Data] AS VARCHAR(MAX)) AS VARBINARY(MAX))")]
    [TestCase("IMAGE", "CAST(source.[Data] AS VARBINARY(MAX))")]
    [TestCase("VARCHAR", "CAST(source.[Data] AS VARBINARY(MAX))")]
    [TestCase("DATETIME2", "SELECT source.[Data] EXCEPT")]
    public void BuildMerge_ChangeDetection_ConvertsByType(string baseType, string expected)
    {
        //Arrange
        List<TargetColumn> columns = new List<TargetColumn> { Column("Id", "INT"), Column("Data", baseType) };

        //Act
        string sql = SqlStatementBuilder.BuildMerge(
            "[dbo].[Users]", "#S1_Users", columns, new List<string> { "Id" }, null);

        //Assert
        Assert.That(sql, Does.Contain(expected));
    }

    /// <summary>
    /// A hostile column name is quoted wherever it appears.
    /// </summary>
    [Test]
    public void BuildMerge_HostileColumnName_IsEscapedEverywhere()
    {
        //Arrange
        List<TargetColumn> columns = new List<TargetColumn> { Column("Id", "INT"), Column("x]; DROP TABLE t; --", "INT") };

        //Act
        string sql = SqlStatementBuilder.BuildMerge(
            "[dbo].[Users]", "#S1_Users", columns, new List<string> { "Id" }, null);

        //Assert
        Assert.That(sql.Replace("[x]]; DROP TABLE t; --]", string.Empty), Does.Not.Contain("DROP TABLE"));
    }

    /// <summary>
    /// A merge without primary keys or without columns is rejected.
    /// </summary>
    [Test]
    public void BuildMerge_MissingKeysOrColumns_Throws()
    {
        //Arrange
        List<TargetColumn> columns = new List<TargetColumn> { Column("Id", "INT") };

        //Act
        Action noKeys = () => SqlStatementBuilder.BuildMerge(
            "[dbo].[Users]", "#S1_Users", columns, new List<string>(), null);
        Action noColumns = () => SqlStatementBuilder.BuildMerge(
            "[dbo].[Users]", "#S1_Users", new List<TargetColumn>(), new List<string> { "Id" }, null);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(noKeys);
            Assert.Throws<ArgumentException>(noColumns);
        });
    }

    /// <summary>
    /// The catalog queries take the table as a parameter and leave out columns that cannot be written.
    /// </summary>
    [Test]
    public void CatalogQueries_AreParameterisedAndFilterUnwritableColumns()
    {
        //Arrange

        //Act
        string schema = SqlStatementBuilder.BuildTargetSchemaQuery();
        string indexes = SqlStatementBuilder.BuildUniqueIndexQuery();

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(schema, Does.Contain("OBJECT_ID(@TableName)"));
            Assert.That(schema, Does.Contain("TYPE_NAME(c.system_type_id)"));
            Assert.That(schema, Does.Contain("c.collation_name"));
            Assert.That(schema, Does.Contain("c.is_computed = 0"));
            Assert.That(schema, Does.Contain("c.is_identity = 0"));
            Assert.That(schema, Does.Contain("c.generated_always_type = 0"));
            Assert.That(schema, Does.Contain("<> 'timestamp'"));
            Assert.That(indexes, Does.Contain("OBJECT_ID(@TableName)"));
            Assert.That(indexes, Does.Contain("i.is_unique = 1"));
        });
    }

    /// <summary>
    /// Type declarations are rendered from catalog metadata.
    /// </summary>
    /// <param name="typeName">The system type name.</param>
    /// <param name="maxLength">The length in bytes.</param>
    /// <param name="precision">The numeric precision.</param>
    /// <param name="scale">The numeric or temporal scale.</param>
    /// <param name="expected">The expected declaration.</param>
    [TestCase("int", (short)4, (byte)10, (byte)0, "INT")]
    [TestCase("uniqueidentifier", (short)16, (byte)0, (byte)0, "UNIQUEIDENTIFIER")]
    [TestCase("nvarchar", (short)510, (byte)0, (byte)0, "NVARCHAR(255)")]
    [TestCase("nvarchar", (short)-1, (byte)0, (byte)0, "NVARCHAR(MAX)")]
    [TestCase("varchar", (short)40, (byte)0, (byte)0, "VARCHAR(40)")]
    [TestCase("varbinary", (short)-1, (byte)0, (byte)0, "VARBINARY(MAX)")]
    [TestCase("decimal", (short)9, (byte)18, (byte)4, "DECIMAL(18,4)")]
    [TestCase("datetime2", (short)8, (byte)27, (byte)7, "DATETIME2(7)")]
    [TestCase("float", (short)8, (byte)53, (byte)0, "FLOAT(53)")]
    public void RenderSqlType_RendersDeclaration(string typeName, short maxLength, byte precision, byte scale, string expected)
    {
        //Arrange

        //Act
        string declaration = SqlStatementBuilder.RenderSqlType(typeName, maxLength, precision, scale);

        //Assert
        Assert.That(declaration, Is.EqualTo(expected));
    }

    /// <summary>
    /// A missing type name is rejected.
    /// </summary>
    /// <param name="typeName">The type name under test.</param>
    [TestCase(null)]
    [TestCase("")]
    public void RenderSqlType_MissingTypeName_Throws(string? typeName)
    {
        //Arrange

        //Act
        Action action = () => SqlStatementBuilder.RenderSqlType(typeName!, 0, 0, 0);

        //Assert
        Assert.Throws<ArgumentException>(action);
    }

    /// <summary>
    /// The truncate and drop statements quote the staging table name.
    /// </summary>
    [Test]
    public void BuildTruncateAndDrop_QuoteTableName()
    {
        //Arrange

        //Act
        string truncate = SqlStatementBuilder.BuildTruncate("#S1_Users");
        string drop = SqlStatementBuilder.BuildDropTempTableIfExists("#S1_Us'ers");

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(truncate, Is.EqualTo("TRUNCATE TABLE [#S1_Users];"));
            Assert.That(drop, Is.EqualTo(
                "IF OBJECT_ID(N'tempdb..#S1_Us''ers') IS NOT NULL DROP TABLE [#S1_Us'ers];"));
        });
    }

    #endregion
}
