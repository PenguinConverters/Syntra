using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PenguinConverters.Keyra;
using PenguinConverters.Syntra.Core.Security;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Core.Target;

namespace PenguinConverters.Syntra.Core;

/// <summary>
/// Dynamically loads connector assemblies and creates provider/consumer instances via reflection.
/// Uses the Builder pattern to configure dependencies before building.
/// </summary>
/// <typeparam name="T">The builder interface type: <see cref="IProviderBuilder"/> or <see cref="IConsumerBuilder"/>.</typeparam>
public class InstanceBuilder<T> where T : class
{
    #region Constants

    /// <summary>
    /// Directory beneath the application a connector deployed as a file may be placed in, so that
    /// what an operator has added stays visibly apart from what shipped.
    /// </summary>
    public const string ConnectorDirectoryName = "connectors";

    #endregion

    #region Fields

    private readonly string _assemblyName;
    private byte[]? _configuration;
    private byte[]? _metadata;
    private Func<byte[], Type, object>? _deserializer;
    private Decryptor? _decryptor;
    private ILogger _logger = NullLogger.Instance;
    private byte[]? _expectedPublicKey;
    private string? _expectedPublisher;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceBuilder{T}"/> class.
    /// </summary>
    /// <param name="assemblyName">The name of the assembly containing the connector implementation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assemblyName"/> is null or empty.</exception>
    public InstanceBuilder(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            throw new ArgumentNullException(nameof(assemblyName));

        _assemblyName = assemblyName;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Sets the serialized configuration bytes.
    /// </summary>
    /// <param name="configuration">The configuration bytes.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithConfiguration(byte[] configuration)
    {
        _configuration = configuration;
        return this;
    }

    /// <summary>
    /// Sets the serialized metadata bytes for delta synchronization.
    /// </summary>
    /// <param name="metadata">The metadata bytes, or <c>null</c> for full sync.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithMetadata(byte[]? metadata)
    {
        _metadata = metadata;
        return this;
    }

    /// <summary>
    /// Sets the deserializer function.
    /// </summary>
    /// <param name="deserializer">The deserializer function.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithDeserializer(Func<byte[], Type, object> deserializer)
    {
        _deserializer = deserializer;
        return this;
    }

    /// <summary>
    /// Sets the Keyra decryptor used to disclose protected configuration values.
    /// </summary>
    /// <param name="decryptor">The decryptor holding the vault key.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithDecryptor(Decryptor decryptor)
    {
        _decryptor = decryptor;
        return this;
    }

    /// <summary>
    /// Sets the logger instance.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithLogger(ILogger logger)
    {
        _logger = logger ?? NullLogger.Instance;
        return this;
    }

    /// <summary>
    /// Sets the expected public key for assembly signature validation.
    /// </summary>
    /// <param name="publicKey">The expected public key bytes.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithPublicKey(byte[] publicKey)
    {
        _expectedPublicKey = publicKey;
        return this;
    }

    /// <summary>
    /// Sets the publisher a connector's Authenticode signature is expected to name.
    /// </summary>
    /// <remarks>
    /// Defaults to the publisher of the assembly this type lives in, so a connector is expected to
    /// come from whoever produced the framework it plugs into. A mismatch is reported and does not
    /// stop the connector loading: an operator's own build is a legitimate thing to run, and this
    /// records what was run rather than deciding it.
    /// </remarks>
    /// <param name="publisher">The common name of the expected signing certificate.</param>
    /// <returns>This builder instance for fluent chaining.</returns>
    public InstanceBuilder<T> WithPublisher(string publisher)
    {
        _expectedPublisher = publisher;
        return this;
    }

    /// <summary>
    /// Loads the assembly, discovers the builder implementation, configures it,
    /// and builds the provider or consumer instance.
    /// </summary>
    /// <returns>The built provider or consumer instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the assembly cannot be loaded, no implementation is found,
    /// or signature validation fails.
    /// </exception>
    public object Build()
    {
        Assembly assembly = LoadAssembly(_assemblyName);

        ValidateSignature(assembly);

        Type builderType = assembly.GetExportedTypes()
            .FirstOrDefault(t => typeof(T).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            ?? throw new InvalidOperationException(
                $"Assembly '{_assemblyName}' does not contain an implementation of {typeof(T).Name}.");

        T builder = (T)(Activator.CreateInstance(builderType)
            ?? throw new InvalidOperationException(
                $"Failed to create instance of '{builderType.FullName}'."));

        ConfigureBuilder(builder);

        return builder switch
        {
            IProviderBuilder providerBuilder => providerBuilder.Build(),
            IConsumerBuilder consumerBuilder => consumerBuilder.Build(),
            _ => throw new InvalidOperationException(
                $"Builder type '{typeof(T).Name}' is not a supported builder interface.")
        };
    }

    /// <summary>
    /// Loads a connector assembly, whether it was referenced at build time or dropped into the
    /// application directory as a file.
    /// </summary>
    /// <remarks>
    /// A referenced connector is listed in the application's <c>deps.json</c> and resolves through
    /// the default load context. A connector deployed as a file is not, and the default context of
    /// .NET does not probe the application directory for it - the .NET Framework loader did, which
    /// is why dropping an assembly beside the host used to be all that was required. The probe
    /// below restores that, so both deployments work.
    /// <para>
    /// The identity of a candidate file is read from its manifest before anything is loaded, so an
    /// assembly signed with another key is never brought into the process at all.
    /// </para>
    /// </remarks>
    /// <param name="assemblyName">The simple name of the connector assembly.</param>
    /// <returns>The loaded assembly.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the assembly cannot be found, or when a file was found but is signed with a
    /// different key than the one expected.
    /// </exception>
    private Assembly LoadAssembly(string assemblyName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            _logger.LogDebug(
                "'{Assembly}' is not referenced by this application; looking for it as a file.",
                assemblyName);
        }

        List<string> searched = [];

        foreach (string directory in GetProbeDirectories())
        {
            string path = Path.Combine(directory, $"{assemblyName}.dll");

            searched.Add(path);

            if (!File.Exists(path))
            {
                continue;
            }

            AssemblyName candidate;

            try
            {
                candidate = AssemblyName.GetAssemblyName(path);
            }
            catch (BadImageFormatException)
            {
                _logger.LogWarning("'{Path}' is not a managed assembly.", path);
                continue;
            }

            ValidatePublicKey(candidate.GetPublicKey(), path);

            ReportPublisher(path);

            _logger.LogInformation("Loading connector '{Assembly}' from '{Path}'.", assemblyName, path);

            // LoadFrom rather than a bare Load: it resolves the assembly's own dependencies from
            // the directory it was found in, so a connector that brings its own libraries works
            // when it is deployed as a set of files.
            return Assembly.LoadFrom(path);
        }

        throw new InvalidOperationException(
            $"Failed to load assembly '{assemblyName}'. It is neither referenced by this "
            + $"application nor present as a file in: {string.Join(", ", searched)}.");
    }

    /// <summary>
    /// Returns the directories a connector deployed as a file is looked for in, nearest first.
    /// </summary>
    /// <returns>The directories.</returns>
    private static IEnumerable<string> GetProbeDirectories()
    {
        string baseDirectory = AppContext.BaseDirectory;

        // A dedicated directory keeps a deployment's connectors apart from the host's own files,
        // which matters when an operator has to see at a glance what has been added.
        string connectors = Path.Combine(baseDirectory, ConnectorDirectoryName);

        if (Directory.Exists(connectors))
        {
            yield return connectors;
        }

        yield return baseDirectory;
    }

    /// <summary>
    /// Reports who signed a connector found as a file.
    /// </summary>
    /// <remarks>
    /// This never stops a load. A connector built in-house, or one taken from a branch during an
    /// investigation, is a legitimate thing to run, and refusing it would make the loader an
    /// obstacle rather than a record. What it does is put the publisher in the log, so the question
    /// "where did this connector come from" has an answer taken from the file itself rather than
    /// from whoever remembers deploying it.
    /// <para>
    /// It is the check a strong name cannot make: .NET does not verify strong-name signatures on
    /// load, so the key establishes identity only. Authenticode establishes authorship, and that
    /// the bytes have not changed since they were signed.
    /// </para>
    /// </remarks>
    /// <param name="path">The connector file.</param>
    private void ReportPublisher(string path)
    {
        string? expected = _expectedPublisher
            ?? PublisherVerifier.GetPublisher(typeof(InstanceBuilder<T>).Assembly.Location);

        PublisherVerification verification = PublisherVerifier.Verify(path, expected);

        switch (verification.Trust)
        {
            case PublisherTrust.Trusted:
                _logger.LogInformation(
                    "Connector '{Path}' is signed by '{Publisher}'.", path, verification.Subject);
                break;

            case PublisherTrust.Untrusted:
                _logger.LogWarning(
                    "Connector '{Path}' is signed by '{Publisher}', which is not the expected "
                    + "publisher '{Expected}'. Loading it anyway.",
                    path, verification.Subject, expected);
                break;

            case PublisherTrust.Unsigned:
                _logger.LogWarning(
                    "Connector '{Path}' carries no publisher signature, so who produced it cannot "
                    + "be established. Loading it anyway.", path);
                break;

            case PublisherTrust.Invalid:
                _logger.LogWarning(
                    "Connector '{Path}' has a publisher signature that does not verify: {Detail} "
                    + "Loading it anyway.", path, verification.Detail);
                break;

            default:
                _logger.LogDebug("Publisher of '{Path}' was not checked: {Detail}", path, verification.Detail);
                break;
        }
    }

    /// <summary>
    /// Verifies that a connector found as a file carries the expected strong name.
    /// </summary>
    /// <remarks>
    /// The expected key defaults to the one this assembly is signed with, so a file dropped into a
    /// deployment has to be signed with the same key as the framework it plugs into. Configuring an
    /// expectation explicitly replaces that default.
    /// <para>
    /// This is an identity check and not a proof of authorship: .NET does not verify strong-name
    /// signatures when it loads an assembly, so a key here establishes which assembly claims to be
    /// which, not who produced it. Authorship is what Authenticode establishes, and that is
    /// verified when a release is signed rather than when it is loaded.
    /// </para>
    /// </remarks>
    /// <param name="publicKey">The public key the candidate file carries, if any.</param>
    /// <param name="path">The file, for the failure message.</param>
    /// <exception cref="InvalidOperationException">Thrown when the key does not match.</exception>
    private void ValidatePublicKey(byte[]? publicKey, string path)
    {
        byte[]? expected = _expectedPublicKey ?? typeof(InstanceBuilder<T>).Assembly.GetName().GetPublicKey();

        if (expected is null || expected.Length == 0)
        {
            return;
        }

        if (publicKey is null || publicKey.Length == 0)
        {
            throw new InvalidOperationException(
                $"'{path}' is not strong-named, so it cannot be loaded as a connector.");
        }

        if (!publicKey.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"'{path}' is signed with a different key than the one expected, so it was not loaded.");
        }
    }

