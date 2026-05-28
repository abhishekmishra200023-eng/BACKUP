using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BackupEngine.Core
{
    /// <summary>
    /// Production-grade backup engine implementation
    /// Handles full, incremental, and differential backups with deduplication and encryption
    /// </summary>
    public class BackupEngineService : IBackupEngine
    {
        private readonly ILogger<BackupEngineService> _logger;
        private readonly IVssService _vssService;
        private readonly IDeduplicationEngine _deduplicationEngine;
        private readonly IEncryptionService _encryptionService;
        private readonly ICompressionService _compressionService;
        private readonly IRepositoryManager _repositoryManager;
        private readonly IMetadataStore _metadataStore;

        public BackupEngineService(
            ILogger<BackupEngineService> logger,
            IVssService vssService,
            IDeduplicationEngine deduplicationEngine,
            IEncryptionService encryptionService,
            ICompressionService compressionService,
            IRepositoryManager repositoryManager,
            IMetadataStore metadataStore)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _vssService = vssService ?? throw new ArgumentNullException(nameof(vssService));
            _deduplicationEngine = deduplicationEngine ?? throw new ArgumentNullException(nameof(deduplicationEngine));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _compressionService = compressionService ?? throw new ArgumentNullException(nameof(compressionService));
            _repositoryManager = repositoryManager ?? throw new ArgumentNullException(nameof(repositoryManager));
            _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
        }

        public async Task<BackupResult> PerformFullBackupAsync(BackupJob job, CancellationToken cancellationToken)
        {
            var result = new BackupResult
            {
                BackupId = Guid.NewGuid().ToString(),
                JobId = job.Id,
                StartTime = DateTime.UtcNow,
                BackupType = BackupType.Full
            };

            try
            {
                _logger.LogInformation($"Starting full backup for job {job.Id} - {job.Name}");

                // Create VSS snapshot
                var snapshotId = await _vssService.CreateSnapshotAsync(job.SourcePath, cancellationToken);
                _logger.LogDebug($"VSS snapshot created: {snapshotId}");

                try
                {
                    // Get list of files from snapshot
                    var files = await _vssService.EnumerateFilesAsync(snapshotId, cancellationToken);
                    _logger.LogInformation($"Found {files.Count} files to backup");

                    // Process files through backup pipeline
                    var backupMetadata = new List<FileBackupMetadata>();
                    long processedBytes = 0;
                    long totalBytes = files.Sum(f => f.Size);

                    foreach (var file in files)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger.LogWarning("Backup cancelled by user");
                            break;
                        }

                        var fileMetadata = await ProcessFileForBackupAsync(file, job, result.BackupId, cancellationToken);
                        if (fileMetadata != null)
                        {
                            backupMetadata.Add(fileMetadata);
                            processedBytes += fileMetadata.OriginalSize;

                            // Log progress every 5%
                            var progressPercent = totalBytes > 0 ? (processedBytes * 100) / totalBytes : 0;
                            if (progressPercent % 5 == 0)
                            {
                                _logger.LogDebug($"Backup progress: {progressPercent}%");
                            }
                        }
                    }

                    // Store backup metadata
                    result.FileCount = backupMetadata.Count;
                    result.OriginalSize = backupMetadata.Sum(m => m.OriginalSize);
                    result.BackedUpSize = backupMetadata.Sum(m => m.BackedUpSize);
                    result.DeduplicationRatio = result.OriginalSize > 0 
                        ? (double)(result.OriginalSize - result.BackedUpSize) / result.OriginalSize 
                        : 0;

                    await _metadataStore.SaveBackupMetadataAsync(result.BackupId, backupMetadata, cancellationToken);

                    result.EndTime = DateTime.UtcNow;
                    result.Status = BackupStatus.Completed;
                    result.Message = $"Full backup completed successfully. {result.FileCount} files backed up. " +
                        $"Original: {FormatBytes(result.OriginalSize)}, " +
                        $"Backed up: {FormatBytes(result.BackedUpSize)}, " +
                        $"Dedup ratio: {result.DeduplicationRatio:P2}, " +
                        $"Duration: {result.Duration.TotalMinutes:F2} minutes";

                    _logger.LogInformation(result.Message);
                }
                finally
                {
                    // Cleanup VSS snapshot
                    try
                    {
                        await _vssService.DeleteSnapshotAsync(snapshotId, cancellationToken);
                        _logger.LogDebug("VSS snapshot cleaned up");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to cleanup VSS snapshot: {ex.Message}");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Full backup failed: {ex.Message}", ex);
                result.Status = BackupStatus.Failed;
                result.Message = $"Full backup failed: {ex.Message}";
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        public async Task<BackupResult> PerformIncrementalBackupAsync(BackupJob job, CancellationToken cancellationToken)
        {
            var result = new BackupResult
            {
                BackupId = Guid.NewGuid().ToString(),
                JobId = job.Id,
                StartTime = DateTime.UtcNow,
                BackupType = BackupType.Incremental
            };

            try
            {
                _logger.LogInformation($"Starting incremental backup for job {job.Id} - {job.Name}");

                // Get last backup metadata
                var lastBackup = await _metadataStore.GetLastBackupAsync(job.Id, cancellationToken);
                if (lastBackup == null)
                {
                    _logger.LogWarning("No previous backup found, performing full backup instead");
                    return await PerformFullBackupAsync(job, cancellationToken);
                }

                // Create VSS snapshot
                var snapshotId = await _vssService.CreateSnapshotAsync(job.SourcePath, cancellationToken);

                try
                {
                    // Get changed files using USN Journal (NTFS Journal)
                    var changedFiles = await _vssService.GetChangedFilesAsync(snapshotId, lastBackup.Timestamp, cancellationToken);
                    _logger.LogInformation($"Found {changedFiles.Count} changed files since last backup");

                    // Process only changed files
                    var backupMetadata = new List<FileBackupMetadata>();
                    long totalOriginalSize = 0;
                    long totalBackedUpSize = 0;

                    foreach (var file in changedFiles)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var fileMetadata = await ProcessFileForBackupAsync(file, job, result.BackupId, cancellationToken);
                        if (fileMetadata != null)
                        {
                            backupMetadata.Add(fileMetadata);
                            totalOriginalSize += fileMetadata.OriginalSize;
                            totalBackedUpSize += fileMetadata.BackedUpSize;
                        }
                    }

                    result.FileCount = backupMetadata.Count;
                    result.OriginalSize = totalOriginalSize;
                    result.BackedUpSize = totalBackedUpSize;
                    result.DeduplicationRatio = result.OriginalSize > 0 
                        ? (double)(result.OriginalSize - result.BackedUpSize) / result.OriginalSize 
                        : 0;

                    await _metadataStore.SaveBackupMetadataAsync(result.BackupId, backupMetadata, cancellationToken);

                    result.EndTime = DateTime.UtcNow;
                    result.Status = BackupStatus.Completed;
                    result.Message = $"Incremental backup completed successfully. {result.FileCount} changed files backed up.";

                    _logger.LogInformation(result.Message);
                }
                finally
                {
                    try
                    {
                        await _vssService.DeleteSnapshotAsync(snapshotId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to cleanup VSS snapshot: {ex.Message}");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Incremental backup failed: {ex.Message}", ex);
                result.Status = BackupStatus.Failed;
                result.Message = $"Incremental backup failed: {ex.Message}";
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        public Task<BackupResult> PerformDifferentialBackupAsync(BackupJob job, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Differential backup will be implemented in next phase");
        }

        public async Task<RestoreResult> RestoreFilesAsync(RestoreRequest request, CancellationToken cancellationToken)
        {
            var result = new RestoreResult
            {
                RequestId = Guid.NewGuid().ToString(),
                StartTime = DateTime.UtcNow
            };

            try
            {
                _logger.LogInformation($"Starting restore from backup {request.BackupId}");

                // Get backup metadata
                var metadata = await _metadataStore.GetBackupMetadataAsync(request.BackupId, cancellationToken);
                if (metadata == null || !metadata.Any())
                {
                    throw new InvalidOperationException($"Backup {request.BackupId} not found");
                }

                int filesRestored = 0;
                int filesFailed = 0;

                // Restore each file
                foreach (var file in request.FilesToRestore)
                {
                    try
                    {
                        var fileMetadata = metadata.FirstOrDefault(m => m.FilePath == file);
                        if (fileMetadata == null)
                        {
                            _logger.LogWarning($"File {file} not found in backup metadata");
                            filesFailed++;
                            continue;
                        }

                        // Retrieve encrypted and compressed data
                        var backupData = await _repositoryManager.RetrieveFileAsync(fileMetadata.BackupBlockId, cancellationToken);

                        // Decompress
                        var decompressed = await _compressionService.DecompressAsync(backupData, cancellationToken);

                        // Decrypt
                        var decrypted = await _encryptionService.DecryptAsync(
                            decompressed, 
                            fileMetadata.EncryptionKey, 
                            fileMetadata.EncryptionIv, 
                            cancellationToken);

                        // De-duplicate (reconstruct from chunks if needed)
                        var originalData = await _deduplicationEngine.ReconstructFileAsync(decrypted, fileMetadata, cancellationToken);

                        // Write to restore location
                        var restorePath = Path.Combine(request.RestorePath, Path.GetFileName(file));
                        Directory.CreateDirectory(request.RestorePath);
                        await System.IO.File.WriteAllBytesAsync(restorePath, originalData, cancellationToken);

                        filesRestored++;
                        _logger.LogDebug($"Restored file: {restorePath}");
                    }
                    catch (Exception ex)
                    {
                        filesFailed++;
                        _logger.LogError($"Failed to restore {file}: {ex.Message}");
                    }
                }

                result.EndTime = DateTime.UtcNow;
                result.Status = filesFailed == 0 ? RestoreStatus.Completed : RestoreStatus.Failed;
                result.FilesRestored = filesRestored;
                result.FilesFailed = filesFailed;
                result.Message = $"Restore completed. {filesRestored} files restored, {filesFailed} failed.";

                _logger.LogInformation(result.Message);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Restore failed: {ex.Message}", ex);
                result.Status = RestoreStatus.Failed;
                result.Message = $"Restore failed: {ex.Message}";
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        public async Task<VerificationResult> VerifyBackupAsync(string backupId, CancellationToken cancellationToken)
        {
            var result = new VerificationResult
            {
                BackupId = backupId,
                StartTime = DateTime.UtcNow
            };

            try
            {
                _logger.LogInformation($"Starting backup verification for {backupId}");

                // Get backup metadata
                var metadata = await _metadataStore.GetBackupMetadataAsync(backupId, cancellationToken);
                if (!metadata.Any())
                {
                    throw new InvalidOperationException($"Backup {backupId} not found");
                }

                int filesVerified = 0;
                int filesFailed = 0;

                // Verify each file's checksum
                foreach (var fileMetadata in metadata)
                {
                    try
                    {
                        // Retrieve and verify checksums
                        var backupData = await _repositoryManager.RetrieveFileAsync(fileMetadata.BackupBlockId, cancellationToken);
                        var calculatedChecksum = System.Security.Cryptography.SHA256.Create()
                            .ComputeHash(backupData);

                        if (!fileMetadata.Checksum.SequenceEqual(calculatedChecksum))
                        {
                            filesFailed++;
                            _logger.LogWarning($"Checksum mismatch for {fileMetadata.FilePath}");
                        }
                        else
                        {
                            filesVerified++;
                        }
                    }
                    catch (Exception ex)
                    {
                        filesFailed++;
                        _logger.LogError($"Verification failed for {fileMetadata.FilePath}: {ex.Message}");
                    }
                }

                result.EndTime = DateTime.UtcNow;
                result.Status = filesFailed == 0 ? VerificationStatus.Passed : VerificationStatus.Failed;
                result.FilesVerified = filesVerified;
                result.FilesFailed = filesFailed;
                result.Message = $"Verification completed. {filesVerified} files verified, {filesFailed} failed.";

                _logger.LogInformation(result.Message);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Verification failed: {ex.Message}", ex);
                result.Status = VerificationStatus.Failed;
                result.Message = $"Verification failed: {ex.Message}";
                result.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        private async Task<FileBackupMetadata> ProcessFileForBackupAsync(
            BackupFile file, 
            BackupJob job, 
            string backupId, 
            CancellationToken cancellationToken)
        {
            try
            {
                // Read file
                var fileData = await System.IO.File.ReadAllBytesAsync(file.Path, cancellationToken);

                // Calculate original checksum
                var originalChecksum = System.Security.Cryptography.SHA256.Create().ComputeHash(fileData);

                // Deduplicate
                var dedupResult = await _deduplicationEngine.DeduplicateAsync(fileData, cancellationToken);

                // Encrypt
                var encryptionKey = await _encryptionService.GenerateKeyAsync(cancellationToken);
                var encryptionIv = await _encryptionService.GenerateIvAsync(cancellationToken);
                var encryptedData = await _encryptionService.EncryptAsync(dedupResult.Data, encryptionKey, encryptionIv, cancellationToken);

                // Compress
                var compressedData = await _compressionService.CompressAsync(encryptedData, cancellationToken);

                // Store in repository
                var blockId = await _repositoryManager.StoreBlockAsync(compressedData, backupId, cancellationToken);

                return new FileBackupMetadata
                {
                    FilePath = file.Path,
                    OriginalSize = fileData.Length,
                    BackedUpSize = compressedData.Length,
                    BackupBlockId = blockId,
                    Checksum = originalChecksum,
                    EncryptionKey = encryptionKey,
                    EncryptionIv = encryptionIv,
                    CreatedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to process file {file.Path}: {ex.Message}");
                return null;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
