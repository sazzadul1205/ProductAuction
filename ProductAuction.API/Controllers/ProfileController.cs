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

        public ProfileController(AppDbContext context)
        {
            _context = context;
        }


        // GET: api/profile
        // Admin view — lists every profile, newest first.
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllProfiles()
        {
            var profiles = await _context.UserProfiles
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new ProfileVerificationResponseDto
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    FullName = p.FullName,

                    // grab the user's role from the Identity role tables
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

            return Ok(new
            {
                status = 200,
                message = "Profiles retrieved successfully.",
                data = profiles
            });
        }


        // GET: api/profile/{id}
        // Admin view — one profile by its database id.
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetProfileById(int id)
        {
            var profile = await _context.UserProfiles
                .Where(p => p.Id == id)
                .Select(p => new ProfileVerificationResponseDto
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    FullName = p.FullName,
                    NationalIdNumber = p.NationalIdNumber,
                    NationalIdFrontImageUrl = p.NationalIdFrontImageUrl,
                    NationalIdBackImageUrl = p.NationalIdBackImageUrl,
                    VerificationStatus = p.VerificationStatus,
                    RejectionReason = p.RejectionReason,
                    CreatedAt = p.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (profile == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Profile not found."
                });
            }

            return Ok(new
            {
                status = 200,
                message = "Profile retrieved successfully.",
                data = profile
            });
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
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // find this user's profile
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
            if (string.IsNullOrEmpty(profile.NationalIdNumber) ||
                string.IsNullOrEmpty(profile.NationalIdFrontImageUrl) ||
                string.IsNullOrEmpty(profile.NationalIdBackImageUrl))
            {
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
            // only two decisions are allowed
            if (dto.Status != "Verified" && dto.Status != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Status must be Verified or Rejected."
                });
            }

            // rejection needs a reason
            if (dto.Status == "Rejected" && string.IsNullOrEmpty(dto.RejectionReason))
            {
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
                return NotFound(new
                {
                    status = 404,
                    message = "Profile not found."
                });
            }

            // grab the admin's id from the JWT so we can record who decided
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            profile.VerificationStatus = dto.Status;
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
                    verificationStatus = profile.VerificationStatus,
                    verifiedBy = profile.VerifiedBy,
                    verifiedAt = profile.VerifiedAt,
                    rejectionReason = profile.RejectionReason
                }
            });
        }
    }
}