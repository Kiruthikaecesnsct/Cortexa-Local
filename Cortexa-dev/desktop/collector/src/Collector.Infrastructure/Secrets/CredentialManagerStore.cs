using System.Runtime.InteropServices;
using System.Text;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Secrets;

public sealed class CredentialManagerStore(IOptions<SecretsOptions> options) : ISecretStore
{
    public const int MaxBlobBytes = 2560;

    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly string _prefix = options.Value.TargetPrefix;

    public Task<string?> ReadAsync(SecretSlot slot, CancellationToken cancellationToken) =>
        Task.Run(() => Read(TargetFor(slot)), cancellationToken);

    public Task WriteAsync(SecretSlot slot, string value, CancellationToken cancellationToken)
    {
        var blob = Utf8.GetBytes(value);
        if (blob.Length > MaxBlobBytes)
        {
            throw new SecretStoreException($"Secret for slot {slot} exceeds the {MaxBlobBytes} byte limit.");
        }

        return Task.Run(() => Write(TargetFor(slot), blob), cancellationToken);
    }

    public Task DeleteAsync(SecretSlot slot, CancellationToken cancellationToken) =>
        Task.Run(() => Delete(TargetFor(slot)), cancellationToken);

    private string TargetFor(SecretSlot slot) => $"{_prefix}/{slot}";

    private static string? Read(string target)
    {
        if (!NativeCredentialMethods.CredRead(target, NativeCredentialMethods.CredTypeGeneric, 0, out var pointer))
        {
            return NotFoundOrThrow("read");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredentialMethods.Credential>(pointer);
            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Utf8.GetString(blob);
        }
        finally
        {
            NativeCredentialMethods.CredFree(pointer);
        }
    }

    private static void Write(string target, byte[] blob)
    {
        var targetPointer = Marshal.StringToHGlobalUni(target);
        var blobPointer = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new NativeCredentialMethods.Credential
            {
                Type = NativeCredentialMethods.CredTypeGeneric,
                TargetName = targetPointer,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = NativeCredentialMethods.CredPersistLocalMachine,
            };

            if (!NativeCredentialMethods.CredWrite(in credential, 0))
            {
                throw FailureFor("write");
            }
        }
        finally
        {
            Marshal.Copy(new byte[blob.Length], 0, blobPointer, blob.Length);
            Marshal.FreeHGlobal(blobPointer);
            Marshal.FreeHGlobal(targetPointer);
        }
    }

    private static void Delete(string target)
    {
        if (!NativeCredentialMethods.CredDelete(target, NativeCredentialMethods.CredTypeGeneric, 0))
        {
            _ = NotFoundOrThrow("delete");
        }
    }

    private static string? NotFoundOrThrow(string operation) =>
        Marshal.GetLastPInvokeError() == NativeCredentialMethods.ErrorNotFound ? null : throw FailureFor(operation);

    private static SecretStoreException FailureFor(string operation) =>
        new($"Credential Manager {operation} failed (error {Marshal.GetLastPInvokeError()}).");
}
