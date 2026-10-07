using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using TfPortable.Errors;

namespace TfPortable.Auth
{
    /// <summary>A PAT stored in the Windows Credential Manager. The secret never leaves this type except through <see cref="Pat"/>.</summary>
    internal sealed class StoredPat
    {
        public StoredPat(string target, string collection, string pat, string? userName, string? storedAt)
        {
            Target = target;
            Collection = collection;
            Pat = pat;
            UserName = userName;
            StoredAt = storedAt;
        }

        public string Target { get; }

        public string Collection { get; }

        public string Pat { get; }

        public string? UserName { get; }

        public string? StoredAt { get; }
    }

    /// <summary>
    /// Windows Credential Manager access through advapi32 (CredReadW, CredWriteW, CredDeleteW, CredEnumerateW).
    /// Entries: type generic, persist local machine, target "TfPortable:&lt;normalized collection URL&gt;",
    /// user name = identity, comment = ISO date of storing, blob = PAT as UTF-16 (SPEC.md "Uložení").
    /// </summary>
    internal static class CredentialStore
    {
        private const int ErrorNotFound = 1168;

        public static StoredPat? Read(string collectionUrl)
        {
            string target = CollectionUrl.ToCredentialTarget(collectionUrl);
            IntPtr pointer = IntPtr.Zero;
            try
            {
                if (!NativeMethods.CredReadW(target, NativeMethods.CredType.Generic, 0, out pointer))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ErrorNotFound)
                    {
                        return null;
                    }

                    throw CredentialError("read", error);
                }

                return FromNative(pointer);
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    NativeMethods.CredFree(pointer);
                }
            }
        }

        public static void Write(string collectionUrl, string pat, string? userName, DateTimeOffset storedAt)
        {
            string target = CollectionUrl.ToCredentialTarget(collectionUrl);
            byte[] blob = Encoding.Unicode.GetBytes(pat);
            IntPtr blobPointer = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, blobPointer, blob.Length);
                var credential = new NativeMethods.CREDENTIAL
                {
                    Type = NativeMethods.CredType.Generic,
                    TargetName = target,
                    Comment = storedAt.ToString("O"),
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = blobPointer,
                    Persist = NativeMethods.CredPersist.LocalMachine,
                    UserName = userName ?? string.Empty,
                };

                if (!NativeMethods.CredWriteW(ref credential, 0))
                {
                    throw CredentialError("write", Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(blobPointer);
            }
        }

        /// <summary>Returns false when there was nothing to delete.</summary>
        public static bool Delete(string collectionUrl)
        {
            string target = CollectionUrl.ToCredentialTarget(collectionUrl);
            if (NativeMethods.CredDeleteW(target, NativeMethods.CredType.Generic, 0))
            {
                return true;
            }

            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return false;
            }

            throw CredentialError("delete", error);
        }

        /// <summary>All TfPortable entries (without reading them individually; the blobs are included by CredEnumerate).</summary>
        public static IReadOnlyList<StoredPat> EnumerateAll()
        {
            var result = new List<StoredPat>();
            IntPtr pointer = IntPtr.Zero;
            try
            {
                if (!NativeMethods.CredEnumerateW(CollectionUrl.CredentialPrefix + "*", 0, out uint count, out pointer))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ErrorNotFound)
                    {
                        return result;
                    }

                    throw CredentialError("enumerate", error);
                }

                for (int i = 0; i < count; i++)
                {
                    IntPtr item = Marshal.ReadIntPtr(pointer, i * IntPtr.Size);
                    var stored = FromNative(item);
                    if (stored != null)
                    {
                        result.Add(stored);
                    }
                }

                return result;
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    NativeMethods.CredFree(pointer);
                }
            }
        }

        private static StoredPat? FromNative(IntPtr pointer)
        {
            var credential = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(pointer);
            if (credential.Type != NativeMethods.CredType.Generic || string.IsNullOrEmpty(credential.TargetName)
                || !credential.TargetName.StartsWith(CollectionUrl.CredentialPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            string pat = string.Empty;
            if (credential.CredentialBlobSize > 0 && credential.CredentialBlob != IntPtr.Zero)
            {
                byte[] blob = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
                pat = Encoding.Unicode.GetString(blob);
            }

            string collection = credential.TargetName.Substring(CollectionUrl.CredentialPrefix.Length);
            return new StoredPat(credential.TargetName, collection, pat,
                string.IsNullOrEmpty(credential.UserName) ? null : credential.UserName,
                string.IsNullOrEmpty(credential.Comment) ? null : credential.Comment);
        }

        private static TfpException CredentialError(string operation, int win32Error)
        {
            string text = new Win32Exception(win32Error).Message;
            return new TfpException(ErrorCodes.CredentialManager, ExitCodes.Authentication,
                $"Cannot {operation} the Windows Credential Manager (Win32 error {win32Error}: {text}).",
                "Check that you run under your own user profile; the credentials are stored per user.");
        }

        private static class NativeMethods
        {
            public enum CredType : uint
            {
                Generic = 1,
            }

            public enum CredPersist : uint
            {
                Session = 1,
                LocalMachine = 2,
                Enterprise = 3,
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct CREDENTIAL
            {
                public uint Flags;
                public CredType Type;
                [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
                [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
                public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
                public uint CredentialBlobSize;
                public IntPtr CredentialBlob;
                public CredPersist Persist;
                public uint AttributeCount;
                public IntPtr Attributes;
                [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
                [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
            }

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CredReadW(string target, CredType type, uint flags, out IntPtr credential);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CredDeleteW(string target, CredType type, uint flags);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CredEnumerateW(string? filter, uint flags, out uint count, out IntPtr credentials);

            [DllImport("advapi32.dll")]
            public static extern void CredFree(IntPtr buffer);
        }
    }
}