    private void ValidateSignature(Assembly assembly)
    {
        if (_expectedPublicKey is null)
            return;

        byte[]? assemblyPublicKey = assembly.GetName().GetPublicKey();

        if (assemblyPublicKey is null || !assemblyPublicKey.SequenceEqual(_expectedPublicKey))
        {
            throw new InvalidOperationException(
                $"Assembly '{_assemblyName}' public key does not match the expected signature. " +
                "The assembly may have been tampered with.");
        }
    }

    private void ConfigureBuilder(T builder)
    {
        if (builder is IProviderBuilder providerBuilder)
        {
            if (_configuration is not null)
                providerBuilder.AddConfiguration(_configuration);

            providerBuilder.AddMetadata(_metadata);

            if (_deserializer is not null)
                providerBuilder.AddDeserializer(_deserializer);

            providerBuilder.AddLogger(_logger);

            if (_decryptor is not null)
                providerBuilder.AddDecryptor(_decryptor);
        }
        else if (builder is IConsumerBuilder consumerBuilder)
        {
            if (_configuration is not null)
                consumerBuilder.AddConfiguration(_configuration);

            consumerBuilder.AddMetadata(_metadata);

            if (_deserializer is not null)
                consumerBuilder.AddDeserializer(_deserializer);

            consumerBuilder.AddLogger(_logger);

            if (_decryptor is not null)
                consumerBuilder.AddDecryptor(_decryptor);
        }
    }

    #endregion
}
