using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BackupEngine.Core
{
    /// <summary>
    /// VSS Service Interface for Windows Volume Shadow Copy Service
    /// </summary>
    public interface IVssService
    {
        Task<string> CreateSnapshotAsync(string sourcePath, CancellationToken cancellationToken);
        Task DeleteSnapshotAsync(string snapshotId, CancellationToken cancellationToken);
        Task<List<BackupFile>> EnumerateFilesAsync(string snapshotId, CancellationToken cancellationToken);
        Task<List<BackupFile>> GetChangedFilesAsync(string snapshotId, DateTime since, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Deduplication Service Interface
    /// </summary>
    public interface IDeduplicationEngine
    {
        Task<DeduplicationResult> DeduplicateAsync(byte[] data, CancellationToken cancellationToken);
        Task<byte[]> ReconstructFileAsync(byte[] dedupData, FileBackupMetadata metadata, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Encryption Service Interface
    /// </summary>
    public interface IEncryptionService
    {
        Task<byte[]> GenerateKeyAsync(CancellationToken cancellationToken);
        Task<byte[]> GenerateIvAsync(CancellationToken cancellationToken);
        Task<byte[]> EncryptAsync(byte[] data, byte[] key, byte[] iv, CancellationToken cancellationToken);
        Task<byte[]> DecryptAsync(byte[] data, byte[] key, byte[] iv, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Compression Service Interface
    /// </summary>
    public interface ICompressionService
    {
        Task<byte[]> CompressAsync(byte[] data, CancellationToken cancellationToken);
        Task<byte[]> DecompressAsync(byte[] data, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Repository Manager Interface
    /// </summary>
    public interface IRepositoryManager
    {
        Task<string> StoreBlockAsync(byte[] data, string backupId, CancellationToken cancellationToken);
        Task<byte[]> RetrieveFileAsync(string blockId, CancellationToken cancellationToken);
        Task<RepositoryInfo> GetRepositoryInfoAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Metadata Store Interface
    /// </summary>
    public interface IMetadataStore
    {
        Task SaveBackupMetadataAsync(string backupId, List<FileBackupMetadata> metadata, CancellationToken cancellationToken);
        Task<List<FileBackupMetadata>> GetBackupMetadataAsync(string backupId, CancellationToken cancellationToken);
        Task<BackupResult> GetLastBackupAsync(string jobId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Deduplication Result
    /// </summary>
    public class DeduplicationResult
    {
        public byte[] Data { get; set; }
        public int OriginalChunks { get; set; }
        public int DeduplicatedChunks { get; set; }
    }

    /// <summary>
    /// Repository Information
    /// </summary>
    public class RepositoryInfo
    {
        public string Name { get; set; }
        public long TotalCapacity { get; set; }
        public long UsedCapacity { get; set; }
        public long AvailableCapacity { get; set; }
    }
}
