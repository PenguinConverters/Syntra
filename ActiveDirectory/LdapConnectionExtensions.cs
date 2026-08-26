using System.DirectoryServices.Protocols;

namespace PenguinConverters.Syntra.ActiveDirectory;

/// <summary>
/// Provides Task-based (TAP) wrappers over the APM methods exposed by <see cref="LdapConnection"/>.
/// <c>System.DirectoryServices.Protocols</c> ships only the <c>BeginSendRequest</c> /
/// <c>EndSendRequest</c> pair, so these adapters bridge it to <see langword="await"/> and map
/// <see cref="CancellationToken"/> onto <see cref="LdapConnection.Abort"/>.
/// </summary>
public static class LdapConnectionExtensions
{
    #region Methods

    /// <summary>
    /// Sends an LDAP request asynchronously and returns the directory response.
    /// </summary>
    /// <param name="connection">The LDAP connection to send the request on.</param>
    /// <param name="request">The directory request to send.</param>
    /// <param name="cancellationToken">
    /// A token to signal cancellation. When signalled, the in-flight request is aborted via
    /// <see cref="LdapConnection.Abort"/> and the returned task transitions to canceled.
    /// </param>
    /// <returns>The directory response returned by the server.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="connection"/> or <paramref name="request"/> is <c>null</c>.
    /// </exception>
    public static async Task<DirectoryResponse> SendRequestAsync(
        this LdapConnection connection,
        DirectoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<DirectoryResponse> completion =
            new TaskCompletionSource<DirectoryResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        IAsyncResult asyncResult = connection.BeginSendRequest(
            request,
            PartialResultProcessing.NoPartialResultSupport,
            result =>
            {
                try
                {
                    completion.TrySetResult(connection.EndSendRequest(result));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            },
            null);

        // Abort is best effort: the request may already have completed by the time the token
        // fires, in which case the callback above has already settled the task.
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state =>
            {
                PendingRequest pending = (PendingRequest)state!;

                try
                {
                    pending.Connection.Abort(pending.AsyncResult);
                }
                catch (Exception)
                {
                    // Ignored: aborting an already-completed request is not an error here.
                }

                pending.Completion.TrySetCanceled(pending.CancellationToken);
            },
            new PendingRequest(connection, asyncResult, completion, cancellationToken));

        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Sends an LDAP request asynchronously and returns the response cast to
    /// <typeparamref name="TResponse"/>.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="connection">The LDAP connection to send the request on.</param>
    /// <param name="request">The directory request to send.</param>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    /// <returns>The directory response, cast to <typeparamref name="TResponse"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the server returns a response that is not a <typeparamref name="TResponse"/>.
    /// </exception>
    public static async Task<TResponse> SendRequestAsync<TResponse>(
        this LdapConnection connection,
        DirectoryRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : DirectoryResponse
    {
        DirectoryResponse response = await connection
            .SendRequestAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return response as TResponse
            ?? throw new InvalidOperationException(
                $"Expected an LDAP response of type '{typeof(TResponse).Name}' but received '{response?.GetType().Name ?? "null"}'.");
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Carries the state needed to abort a pending request when its cancellation token fires.
    /// </summary>
    private sealed record PendingRequest(
        LdapConnection Connection,
        IAsyncResult AsyncResult,
        TaskCompletionSource<DirectoryResponse> Completion,
        CancellationToken CancellationToken);

    #endregion
}
