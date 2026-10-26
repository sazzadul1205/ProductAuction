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
        // Get all the Profiles
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllProfiles()
        {
            // Retrieve all profiles, newest first.
            var profiles = await _context.UserProfiles
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new ProfileVerificationResponseDto
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    FullName = p.FullName,
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


        // GET: api/profile
        // Get the Profile by Its Id
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetProfileById(int id)
        {
            // Find the profile using its database ID.
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

            // Return 404 if the profile does not exist.
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
        // Update the Profile so the Product is now Goint To Varification 
        [HttpPut("submit-verification")]
        public async Task<IActionResult> SubmitVerification()
        {
            // Read the user's ID from the validated JWT token.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            // Find the profile belonging to this user.
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

            if (profile == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Profile not found."
                });
            }

            // Require all National ID details before submission.
            if (string.IsNullOrEmpty(profile.NationalIdNumber) || string.IsNullOrEmpty(profile.NationalIdFrontImageUrl) || string.IsNullOrEmpty(profile.NationalIdBackImageUrl))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Please provide your National ID number and both ID image URLs before submitting."
                });
            }

            // Submit the profile for admin review.
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
        // Get all the Pofiles that are Pending Varification
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

                    // Fetch the phone number from the AspNetUsers (Identity) table
                    PhoneNumber = _context.Users
                        .Where(u => u.Id == p.UserId)
                        .Select(u => u.PhoneNumber)
                        .FirstOrDefault(),

                    // Fetch the user's assigned role (Buyer / Seller)
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

        // GET api/profile/5/verify
        // Update the Profile With Either Verified or not 
        [HttpPut("{id}/verify")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> VerifyProfile(int id, VerifyProfileDto dto)
        {
            // Accept only the two supported decisions.
            if (dto.Status != "Verified" && dto.Status != "Rejected")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Status must be Verified or Rejected."
                });
            }

            // If Rejected it needs a Rejection Reason
            if (dto.Status == "Rejected" && string.IsNullOrEmpty(dto.RejectionReason))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "A rejection reason is required."
                });
            }

            // Check if the Profile Exsisits 
            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.Id == id);

            if (profile == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Profile not found."
                });
            }

            // Read the user's ID from the validated JWT token.
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            profile.VerificationStatus = dto.Status;
            profile.VerifiedBy = adminId;
            profile.VerifiedAt = DateTime.UtcNow;
            // Save the rejection reason only if the status is "Rejected"; otherwise clear it (null).
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
