using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Profile;
using ProductAuction.API.Models.Enums;
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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Profile not found."
                });
            }

            // National ID number and both sides of the ID are required
            if (string.IsNullOrEmpty(profile.NationalIdNumber) || string.IsNullOrEmpty(profile.NationalIdFrontImageUrl) || string.IsNullOrEmpty(profile.NationalIdBackImageUrl))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Please provide your National ID number and both ID image URLs before submitting."
                });
            }

            // move it into the review
            profile.VerificationStatus = VerificationStatus.PendingReview;
            profile.VerifiedBy = null;
            profile.VerifiedAt = null;
            profile.RejectionReason = null;
            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Verification submitted successfully. Await admin review.",
                data = new
                {
                    id = profile.Id,
                    verificationStatus = profile.VerificationStatus.ToString(),
                    submittedAt = profile.UpdatedAt
                }
            });
        }


        // GET: api/profile/pending-verification
        // Admin view � profiles waiting for review.
        [HttpGet("pending-verification")]
        [Authorize(Roles = "Admin,Super Admin")]
        public async Task<IActionResult> GetPendingVerificationProfiles()
        {
            var profiles = await _context.UserProfiles
                .Where(p => p.VerificationStatus == VerificationStatus.PendingReview)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new ProfileVerificationResponseDto
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    FullName = p.FullName,
                    City = p.City,
                    Address = p.Address,
                    PhoneNumber = _context.Users
                        .Where(u => u.Id == p.UserId)
                        .Select(u => u.PhoneNumber)
                        .FirstOrDefault(),

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
                    VerificationStatus = p.VerificationStatus.ToString(),
                    RejectionReason = p.RejectionReason,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();

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
        [Authorize(Roles = "Admin,Super Admin")]
        public async Task<IActionResult> VerifyProfile(int id, VerifyProfileDto dto)
        {
            if (dto.Status != "Verified" && dto.Status != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Status must be Verified or Rejected."
                });
            }

            if (dto.Status == "Rejected" && string.IsNullOrEmpty(dto.RejectionReason))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A rejection reason is required."
                });
            }

            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.Id == id);
            if (profile == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Profile not found."
                });
            }

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // map the DTO string to the enum
            profile.VerificationStatus = dto.Status == "Verified"
                ? VerificationStatus.Verified
                : VerificationStatus.Rejected;

            profile.VerifiedBy = adminId;
            profile.VerifiedAt = DateTime.UtcNow;

            // only keep the rejection reason when the decision is Rejected
            profile.RejectionReason = dto.Status == "Rejected" ? dto.RejectionReason : null;

            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Profile verification decision saved successfully.",
                data = new
                {
                    id = profile.Id,
                    userId = profile.UserId,
                    verificationStatus = profile.VerificationStatus.ToString(),
                    verifiedBy = profile.VerifiedBy,
                    verifiedAt = profile.VerifiedAt,
                    rejectionReason = profile.RejectionReason
                }
            });
        }
    }
}