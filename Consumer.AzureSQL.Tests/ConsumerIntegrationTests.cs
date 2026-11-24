using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Consumer.AzureSQL.Target;
using Configuration = PenguinConverters.Syntra.Consumer.AzureSQL.Target.Configuration;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Source;

namespace PenguinConverters.Syntra.Consumer.AzureSQL.Tests;

/// <summary>
/// Runs the consumer against a SQL Server LocalDB instance and verifies what reaches the target
/// table. The fixture is ignored when no LocalDB instance is reachable.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ConsumerIntegrationTests
{
    #region Constants

    /// <summary>
    /// Connection string of the LocalDB instance, without a database.
    /// </summary>
    private const string ServerConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connect Timeout=60;Encrypt=false";

    #endregion

    #region Fields

    /// <summary>
    /// Name of the database created for this fixture.
    /// </summary>
    private string _databaseName = string.Empty;

    /// <summary>
    /// Connection string of the database created for this fixture.
    /// </summary>
    private string _connectionString = string.Empty;

    /// <summary>
    /// Logger capturing what the consumer under test reports.
    /// </summary>
    private ListLogger _logger = new ListLogger();

    #endregion

    #region Methods

    /// <summary>
    /// Creates a database whose collation differs from that of <c>tempdb</c>.
    /// </summary>
    [OneTimeSetUp]
    public void CreateDatabase()
    {
        _databaseName = "SyntraConsumerTests_" + Guid.NewGuid().ToString("N");

        try
        {
            using SqlConnection connection = new SqlConnection(ServerConnectionString);
            connection.Open();
            Execute(connection, $"CREATE DATABASE [{_databaseName}] COLLATE Latin1_General_100_CS_AS;");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or PlatformNotSupportedException)
        {
            Assert.Ignore($"SQL Server LocalDB is not available: {ex.Message}");
        }

        _connectionString = ServerConnectionString + ";Database=" + _databaseName;
    }

    /// <summary>
    /// Drops the database created for this fixture.
    /// </summary>
    [OneTimeTearDown]
    public void DropDatabase()
    {
        if (_connectionString.Length == 0)
            return;

        SqlConnection.ClearAllPools();

        using SqlConnection connection = new SqlConnection(ServerConnectionString);
        connection.Open();
        Execute(connection,
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];");
    }

    /// <summary>
    /// Starts every test with an empty log.
    /// </summary>
    [SetUp]
    public void ResetLogger()
    {
        _logger = new ListLogger();
    }

    /// <summary>
    /// New keys are inserted, changed rows are updated and unchanged rows are not written.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Test]
    public async Task Synchronize_InsertsNewUpdatesChangedAndSkipsUnchangedRows()
    {
        //Arrange
        ExecuteOnDatabase("""
            CREATE TABLE dbo.Person (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                Name NVARCHAR(50) NULL,
                Born DATETIME2(3) NULL,
                Score DECIMAL(9,2) NULL,
                Version ROWVERSION NOT NULL);
            """);
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid third = Guid.NewGuid();
        Configuration configuration = Configure("dbo.Person", "Tests.Person", new[] { "Id" }, "Id", "Name", "Born", "Score", "Version");

        //Act
        await RunAsync(configuration,
            Person(first, "Ada", "1815-12-10T00:00:00Z", "1.50"),
            Person(second, "Grace", "1906-12-09T00:00:00Z", "2.50"));
        Dictionary<Guid, long> versionsBefore = ReadVersions();
        Consumer consumer = await RunAsync(configuration,
            Person(first, "Ada", "1815-12-10T00:00:00Z", "1.50"),
            Person(second, "GRACE", "1906-12-09T00:00:00Z", "2.50"),
            Person(third, "Alan", "1912-06-23T00:00:00Z", "3.50"));
        Dictionary<Guid, long> versionsAfter = ReadVersions();

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(consumer.HadErrors, Is.False);
            Assert.That(versionsAfter, Has.Count.EqualTo(3));
            Assert.That(versionsAfter[first], Is.EqualTo(versionsBefore[first]));
            Assert.That(versionsAfter[second], Is.Not.EqualTo(versionsBefore[second]));
            Assert.That(Scalar<string>($"SELECT Name FROM dbo.Person WHERE Id = '{second}'"), Is.EqualTo("GRACE"));
            Assert.That(Scalar<decimal>($"SELECT Score FROM dbo.Person WHERE Id = '{third}'"), Is.EqualTo(3.5m));
        });
    }

    /// <summary>
    /// Every entity is written whether the stream fills several batches, a partial one or none.
    /// </summary>
    /// <param name="entityCount">The number of entities the provider returns.</param>
    /// <param name="batchSize">The configured batch size.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [TestCase(0, 3)]
    [TestCase(2, 3)]
    [TestCase(3, 3)]
    [TestCase(10, 3)]
    public async Task Synchronize_AnyStreamLength_WritesEveryEntity(int entityCount, int batchSize)
    {
        //Arrange
        string table = $"Batch_{entityCount}_{batchSize}";
        ExecuteOnDatabase($"CREATE TABLE dbo.[{table}] (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(50) NULL);");
        Configuration configuration = Configure(table, "Tests." + table, new[] { "Id" }, "Id", "Name");
        configuration.BatchSize = batchSize;
        IEntity[] entities = Enumerable.Range(1, entityCount)
            .Select(number => Entity(("Id", number), ("Name", "n" + number)))
            .ToArray();

        //Act
        Consumer consumer = await RunAsync(configuration, entities);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(consumer.HadErrors, Is.False);
            Assert.That(Scalar<int>($"SELECT COUNT(*) FROM dbo.[{table}]"), Is.EqualTo(entityCount));
        });
    }

    /// <summary>
    /// A key staged several times in one batch ends in the state of its last occurrence.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Test]
    public async Task Synchronize_RepeatedKeyInOneBatch_LastStateWins()
    {
        //Arrange
        ExecuteOnDatabase("CREATE TABLE dbo.Repeat (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(50) NULL);");
        ExecuteOnDatabase("INSERT dbo.Repeat VALUES (3, N'stays'), (4, N'goes');");
        Configuration configuration = Configure("Repeat", "Tests.Repeat", new[] { "Id" }, "Id", "Name");

        //Act
        Consumer consumer = await RunAsync(configuration,
            Entity(("Id", 1), ("Name", "first")),
            Entity(("Id", 1), ("Name", "second")),
            Deleted(("Id", 2)),
            Entity(("Id", 2), ("Name", "recreated")),
            Deleted(("Id", 3)),
            Entity(("Id", 3), ("Name", "stays")),
            Entity(("Id", 4), ("Name", "updated")),
            Deleted(("Id", 4)));

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(consumer.HadErrors, Is.False);
            Assert.That(Scalar<string>("SELECT Name FROM dbo.Repeat WHERE Id = 1"), Is.EqualTo("second"));
            Assert.That(Scalar<string>("SELECT Name FROM dbo.Repeat WHERE Id = 2"), Is.EqualTo("recreated"));
            Assert.That(Scalar<string>("SELECT Name FROM dbo.Repeat WHERE Id = 3"), Is.EqualTo("stays"));
            Assert.That(Scalar<int>("SELECT COUNT(*) FROM dbo.Repeat WHERE Id = 4"), Is.EqualTo(0));
        });
    }

    /// <summary>
    /// With a soft-delete column a deletion stamps the row once, keeps its data, and a
    /// reappearing entity clears the stamp.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Test]
    public async Task Synchronize_SoftDelete_StampsOnceKeepsDataAndReinstates()
    {
        //Arrange
        ExecuteOnDatabase("""
            CREATE TABLE dbo.Soft (
                Id INT NOT NULL PRIMARY KEY,
                Name NVARCHAR(50) NULL,
                SoftDeleted DATETIME2 NULL);
            """);
        Configuration configuration = Configure("Soft", "Tests.Soft", new[] { "Id" }, "Id", "Name");
        configuration.HasDeletedColumn = true;
        configuration.DeletedColumnName = "softdeleted";
        await RunAsync(configuration, Entity(("Id", 1), ("Name", "kept")), Entity(("Id", 2), ("Name", "other")));

        //Act
        await RunAsync(configuration, Deleted(("Id", 1)), Deleted(("Id", 99)));
        DateTime firstStamp = Scalar<DateTime>("SELECT SoftDeleted FROM dbo.Soft WHERE Id = 1");
        string nameAfterDelete = Scalar<string>("SELECT Name FROM dbo.Soft WHERE Id = 1");
        await Task.Delay(50);
        await RunAsync(configuration, Deleted(("Id", 1)));
        DateTime secondStamp = Scalar<DateTime>("SELECT SoftDeleted FROM dbo.Soft WHERE Id = 1");
        await RunAsync(configuration, Entity(("Id", 1), ("Name", "kept")));

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(nameAfterDelete, Is.EqualTo("kept"));
            Assert.That(secondStamp, Is.EqualTo(firstStamp));
            Assert.That(Scalar<int>("SELECT COUNT(*) FROM dbo.Soft"), Is.EqualTo(2));
            Assert.That(Scalar<int>("SELECT COUNT(*) FROM dbo.Soft WHERE SoftDeleted IS NOT NULL"), Is.EqualTo(0));
        });
    }

    /// <summary>
    /// A schema-qualified target with a character key in the database collation is merged
    /// case-sensitively, and source values arrive converted to the column types.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Test]
    public async Task Synchronize_QualifiedTableAndCharacterKey_ConvertsValuesAndRespectsCollation()
    {
        //Arrange
        ExecuteOnDatabase("CREATE SCHEMA stage;");
        ExecuteOnDatabase("""
            CREATE TABLE stage.Account (
                Tenant INT NOT NULL,
                Login NVARCHAR(100) NOT NULL,
                ObjectId UNIQUEIDENTIFIER NULL,
                Enabled BIT NULL,
                Tags NVARCHAR(MAX) NULL,
                Payload XML NULL,
                CONSTRAINT PK_Account PRIMARY KEY (Tenant, Login));
            """);
        Guid objectId = Guid.NewGuid();
        Configuration configuration = Configure(
            "stage.Account", "Tests.Account", new[] { "Tenant", "Login" }, "Tenant", "Login", "ObjectId", "Enabled", "Tags", "Payload", "Missing");
        configuration.Columns!["Login"].SourceProperty = "sAMAccountName";
        JsonElement json = JsonDocument.Parse("{\"enabled\":true,\"tenant\":7}").RootElement;

        //Act
        Consumer consumer = await RunAsync(configuration,
            Entity(("Tenant", json.GetProperty("tenant")), ("sAMAccountName", "ada"), ("ObjectId", objectId.ToByteArray()),
                ("Enabled", json.GetProperty("enabled")), ("Tags", new[] { "a", "b" }), ("Payload", "<a>1</a>")),
            Entity(("Tenant", "7"), ("sAMAccountName", "ADA"), ("ObjectId", objectId.ToString()),
                ("Enabled", "0"), ("Tags", null), ("Payload", null)));
        Consumer second = await RunAsync(configuration,
            Entity(("Tenant", 7), ("sAMAccountName", "ada"), ("ObjectId", objectId), ("Enabled", true),
                ("Tags", new[] { "a", "b" }), ("Payload", "<a>2</a>")));

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(consumer.HadErrors, Is.False);
            Assert.That(second.HadErrors, Is.False);
            Assert.That(Scalar<int>("SELECT COUNT(*) FROM stage.Account"), Is.EqualTo(2));
            Assert.That(Scalar<Guid>("SELECT ObjectId FROM stage.Account WHERE Login = N'ada'"), Is.EqualTo(objectId));
            Assert.That(Scalar<bool>("SELECT Enabled FROM stage.Account WHERE Login = N'ada'"), Is.True);
            Assert.That(Scalar<string>("SELECT Tags FROM stage.Account WHERE Login = N'ada'"), Is.EqualTo("[\"a\",\"b\"]"));
            Assert.That(Scalar<string>("SELECT CAST(Payload AS NVARCHAR(MAX)) FROM stage.Account WHERE Login = N'ada'"), Is.EqualTo("<a>2</a>"));
            Assert.That(Scalar<bool>("SELECT Enabled FROM stage.Account WHERE Login = N'ADA'"), Is.False);
            Assert.That(_logger.Messages(LogLevel.Warning), Has.Some.Contains("Missing"));
        });
    }

    /// <summary>
    /// An entity without a key value, or with a value its column cannot hold, is left out and
    /// reported while the remaining entities are written.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Test]
    public async Task Synchronize_UnusableEntity_IsSkippedAndReported()
    {
        //Arrange
        ExecuteOnDatabase("CREATE TABLE dbo.Skip (Id INT NOT NULL PRIMARY KEY, Born DATE NULL);");
        Configuration configuration = Configure("Skip", "Tests.Skip", new[] { "Id" }, "Id", "Born");

        //Act
        Consumer consumer = await RunAsync(configuration,
            Entity(("Id", null), ("Born", "2020-01-01")),
            Entity(("Id", 1), ("Born", "not a date")),
            Entity(("Id", 2), ("Born", "2020-01-02")));

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(consumer.HadErrors, Is.True);
            Assert.That(Scalar<int>("SELECT COUNT(*) FROM dbo.Skip"), Is.EqualTo(1));
            Assert.That(_logger.Messages(LogLevel.Error), Has.Count.EqualTo(2));
        });
    }

    /// <summary>
    /// The staging table exists under the synchronization name while entities are read and is
    /// gone once the run has ended, whether it succeeded or failed.
    /// </summary>
    /// <param name="fail">Whether the provider fails in the middle of the stream.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [TestCase(false)]
    [TestCase(true)]
    public async Task Synchronize_StagingTable_ExistsDuringRunAndIsDroppedAfterwards(bool fail)
    {
        //Arrange
        string table = "Life_" + fail;
        string objectNamespace = "Tests.Lifecycle." + Guid.NewGuid().ToString("N");
        string pattern = SqlStatementBuilder.BuildTempTableName(objectNamespace).Replace("_", "[_]") + "[_][_]%";
        ExecuteOnDatabase($"CREATE TABLE dbo.[{table}] (Id INT NOT NULL PRIMARY KEY);");
        Configuration configuration = Configure(table, objectNamespace, new[] { "Id" }, "Id");
        int duringRun = -1;
        ListProvider provider = new ListProvider(new[] { Entity(("Id", 1)), Entity(("Id", 2)) })
        {
            AfterFirstEntity = () =>
            {
                duringRun = CountStagingTables(pattern);
                if (fail)
                    throw new InvalidOperationException("Source failed.");
            }
        };
        Consumer consumer = CreateConsumer(configuration);

        //Act
        Exception? failure = null;
        try
        {
            await consumer.SynchronizeAsync(provider);
        }
        catch (InvalidOperationException ex)
        {
            failure = ex;
        }

        int afterSynchronize = fail ? CountStagingTables(pattern) : -1;
        await consumer.FinalizeAsync(provider);
        await consumer.DisposeAsync();
        int afterRun = CountStagingTables(pattern);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(failure is not null, Is.EqualTo(fail));
            Assert.That(consumer.HadErrors, Is.EqualTo(fail));
            Assert.That(duringRun, Is.EqualTo(1));
            Assert.That(afterSynchronize, Is.EqualTo(fail ? 0 : -1));
            Assert.That(afterRun, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// A cancelled run drops the staging table without recording an error.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Test]
    public async Task Synchronize_Cancelled_DropsStagingTable()
    {
        //Arrange
        string objectNamespace = "Tests.Cancel." + Guid.NewGuid().ToString("N");
        string pattern = SqlStatementBuilder.BuildTempTableName(objectNamespace).Replace("_", "[_]") + "[_][_]%";
        ExecuteOnDatabase("CREATE TABLE dbo.Cancel (Id INT NOT NULL PRIMARY KEY);");
        Configuration configuration = Configure("Cancel", objectNamespace, new[] { "Id" }, "Id");
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        ListProvider provider = new ListProvider(new[] { Entity(("Id", 1)), Entity(("Id", 2)) })
        {
            AfterFirstEntity = cancellation.Cancel
        };
        Consumer consumer = CreateConsumer(configuration);

        //Act
        Func<Task> action = () => consumer.SynchronizeAsync(provider, cancellation.Token);

        //Assert
        Assert.CatchAsync<OperationCanceledException>(action);
        Assert.Multiple(() =>
        {
            Assert.That(CountStagingTables(pattern), Is.EqualTo(0));
            Assert.That(consumer.HadErrors, Is.False);
        });
        await consumer.DisposeAsync();
    }

    /// <summary>
    /// A target without a unique index on the primary keys is reported, and a missing primary
    /// key column stops the run before anything is staged.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Test]
    public async Task Synchronize_TargetDefinitionProblems_AreReported()
    {
        //Arrange
        ExecuteOnDatabase("CREATE TABLE dbo.Heap (Id INT NOT NULL, Name NVARCHAR(10) NULL);");
        Configuration heap = Configure("Heap", "Tests.Heap", new[] { "Id" }, "Id", "Name");
        Configuration missingKey = Configure("Heap", "Tests.Heap2", new[] { "Nope" }, "Id", "Name");
        Consumer broken = CreateConsumer(missingKey);

        //Act
        await RunAsync(heap, Entity(("Id", 1), ("Name", "a")));
        List<string> warnings = _logger.Messages(LogLevel.Warning);
        Func<Task> action = () => broken.SynchronizeAsync(new ListProvider(Array.Empty<IEntity>()));

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(warnings, Has.Some.Contains("no unique index"));
            Assert.That(Scalar<int>("SELECT COUNT(*) FROM dbo.Heap"), Is.EqualTo(1));
        });
        Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.That(broken.HadErrors, Is.True);
    }

    /// <summary>
    /// Against a target with the statistics of a two-billion-row table the batch statement seeks
    /// the target by its key, never scans it and converts no value implicitly.
    /// </summary>
    /// <param name="softDeleteColumn">The soft-delete column, or <c>null</c> for physical deletion.</param>
    [TestCase(null)]
    [TestCase("BigDeleted")]
    public void MergePlan_OnVeryLargeTarget_SeeksKeysWithoutImplicitConversion(string? softDeleteColumn)
    {
        //Arrange
        string table = softDeleteColumn is null ? "BigHard" : "BigSoft";
        ExecuteOnDatabase($"""
            CREATE TABLE dbo.{table} (
                Tenant INT NOT NULL,
                Login VARCHAR(100) NOT NULL,
                ObjectId UNIQUEIDENTIFIER NULL,
                Display NVARCHAR(200) NULL,
                Changed DATETIME2(7) NULL,
                BigDeleted DATETIME2 NULL,
                CONSTRAINT PK_{table} PRIMARY KEY CLUSTERED (Tenant, Login));
            UPDATE STATISTICS dbo.{table} WITH ROWCOUNT = 2000000000, PAGECOUNT = 40000000;
            """);
        List<TargetColumn> columns = new List<TargetColumn>
        {
            new TargetColumn("Tenant", "INT", "INT", null),
            new TargetColumn("Login", "VARCHAR", "VARCHAR(100)", "Latin1_General_100_CS_AS"),
            new TargetColumn("ObjectId", "UNIQUEIDENTIFIER", "UNIQUEIDENTIFIER", null),
            new TargetColumn("Display", "NVARCHAR", "NVARCHAR(200)", "Latin1_General_100_CS_AS"),
            new TargetColumn("Changed", "DATETIME2", "DATETIME2(7)", null)
        };
        string merge = SqlStatementBuilder.BuildMerge(
            $"[dbo].[{table}]", "#S1_Plan", columns, new List<string> { "Tenant", "Login" }, softDeleteColumn);

        //Act
        string plan;
        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            Execute(connection, SqlStatementBuilder.BuildCreateTempTable("#S1_Plan", columns));
            Execute(connection, """
                INSERT [#S1_Plan] (Tenant, Login, ObjectId, Display, Changed, __S1Deleted, __S1Sequence)
                SELECT TOP (10000) 1, CONVERT(VARCHAR(100), NEWID()), NEWID(), N'x', SYSUTCDATETIME(), 0,
                       ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
                  FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b;
                """);
            Execute(connection, "SET SHOWPLAN_XML ON;");
            using SqlCommand command = new SqlCommand(merge, connection);
            plan = (string)command.ExecuteScalar();
        }

        XDocument document = XDocument.Parse(plan);
        XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        List<string> targetOperators = document.Descendants(ns + "RelOp")
            .Where(relOp => relOp.Elements().Any(child => child.Elements(ns + "Object")
                .Any(target => (string?)target.Attribute("Table") == $"[{table}]")))
            .Select(relOp => (string)relOp.Attribute("PhysicalOp")!)
            .ToList();

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(targetOperators, Has.Some.EqualTo("Clustered Index Seek"));
            Assert.That(targetOperators, Has.None.Contains("Scan"));
            Assert.That(plan, Does.Not.Contain("CONVERT_IMPLICIT"));
            Assert.That(plan, Does.Not.Contain("PlanAffectingConvert"));
        });
    }

    /// <summary>
    /// A full synchronization removes the rows the source no longer returns, across several
    /// ranges, and leaves no temporary table behind.
    /// </summary>
    /// <param name="softDelete">Whether the target records deletions in a column.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [TestCase(false)]
    [TestCase(true)]
    public async Task Reconcile_FullSynchronization_RemovesRowsTheSourceNoLongerReturns(bool softDelete)
    {
        //Arrange
        string table = "Recon_" + softDelete;
        string objectNamespace = "Tests.Recon." + Guid.NewGuid().ToString("N");
        ExecuteOnDatabase($"""
            CREATE TABLE dbo.[{table}] (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(50) NULL, Gone DATETIME2 NULL);
            INSERT dbo.[{table}] (Id, Name, Gone)
            SELECT TOP (20) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), N'old', NULL FROM sys.all_columns;
            UPDATE dbo.[{table}] SET Gone = '2020-01-01' WHERE Id = 20;
            """);
        Configuration configuration = Configure(table, objectNamespace, new[] { "Id" }, "Id", "Name");
        configuration.ReconcileDeletions = true;
        configuration.Threshold = 50;
        configuration.BatchSize = 3;
        configuration.HasDeletedColumn = softDelete;
        configuration.DeletedColumnName = "Gone";
        IEntity[] entities = Enumerable.Range(1, softDelete ? 12 : 13)
            .Select(number => Entity(("Id", number), ("Name", "new")))
            .ToArray();

        //Act
        Consumer consumer = await RunAsync(configuration, entities);
        string active = softDelete ? "Gone IS NULL" : "1 = 1";

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(consumer.HadErrors, Is.False);
            Assert.That(Scalar<int>($"SELECT COUNT(*) FROM dbo.[{table}] WHERE {active}"), Is.EqualTo(softDelete ? 12 : 13));
            Assert.That(Scalar<int>($"SELECT COUNT(*) FROM dbo.[{table}]"), Is.EqualTo(softDelete ? 20 : 13));
            Assert.That(Scalar<int>($"SELECT COUNT(*) FROM dbo.[{table}] WHERE Name = N'new'"), Is.EqualTo(softDelete ? 12 : 13));
            Assert.That(
                softDelete ? Scalar<DateTime>($"SELECT Gone FROM dbo.[{table}] WHERE Id = 20") : new DateTime(2020, 1, 1),
                Is.EqualTo(new DateTime(2020, 1, 1)));
            Assert.That(
                CountStagingTables(SqlStatementBuilder.BuildTempTableName(objectNamespace).Replace("_", "[_]") + "%"),
                Is.EqualTo(0));
        });
    }

    /// <summary>
    /// When more rows are missing than the threshold allows, none is removed and the run fails.
    /// </summary>
    /// <param name="returned">The number of entities the source returns out of ten target rows.</param>
    /// <param name="threshold">The configured threshold in percent, or <c>null</c> for the default.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [TestCase(0, null)]
    [TestCase(8, null)]
    [TestCase(5, 49)]
    public async Task Reconcile_ThresholdExceeded_RemovesNothingAndRecordsAnError(int returned, int? threshold)
    {
        //Arrange
        string table = $"Limit_{returned}_{threshold}";
        ExecuteOnDatabase($"""
            CREATE TABLE dbo.[{table}] (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(50) NULL);
            INSERT dbo.[{table}] (Id, Name)
            SELECT TOP (10) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), N'old' FROM sys.all_columns;
            """);
        Configuration configuration = Configure(table, "Tests." + table, new[] { "Id" }, "Id", "Name");
        configuration.ReconcileDeletions = true;
        configuration.Threshold = threshold;
        IEntity[] entities = Enumerable.Range(1, returned)
            .Select(number => Entity(("Id", number), ("Name", "new")))
            .ToArray();

        //Act
        Consumer consumer = await RunAsync(configuration, entities);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(consumer.HadErrors, Is.True);
            Assert.That(Scalar<int>($"SELECT COUNT(*) FROM dbo.[{table}]"), Is.EqualTo(10));
            Assert.That(_logger.Messages(LogLevel.Error), Has.Some.Contains("exceeds the threshold"));
        });
    }

    /// <summary>
    /// A share of missing rows equal to the threshold is still removed.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Test]
    public async Task Reconcile_ShareEqualToThreshold_RemovesRows()
    {
        //Arrange
        ExecuteOnDatabase("""
            CREATE TABLE dbo.Edge (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(50) NULL);
            INSERT dbo.Edge (Id, Name)
            SELECT TOP (10) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), N'old' FROM sys.all_columns;
            """);
        Configuration configuration = Configure("Edge", "Tests.Edge", new[] { "Id" }, "Id", "Name");
        configuration.ReconcileDeletions = true;
        IEntity[] entities = Enumerable.Range(1, 9).Select(number => Entity(("Id", number), ("Name", "new"))).ToArray();

        //Act
        Consumer consumer = await RunAsync(configuration, entities);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(consumer.HadErrors, Is.False);
            Assert.That(Scalar<int>("SELECT COUNT(*) FROM dbo.Edge"), Is.EqualTo(9));
        });
    }

    /// <summary>
    /// Reconciliation does not take place for a delta synchronization, when it is not enabled,
    /// or when an entity could not be written.
    /// </summary>
    /// <param name="reconcile">Whether reconciliation is enabled.</param>
    /// <param name="delta">Whether the run is a delta synchronization.</param>
    /// <param name="withUnusableEntity">Whether the source returns an entity that cannot be written.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    [TestCase(true, false, true)]
    public async Task Reconcile_NotApplicable_RemovesNothing(bool reconcile, bool delta, bool withUnusableEntity)
    {
        //Arrange
        string table = $"NoRecon_{reconcile}_{delta}_{withUnusableEntity}";
        ExecuteOnDatabase($"""
            CREATE TABLE dbo.[{table}] (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(50) NULL);
            INSERT dbo.[{table}] (Id, Name)
            SELECT TOP (10) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), N'old' FROM sys.all_columns;
            """);
        Configuration configuration = Configure(table, "Tests." + table, new[] { "Id" }, "Id", "Name");
        configuration.ReconcileDeletions = reconcile;
        configuration.Delta = delta;
        configuration.Threshold = 100;
        List<IEntity> entities = Enumerable.Range(1, 9)
            .Select(number => Entity(("Id", number), ("Name", "new")))
            .ToList();
        if (withUnusableEntity)
            entities.Add(Entity(("Id", "not a number"), ("Name", "x")));

        //Act
        Consumer consumer = await RunAsync(configuration, entities.ToArray());

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(consumer.HadErrors, Is.EqualTo(withUnusableEntity));
            Assert.That(Scalar<int>($"SELECT COUNT(*) FROM dbo.[{table}]"), Is.EqualTo(10));
        });
    }

    /// <summary>
    /// Deletion constraints keep reconciliation to the rows of one source, and the threshold is
    /// measured against those rows only.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Test]
    public async Task Reconcile_WithConstraints_TouchesOnlyRowsInScope()
    {
        //Arrange
        ExecuteOnDatabase("""
            CREATE TABLE dbo.Shared (
                Tenant INT NOT NULL,
                Kind VARCHAR(10) NULL,
                Id INT NOT NULL,
                Name NVARCHAR(50) NULL,
                CONSTRAINT PK_Shared PRIMARY KEY (Tenant, Id));
            INSERT dbo.Shared (Tenant, Kind, Id, Name)
            SELECT 7, 'user', n, N'mine' FROM (VALUES (1),(2),(3),(4)) v(n)
            UNION ALL SELECT 7, NULL, n, N'untyped' FROM (VALUES (5),(6)) v(n)
            UNION ALL SELECT 8, 'user', n, N'theirs' FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8)) v(n);
            """);
        Configuration configuration = Configure(
            "Shared", "Tests.Shared", new[] { "Tenant", "Id" }, "Tenant", "Kind", "Id", "Name");
        configuration.ReconcileDeletions = true;
        configuration.Threshold = 25;
        configuration.DeletionConstraints = new Dictionary<string, string?> { ["tenant"] = "7", ["Kind"] = "user" };

        //Act
        Consumer consumer = await RunAsync(configuration,
            Entity(("Tenant", 7), ("Kind", "user"), ("Id", 1), ("Name", "mine")),
            Entity(("Tenant", 7), ("Kind", "user"), ("Id", 2), ("Name", "mine")),
            Entity(("Tenant", 7), ("Kind", "user"), ("Id", 3), ("Name", "mine")));

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(consumer.HadErrors, Is.False);
            Assert.That(Scalar<int>("SELECT COUNT(*) FROM dbo.Shared WHERE Tenant = 7 AND Kind = 'user'"), Is.EqualTo(3));
            Assert.That(Scalar<int>("SELECT COUNT(*) FROM dbo.Shared WHERE Tenant = 7 AND Kind IS NULL"), Is.EqualTo(2));
            Assert.That(Scalar<int>("SELECT COUNT(*) FROM dbo.Shared WHERE Tenant = 8"), Is.EqualTo(8));
        });
    }

    /// <summary>
    /// A constraint on a column the target does not have stops the run before anything is written.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Test]
    public async Task Reconcile_ConstraintOnUnknownColumn_Throws()
    {
        //Arrange
        ExecuteOnDatabase("CREATE TABLE dbo.BadScope (Id INT NOT NULL PRIMARY KEY);");
        Configuration configuration = Configure("BadScope", "Tests.BadScope", new[] { "Id" }, "Id");
        configuration.ReconcileDeletions = true;
        configuration.DeletionConstraints = new Dictionary<string, string?> { ["Nope"] = "1" };
        Consumer consumer = CreateConsumer(configuration);

        //Act
        Func<Task> action = () => consumer.SynchronizeAsync(new ListProvider(new[] { Entity(("Id", 1)) }));

        //Assert
        Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.That(Scalar<int>("SELECT COUNT(*) FROM dbo.BadScope"), Is.EqualTo(0));
        await consumer.DisposeAsync();
    }

    /// <summary>
    /// On a target with the statistics of a two-billion-row table, removing a range of candidates
    /// seeks the target by key, and no reconciliation statement converts a target column.
    /// </summary>
    /// <param name="softDeleteColumn">The soft-delete column, or <c>null</c> for physical deletion.</param>
    [TestCase(null)]
    [TestCase("Gone")]
    public void ReconcilePlans_OnVeryLargeTarget_SeekOnApplyAndNeverConvertTheTarget(string? softDeleteColumn)
    {
        //Arrange
        string table = softDeleteColumn is null ? "HugeHard" : "HugeSoft";
        ExecuteOnDatabase($"""
            CREATE TABLE dbo.{table} (
                Tenant INT NOT NULL,
                Login VARCHAR(100) NOT NULL,
                Kind VARCHAR(10) NULL,
                Gone DATETIME2 NULL,
                CONSTRAINT PK_{table} PRIMARY KEY CLUSTERED (Tenant, Login));
            UPDATE STATISTICS dbo.{table} WITH ROWCOUNT = 2000000000, PAGECOUNT = 40000000;
            """);
        List<TargetColumn> keys = new List<TargetColumn>
        {
            new TargetColumn("Tenant", "INT", "INT", null, 4),
            new TargetColumn("Login", "VARCHAR", "VARCHAR(100)", "Latin1_General_100_CS_AS", 100)
        };
        List<string> keyNames = new List<string> { "Tenant", "Login" };
        List<string> scope = new List<string>
        {
            SqlStatementBuilder.BuildConstraint(new TargetColumn("Tenant", "INT", "INT", null, 4), "@C0"),
            SqlStatementBuilder.BuildConstraint(new TargetColumn("Kind", "VARCHAR", "VARCHAR(10)", "Latin1_General_100_CS_AS", 10), "@C1")
        };
        if (softDeleteColumn is not null)
            scope.Add($"DST.[{softDeleteColumn}] IS NULL");

        //Act
        List<string> plans = new List<string>();
        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            Execute(connection, SqlStatementBuilder.BuildCreateKeyTable("#S1_P_K", keys));
            Execute(connection, SqlStatementBuilder.BuildCreateCandidateTable("#S1_P_X", keys));
            Execute(connection, "INSERT [#S1_P_X] (Tenant, Login) SELECT TOP (2000) 7, CONVERT(VARCHAR(100), NEWID()) FROM sys.all_columns;");
            Execute(connection, "SET SHOWPLAN_XML ON;");

            foreach (string statement in new[]
            {
                SqlStatementBuilder.BuildReconciliationCount($"[dbo].[{table}]", "#S1_P_K", keyNames, scope),
                SqlStatementBuilder.BuildCollectCandidates($"[dbo].[{table}]", "#S1_P_K", "#S1_P_X", keyNames, scope),
                SqlStatementBuilder.BuildApplyCandidates($"[dbo].[{table}]", "#S1_P_X", keyNames, softDeleteColumn)
            })
            {
                using SqlCommand command = new SqlCommand(
                    "DECLARE @C0 NVARCHAR(MAX) = N'7', @C1 NVARCHAR(MAX) = N'user', @From BIGINT = 1, @To BIGINT = 2000; " + statement,
                    connection);
                plans.Add((string)command.ExecuteScalar());
            }
        }

        XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        List<string> applyOperators = XDocument.Parse(plans[2]).Descendants(ns + "RelOp")
            .Where(relOp => relOp.Elements().Any(child => child.Elements(ns + "Object")
                .Any(target => (string?)target.Attribute("Table") == $"[{table}]")))
            .Select(relOp => (string)relOp.Attribute("PhysicalOp")!)
            .ToList();
        string targetConversion = $"CONVERT_IMPLICIT\\([^)]*\\[{table}\\]";

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(applyOperators, Has.Some.EqualTo("Clustered Index Seek"));
            Assert.That(applyOperators, Has.None.Contains("Scan"));
            Assert.That(plans, Has.None.Contains("PlanAffectingConvert"));
            Assert.That(plans, Has.None.Match(targetConversion));
            Assert.That(plans[0], Does.Contain("Seek"));
        });
    }

    /// <summary>
    /// Builds a consumer configuration for a target table of the fixture database.
    /// </summary>
    /// <param name="tableName">The target table name.</param>
    /// <param name="objectNamespace">The synchronization name.</param>
    /// <param name="primaryKeys">The primary key column names.</param>
    /// <param name="columns">The configured column names.</param>
    /// <returns>The configuration.</returns>
    private Configuration Configure(string tableName, string objectNamespace, string[] primaryKeys, params string[] columns)
    {
        return new Configuration
        {
            ConnectionString = new Secret { Value = _connectionString, Protected = false },
            TableName = tableName,
            ObjectNamespace = objectNamespace,
            PrimaryKeys = primaryKeys.ToList(),
            Columns = columns.ToDictionary(name => name, name => new ColumnDefinition())
        };
    }

    /// <summary>
    /// Creates a consumer bound to a configuration and to the capturing logger.
    /// </summary>
    /// <param name="configuration">The consumer configuration.</param>
    /// <returns>The consumer.</returns>
    private Consumer CreateConsumer(Configuration configuration)
    {
        Consumer consumer = new Consumer { Configuration = configuration };
        consumer.SetLogger(_logger);
        return consumer;
    }

    /// <summary>
    /// Runs one complete synchronization of the given entities.
    /// </summary>
    /// <param name="configuration">The consumer configuration.</param>
    /// <param name="entities">The entities the provider returns.</param>
    /// <returns>The consumer after the run has been finalized and released.</returns>
    private async Task<Consumer> RunAsync(Configuration configuration, params IEntity[] entities)
    {
        Consumer consumer = CreateConsumer(configuration);
        ListProvider provider = new ListProvider(entities);

        await consumer.SynchronizeAsync(provider);
        await consumer.FinalizeAsync(provider);
        await consumer.DisposeAsync();

        return consumer;
    }

    /// <summary>
    /// Creates a person entity whose values are all text.
    /// </summary>
    /// <param name="id">The person identifier.</param>
    /// <param name="name">The person name.</param>
    /// <param name="born">The birth date in ISO 8601 form.</param>
    /// <param name="score">The score in invariant decimal form.</param>
    /// <returns>The entity.</returns>
    private static IEntity Person(Guid id, string name, string born, string score)
    {
        return Entity(("Id", id.ToString()), ("Name", name), ("Born", born), ("Score", score));
    }

    /// <summary>
    /// Creates an entity from property names and values.
    /// </summary>
    /// <param name="properties">The properties of the entity.</param>
    /// <returns>The entity.</returns>
    private static IEntity Entity(params (string Name, object? Value)[] properties)
    {
        Entity entity = new Entity();
        foreach ((string name, object? value) in properties)
            entity[name] = value;

        return entity;
    }

    /// <summary>
    /// Creates an entity the source reports as deleted.
    /// </summary>
    /// <param name="properties">The key properties of the entity.</param>
    /// <returns>The entity.</returns>
    private static IEntity Deleted(params (string Name, object? Value)[] properties)
    {
        IEntity entity = Entity(properties);
        entity.State = EntityState.Deleted;
        return entity;
    }

    /// <summary>
    /// Reads the row version of every person.
    /// </summary>
    /// <returns>The row versions by person identifier.</returns>
    private Dictionary<Guid, long> ReadVersions()
    {
        Dictionary<Guid, long> versions = new Dictionary<Guid, long>();

        using SqlConnection connection = new SqlConnection(_connectionString);
        connection.Open();
        using SqlCommand command = new SqlCommand("SELECT Id, CAST(Version AS BIGINT) FROM dbo.Person", connection);
        using SqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
            versions[reader.GetGuid(0)] = reader.GetInt64(1);

        return versions;
    }

    /// <summary>
    /// Counts the temporary tables whose name matches a pattern, across all sessions.
    /// </summary>
    /// <param name="pattern">The <c>LIKE</c> pattern of the table name.</param>
    /// <returns>The number of matching temporary tables.</returns>
    private int CountStagingTables(string pattern)
    {
        using SqlConnection connection = new SqlConnection(_connectionString);
        connection.Open();
        using SqlCommand command = new SqlCommand("SELECT COUNT(*) FROM tempdb.sys.tables WHERE name LIKE @Pattern", connection);
        command.Parameters.AddWithValue("@Pattern", pattern);
        return (int)command.ExecuteScalar();
    }

    /// <summary>
    /// Reads a single value from the fixture database.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="sql">The query returning one value.</param>
    /// <returns>The value.</returns>
    private T Scalar<T>(string sql)
    {
        using SqlConnection connection = new SqlConnection(_connectionString);
        connection.Open();
        using SqlCommand command = new SqlCommand(sql, connection);
        return (T)command.ExecuteScalar();
    }

    /// <summary>
    /// Executes a statement on the fixture database.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    private void ExecuteOnDatabase(string sql)
    {
        using SqlConnection connection = new SqlConnection(_connectionString);
        connection.Open();
        Execute(connection, sql);
    }

    /// <summary>
    /// Executes a statement on an open connection.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="sql">The statement text.</param>
    private static void Execute(SqlConnection connection, string sql)
    {
        using SqlCommand command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        command.ExecuteNonQuery();
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Provider returning a fixed list of entities.
    /// </summary>
    private sealed class ListProvider : IProvider
    {
        #region Fields

        /// <summary>
        /// The entities returned by the provider.
        /// </summary>
        private readonly IReadOnlyList<IEntity> _entities;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ListProvider"/> class.
        /// </summary>
        /// <param name="entities">The entities to return.</param>
        public ListProvider(IReadOnlyList<IEntity> entities)
        {
            _entities = entities;
        }

        #endregion

        #region Properties

        /// <inheritdoc />
        public byte[]? Metadata => null;

        /// <summary>
        /// Gets or sets the action invoked after the first entity has been returned and consumed.
        /// </summary>
        public Action? AfterFirstEntity { get; set; }

        #endregion

        #region Methods

        /// <inheritdoc />
        public async IAsyncEnumerable<IEntity> RetrieveAsync(
            IEnumerable<string> properties,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                if (i == 1)
                    AfterFirstEntity?.Invoke();

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return _entities[i];
            }
        }

        #endregion
    }

    /// <summary>
    /// Logger keeping every message it receives.
    /// </summary>
    private sealed class ListLogger : ILogger
    {
        #region Fields

        /// <summary>
        /// The received messages with their levels.
        /// </summary>
        private readonly List<(LogLevel Level, string Message)> _entries = new List<(LogLevel Level, string Message)>();

        #endregion

        #region Methods

        /// <summary>
        /// Returns the messages logged at a level.
        /// </summary>
        /// <param name="level">The level to filter by.</param>
        /// <returns>The formatted messages.</returns>
        public List<string> Messages(LogLevel level)
        {
            return _entries.Where(entry => entry.Level == level).Select(entry => entry.Message).ToList();
        }

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }

        #endregion
    }

    #endregion
}
