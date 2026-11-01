using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Profile;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ProfileController> _logger;

        public ProfileController(AppDbContext context, ILogger<ProfileController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // PUT: api/profile/submit-verification
        // Logged-in user submits their profile for admin review.
        [HttpPut("submit-verification")]
        public async Task<IActionResult> SubmitVerification()
        {
            // grab the user id from the JWT
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                // _logger.LogWarning("SubmitVerification: user identity missing from token.");

                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // _logger.LogInformation("SubmitVerification called by user {UserId}.", userId);

            // find this user's profile
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

            if (profile == null)
            {
                // _logger.LogWarning("SubmitVerification: profile for user {UserId} not found.", userId);

                return NotFound(new
                {
                    status = 404,
                    message = "Profile not found."
                });
            }

            // National ID number and both sides of the ID are required
            if (string.IsNullOrEmpty(profile.NationalIdNumber) ||
                string.IsNullOrEmpty(profile.NationalIdFrontImageUrl) ||
                string.IsNullOrEmpty(profile.NationalIdBackImageUrl))
            {
                // _logger.LogWarning("SubmitVerification: user {UserId} missing NID fields (number={HasNumber}, front={HasFront}, back={HasBack}).",
                //     userId,
                //     !string.IsNullOrEmpty(profile.NationalIdNumber),
                //     !string.IsNullOrEmpty(profile.NationalIdFrontImageUrl),
                //     !string.IsNullOrEmpty(profile.NationalIdBackImageUrl));

                return BadRequest(new
                {
                    status = 400,
                    message = "Please provide your National ID number and both ID image URLs before submitting."
                });
            }

            // move it into the review queue and clear any leftover review state
            profile.VerificationStatus = "PendingReview";
            profile.VerifiedBy = null;
            profile.VerifiedAt = null;
            profile.RejectionReason = null;
            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();


            // System.Diagnostics.Debug.Assert(profile.VerificationStatus == "PendingReview");
            // System.Diagnostics.Debug.Assert(profile.VerifiedBy == null && profile.VerifiedAt == null);

            // _logger.LogInformation("SubmitVerification: profile #{ProfileId} for user {UserId} submitted for review.", profile.Id, userId);

            return Ok(new
            {
                status = 200,
                message = "Verification submitted successfully. Await admin review.",
                data = new
                {
                    id = profile.Id,
                    verificationStatus = profile.VerificationStatus,
                    submittedAt = profile.UpdatedAt
                }
            });
        }


        // GET: api/profile/pending-verification
        // Admin view — profiles waiting for review.
        [HttpGet("pending-verification")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetPendingVerificationProfiles()
        {
            // _logger.LogInformation("GetPendingVerificationProfiles called.");

            var profiles = await _context.UserProfiles
                .Where(p => p.VerificationStatus == "PendingReview")
                .Select(p => new ProfileVerificationResponseDto
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    FullName = p.FullName,
                    City = p.City,
                    Address = p.Address,

                    // phone number comes from the Identity users table
                    PhoneNumber = _context.Users
                        .Where(u => u.Id == p.UserId)
                        .Select(u => u.PhoneNumber)
                        .FirstOrDefault(),

                    // user's role (Buyer or Seller) from the role tables
                    Role = _context.UserRoles
                        .Where(ur => ur.UserId == p.UserId)
                        .Join(_context.Roles,
                            ur => ur.RoleId,
                            r => r.Id,
                            (ur, r) => r.Name)
                        .FirstOrDefault(),

                    NationalIdNumber = p.NationalIdNumber,
                    NationalIdFrontImageUrl = p.NationalIdFrontImageUrl,
                    NationalIdBackImageUrl = p.NationalIdBackImageUrl,
                    VerificationStatus = p.VerificationStatus,
                    RejectionReason = p.RejectionReason,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();


            // System.Diagnostics.Debug.Assert(profiles.All(p => p.VerificationStatus == "PendingReview"));

            // _logger.LogInformation("GetPendingVerificationProfiles returned {Count} profile(s).", profiles.Count);

            return Ok(new
            {
                status = 200,
                message = "Pending verification profiles retrieved successfully.",
                data = profiles
            });
        }


        // PUT: api/profile/{id}/verify
        // Admin marks a profile as Verified or Rejected.
        [HttpPut("{id}/verify")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> VerifyProfile(int id, VerifyProfileDto dto)
        {
            // _logger.LogInformation("VerifyProfile called for profile #{ProfileId} with decision '{Status}'.", id, dto.Status);

            // only two decisions are allowed
            if (dto.Status != "Verified" && dto.Status != "Rejected")
            {
                // _logger.LogWarning("VerifyProfile: invalid decision '{Status}' for profile #{ProfileId}.", dto.Status, id);

                return BadRequest(new
                {
                    status = 400,
                    message = "Status must be Verified or Rejected."
                });
            }

            // rejection needs a reason
            if (dto.Status == "Rejected" && string.IsNullOrEmpty(dto.RejectionReason))
            {
                // _logger.LogWarning("VerifyProfile: rejection reason missing for profile #{ProfileId}.", id);

                return BadRequest(new
                {
                    status = 400,
                    message = "A rejection reason is required."
                });
            }

            // find the profile
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.Id == id);

            if (profile == null)
            {
                // _logger.LogWarning("VerifyProfile: profile #{ProfileId} not found.", id);

                return NotFound(new
                {
                    status = 404,
                    message = "Profile not found."
                });
            }

            // grab the admin's id from the JWT so we can record who decided
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // _logger.LogDebug("VerifyProfile: profile #{ProfileId} reviewed by admin {AdminId}.", id, adminId);

            profile.VerificationStatus = dto.Status;
            profile.VerifiedBy = adminId;
            profile.VerifiedAt = DateTime.UtcNow;

            // only keep the rejection reason when the decision is Rejected
            profile.RejectionReason = dto.Status == "Rejected" ? dto.RejectionReason : null;
            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();


            // System.Diagnostics.Debug.Assert(profile.VerificationStatus == dto.Status);
            // System.Diagnostics.Debug.Assert(profile.VerifiedBy == adminId);
            // System.Diagnostics.Debug.Assert(dto.Status == "Rejected" || profile.RejectionReason == null);

            // _logger.LogInformation("VerifyProfile: profile #{ProfileId} marked '{Status}' by admin {AdminId}.",
            //     id, dto.Status, adminId);

            return Ok(new
            {
                status = 200,
                message = "Profile verification decision saved successfully.",
                data = new
                {
                    id = profile.Id,
                    userId = profile.UserId,
                    verificationStatus = profile.VerificationStatus,
                    verifiedBy = profile.VerifiedBy,
                    verifiedAt = profile.VerifiedAt,
                    rejectionReason = profile.RejectionReason
                }
            });
        }
    }
}