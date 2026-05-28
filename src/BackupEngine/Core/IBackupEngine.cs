using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BackupEngine.Core
{
    /// <summary>
    /// Core backup engine interface defining backup operations
    /// </summary>
    public interface IBackupEngine
    {
        /// <summary>
        /// Perform a full backup of specified source
        /// </summary>
        Task<BackupResult> PerformFullBackupAsync(BackupJob job, CancellationToken cancellationToken);

        /// <summary>
        /// Perform an incremental backup
        /// </summary>
        Task<BackupResult> PerformIncrementalBackupAsync(BackupJob job, CancellationToken cancellationToken);

        /// <summary>
        /// Perform a differential backup
        /// </summary>
        Task<BackupResult> PerformDifferentialBackupAsync(BackupJob job, CancellationToken cancellationToken);

        /// <summary>
        /// Restore files from backup
        /// </summary>
        Task<RestoreResult> RestoreFilesAsync(RestoreRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Verify backup integrity
        /// </summary>
        Task<VerificationResult> VerifyBackupAsync(string backupId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Enums for backup operations
    /// </summary>
    public enum BackupType { Full, Incremental, Differential, Synthetic }
    public enum BackupStatus { Pending, Running, Completed, Failed, Cancelled }
    public enum RestoreStatus { Pending, Running, Completed, Failed }
    public enum VerificationStatus { Pending, Running, Passed, Failed }

    /// <summary>
    /// Backup job configuration
    /// </summary>
    public class BackupJob
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string SourcePath { get; set; }
        public string DestinationRepository { get; set; }
        public BackupType BackupType { get; set; }
        public string Schedule { get; set; } // Cron expression
        public int RetentionDays { get; set; }
        public bool EncryptionEnabled { get; set; }
        public bool CompressionEnabled { get; set; }
        public bool DeduplicationEnabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastRunTime { get; set; }
        public DateTime? NextRunTime { get; set; }
        public bool IsEnabled { get; set; }
    }

    /// <summary>
    /// Result of a backup operation
    /// </summary>
    public class BackupResult
    {
        public string BackupId { get; set; }
        public string JobId { get; set; }
        public BackupType BackupType { get; set; }
        public BackupStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int FileCount { get; set; }
        public long OriginalSize { get; set; }
        public long BackedUpSize { get; set; }
        public double DeduplicationRatio { get; set; }
        public string Message { get; set; }
        
        public TimeSpan Duration => EndTime - StartTime;
        public double CompressionRatio => OriginalSize > 0 ? (double)BackedUpSize / OriginalSize : 0;
        public double ThroughputMBps => Duration.TotalSeconds > 0 ? OriginalSize / (1024 * 1024) / Duration.TotalSeconds : 0;
    }

    /// <summary>
    /// Restore request
    /// </summary>
    public class RestoreRequest
    {
        public string BackupId { get; set; }
        public List<string> FilesToRestore { get; set; } = new();
        public string RestorePath { get; set; }
        public bool OverwriteExisting { get; set; }
    }

    /// <summary>
    /// Result of a restore operation
    /// </summary>
    public class RestoreResult
    {
        public string RequestId { get; set; }
        public RestoreStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int FilesRestored { get; set; }
        public int FilesFailed { get; set; }
        public string Message { get; set; }
        
        public TimeSpan Duration => EndTime - StartTime;
    }

    /// <summary>
    /// Result of backup verification
    /// </summary>
    public class VerificationResult
    {
        public string BackupId { get; set; }
        public VerificationStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int FilesVerified { get; set; }
        public int FilesFailed { get; set; }
        public string Message { get; set; }
        
        public TimeSpan Duration => EndTime - StartTime;
    }

    /// <summary>
    /// File backup metadata
    /// </summary>
    public class FileBackupMetadata
    {
        public string FilePath { get; set; }
        public long OriginalSize { get; set; }
        public long BackedUpSize { get; set; }
        public string BackupBlockId { get; set; }
        public byte[] Checksum { get; set; }
        public byte[] EncryptionKey { get; set; }
        public byte[] EncryptionIv { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
    }

    /// <summary>
    /// File information for backup
    /// </summary>
    public class BackupFile
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public bool IsDirectory { get; set; }
        public string[] Attributes { get; set; }
    }
}
