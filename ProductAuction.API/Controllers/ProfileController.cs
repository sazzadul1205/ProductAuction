using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Profile;
using ProductAuction.API.Models;
using ProductAuction.API.Models.Enums;
using ProductAuction.API.Services;
using System.Security.Claims;

namespace ProductAuction.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly ILogger<ProfileController> _logger;

        public ProfileController(
            AppDbContext context,
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IJwtTokenService jwtTokenService,
            ILogger<ProfileController> logger)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _jwtTokenService = jwtTokenService;
            _logger = logger;
        }

        // POST: api/profile/setup
        [HttpPost("setup")]
        public async Task<IActionResult> Setup(SetupProfileDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new
                {
                    status = 401,
                    message = "User identity was not found in the token."
                });
            }

            if (dto.Role != "Buyer" && dto.Role != "Seller")
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Only Buyer and Seller roles are allowed."
                });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "User account was not found."
                });
            }

            var roles = await _userManager.GetRolesAsync(user);

            var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile != null && profile.IsProfileSetupCompleted)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Profile setup is already completed."
                });
            }

            if (!await _roleManager.RoleExistsAsync(dto.Role))
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Invalid role."
                });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var phoneResult = await _userManager.SetPhoneNumberAsync(user, dto.PhoneNumber);
                if (!phoneResult.Succeeded)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Failed to update phone number.",
                        errors = phoneResult.Errors.Select(e => e.Description)
                    });
                }

                // Assign the selected Buyer or Seller role.
                var roleResult = await _userManager.AddToRoleAsync(user, dto.Role);
                if (!roleResult.Succeeded)
                {
                    return BadRequest(new
                    {
                        status = 400,
                        message = "Role assignment failed.",
                        errors = roleResult.Errors.Select(e => e.Description)
                    });
                }


                if (profile == null)
                {
                    profile = new UserProfile
                    {
                        UserId = user.Id,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.UserProfiles.Add(profile);
                }

                profile.FullName = dto.FullName;
                profile.Address = dto.Address;
                profile.City = dto.City;
                profile.IsProfileSetupCompleted = true;
                profile.UpdatedAt = DateTime.UtcNow;

                bool walletExists = await _context.Wallets.AnyAsync(w => w.UserId == userId);
                if (!walletExists)
                {
                    _context.Wallets.Add(new Wallet
                    {
                        UserId = user.Id,
                        Balance = 0,
                        ReservedBalance = 0
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            // Issue refreshed credentials containing the newly assigned role.
            var updatedRoles = await _userManager.GetRolesAsync(user);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email!)
            };

            foreach (var role in updatedRoles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var tokenString = _jwtTokenService.GenerateAccessToken(claims);
            var refreshToken = _jwtTokenService.GenerateRefreshToken();
            _context.RefreshTokens.Add(new RefreshToken
            {
                Token = refreshToken,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            });
            await _context.SaveChangesAsync();

            return Ok(new
            {
                status = 200,
                message = "Profile setup completed successfully.",
                data = new
                {
                    email = user.Email,
                    roles = updatedRoles,
                    token = tokenString,
                    refreshToken,
                    expiresInMinutes = 120,
                    profileSetupRequired = false,
                    profile = new
                    {
                        fullName = profile.FullName,
                        phoneNumber = user.PhoneNumber,
                        address = profile.Address,
                        city = profile.City,
                        isProfileSetupCompleted = profile.IsProfileSetupCompleted,
                        createdAt = profile.CreatedAt,
                        updatedAt = profile.UpdatedAt
                    }
                }
            });
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