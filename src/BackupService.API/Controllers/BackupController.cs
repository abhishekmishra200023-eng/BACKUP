using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BackupEngine.Core;
using Microsoft.Extensions.Logging;

namespace BackupService.API.Controllers
{
    /// <summary>
    /// Backup Jobs API Controller
    /// Handles CRUD operations and backup job management
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class BackupJobsController : ControllerBase
    {
        private readonly IBackupEngine _backupEngine;
        private readonly IBackupJobService _jobService;
        private readonly ILogger<BackupJobsController> _logger;

        public BackupJobsController(
            IBackupEngine backupEngine,
            IBackupJobService jobService,
            ILogger<BackupJobsController> logger)
        {
            _backupEngine = backupEngine ?? throw new ArgumentNullException(nameof(backupEngine));
            _jobService = jobService ?? throw new ArgumentNullException(nameof(jobService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Get all backup jobs
        /// </summary>
        /// <response code="200">Returns list of all backup jobs</response>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<List<BackupJob>>> GetAllJobs()
        {
            try
            {
                _logger.LogInformation("Fetching all backup jobs");
                var jobs = await _jobService.GetAllJobsAsync();
                return Ok(jobs);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting jobs: {ex.Message}", ex);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        /// <summary>
        /// Get specific backup job by ID
        /// </summary>
        /// <param name="jobId">The backup job ID</param>
        /// <response code="200">Returns the backup job</response>
        /// <response code="404">Job not found</response>
        [HttpGet("{jobId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BackupJob>> GetJob(string jobId)
        {
            try
            {
                _logger.LogInformation($"Fetching job: {jobId}");
                var job = await _jobService.GetJobAsync(jobId);
                if (job == null)
                    return NotFound(new { error = $"Job {jobId} not found" });

                return Ok(job);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting job: {ex.Message}", ex);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Create new backup job
        /// </summary>
        /// <param name="request">Backup job creation request</param>
        /// <response code="201">Job created successfully</response>
        /// <response code="400">Invalid request</response>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<BackupJob>> CreateJob([FromBody] CreateBackupJobRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                _logger.LogInformation($"Creating new backup job: {request.Name}");
                var job = await _jobService.CreateJobAsync(request);
                return CreatedAtAction(nameof(GetJob), new { jobId = job.Id }, job);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating job: {ex.Message}", ex);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Start backup job immediately
        /// </summary>
        /// <param name="jobId">The backup job ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <response code="200">Backup started successfully</response>
        /// <response code="404">Job not found</response>
        [HttpPost("{jobId}/start")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BackupResult>> StartBackup(string jobId, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"Starting backup for job: {jobId}");
                var job = await _jobService.GetJobAsync(jobId);
                if (job == null)
                    return NotFound(new { error = $"Job {jobId} not found" });

                var result = await _backupEngine.PerformFullBackupAsync(job, cancellationToken);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error starting backup: {ex.Message}", ex);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Update backup job
        /// </summary>
        /// <param name="jobId">The backup job ID</param>
        /// <param name="request">Update request</param>
        /// <response code="200">Job updated successfully</response>
        /// <response code="404">Job not found</response>
        [HttpPut("{jobId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BackupJob>> UpdateJob(string jobId, [FromBody] UpdateBackupJobRequest request)
        {
            try
            {
                _logger.LogInformation($"Updating job: {jobId}");
                var job = await _jobService.UpdateJobAsync(jobId, request);
                if (job == null)
                    return NotFound(new { error = $"Job {jobId} not found" });

                return Ok(job);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating job: {ex.Message}", ex);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Delete backup job
        /// </summary>
        /// <param name="jobId">The backup job ID</param>
        /// <response code="204">Job deleted successfully</response>
        /// <response code="404">Job not found</response>
        [HttpDelete("{jobId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteJob(string jobId)
        {
            try
            {
                _logger.LogInformation($"Deleting job: {jobId}");
                var success = await _jobService.DeleteJobAsync(jobId);
                if (!success)
                    return NotFound(new { error = $"Job {jobId} not found" });

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting job: {ex.Message}", ex);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }

    /// <summary>
    /// Restore API Controller
    /// Handles restore operations
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class RestoreController : ControllerBase
    {
        private readonly IBackupEngine _backupEngine;
        private readonly ILogger<RestoreController> _logger;

        public RestoreController(
            IBackupEngine backupEngine,
            ILogger<RestoreController> logger)
        {
            _backupEngine = backupEngine ?? throw new ArgumentNullException(nameof(backupEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Restore files from backup
        /// </summary>
        /// <param name="request">Restore request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <response code="200">Restore completed</response>
        /// <response code="400">Invalid request</response>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<RestoreResult>> RestoreFiles(
            [FromBody] RestoreRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                _logger.LogInformation($"Starting restore from backup: {request.BackupId}");
                var result = await _backupEngine.RestoreFilesAsync(request, cancellationToken);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error restoring files: {ex.Message}", ex);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }

    /// <summary>
    /// Monitoring and Verification API Controller
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class VerificationController : ControllerBase
    {
        private readonly IBackupEngine _backupEngine;
        private readonly ILogger<VerificationController> _logger;

        public VerificationController(
            IBackupEngine backupEngine,
            ILogger<VerificationController> logger)
        {
            _backupEngine = backupEngine ?? throw new ArgumentNullException(nameof(backupEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Verify backup integrity
        /// </summary>
        /// <param name="backupId">The backup ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <response code="200">Verification completed</response>
        [HttpPost("{backupId}/verify")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<VerificationResult>> VerifyBackup(
            string backupId,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"Starting verification for backup: {backupId}");
                var result = await _backupEngine.VerifyBackupAsync(backupId, cancellationToken);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error verifying backup: {ex.Message}", ex);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }

    /// <summary>
    /// Backup Job Service Interface
    /// </summary>
    public interface IBackupJobService
    {
        Task<List<BackupJob>> GetAllJobsAsync();
        Task<BackupJob> GetJobAsync(string jobId);
        Task<BackupJob> CreateJobAsync(CreateBackupJobRequest request);
        Task<BackupJob> UpdateJobAsync(string jobId, UpdateBackupJobRequest request);
        Task<bool> DeleteJobAsync(string jobId);
    }

    /// <summary>
    /// Create Backup Job Request DTO
    /// </summary>
    public class CreateBackupJobRequest
    {
        public string Name { get; set; }
        public string SourcePath { get; set; }
        public string DestinationRepository { get; set; }
        public BackupType BackupType { get; set; }
        public string Schedule { get; set; }
        public int RetentionDays { get; set; }
        public bool EncryptionEnabled { get; set; }
        public bool CompressionEnabled { get; set; }
        public bool DeduplicationEnabled { get; set; }
    }

    /// <summary>
    /// Update Backup Job Request DTO
    /// </summary>
    public class UpdateBackupJobRequest
    {
        public string Name { get; set; }
        public string Schedule { get; set; }
        public int RetentionDays { get; set; }
        public bool IsEnabled { get; set; }
    }
}
