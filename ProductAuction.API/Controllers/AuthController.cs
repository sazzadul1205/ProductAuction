using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using ProductAuction.API.Data;
using ProductAuction.API.DTOs.Auth;
using ProductAuction.API.Models;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ProductAuction.API.Controllers
{
    // This controller handles user registration and login.
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AppDbContext _context;

        // This lets us read settings from appsettings.json.
        private readonly IConfiguration _configuration;

        public AuthController(
            UserManager<IdentityUser> userManager,
            AppDbContext context,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _context = context;
            _configuration = configuration;
        }

        // POST: api/auth/register
        // Creates a new user account and saves their profile.
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            // Start measuring this request.
            var stopwatch = Stopwatch.StartNew();

            // Check whether this email is already registered.
            var existingUser =
                await _userManager.FindByEmailAsync(dto.Email);

            if (existingUser != null)
            {
                stopwatch.Stop();
                return BadRequest(new
                {
                    status = StatusCodes.Status400BadRequest,
                    message = "Email is already registered.",
                    timerMs = stopwatch.Elapsed.TotalMilliseconds
                });
            }

            // Create the account details for ASP.NET Identity.
            var user = new IdentityUser
            {
                UserName = dto.Email,
                Email = dto.Email
            };

            // Save the account and securely hash the password.
            var result = await _userManager.CreateAsync(user, dto.Password);

            // Return any errors if account creation failed.
            if (!result.Succeeded)
            {
                stopwatch.Stop();
                return BadRequest(new
                {
                    status = StatusCodes.Status400BadRequest,
                    message = "Registration failed.",
                    errors = result.Errors,
                    timerMs = stopwatch.Elapsed.TotalMilliseconds
                });
            }

            // Save the user's additional information in UserProfiles.
            var profile = new UserProfile
            {
                UserId = user.Id,
                FullName = dto.FullName,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserProfiles.Add(profile);
            await _context.SaveChangesAsync();

            stopwatch.Stop();

            // Tell the client that registration succeeded.
            return Ok(new
            {
                status = StatusCodes.Status200OK,
                message = "Registration successful.",
                timerMs = stopwatch.Elapsed.TotalMilliseconds
            });
        }

        // POST: api/auth/login
        // Checks the user's credentials and returns a JWT token.
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            // Start measuring the login operation.
            var stopwatch = Stopwatch.StartNew();

            // Find the account using the email address.
            var user = await _userManager.FindByEmailAsync(dto.Email);

            // Use the same message whether the email or password is wrong.
            if (user == null)
            {
                stopwatch.Stop();
                return Unauthorized(new
                {
                    status = StatusCodes.Status401Unauthorized,
                    message = "Invalid email or password.",
                    timerMs = stopwatch.Elapsed.TotalMilliseconds
                });
            }

            // Check the password against the stored password hash.
            var passwordIsValid = await _userManager.CheckPasswordAsync(user, dto.Password);

            if (!passwordIsValid)
            {
                stopwatch.Stop();
                return Unauthorized(new
                {
                    status = StatusCodes.Status401Unauthorized,
                    message = "Invalid email or password.",
                    timerMs = stopwatch.Elapsed.TotalMilliseconds
                });
            }

            // Claims are information about the logged-in user.
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email!)
            };

            // Read the secret key used to sign the token.
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));

            // Choose the signing algorithm for the token.
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Create a token that expires after two hours.
            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: credentials);

            // Convert the token object into a string for the client.
            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            stopwatch.Stop();

            // Return the token so the client can use it in API requests.
            return Ok(new
            {
                status = StatusCodes.Status200OK,
                message = "Login successful.",
                timerMs = stopwatch.Elapsed.TotalMilliseconds,
                token = tokenString,
                email = user.Email
            });
        }

        // GET: api/auth/test-token
        // Only users with a valid JWT token can access this endpoint.
        [Authorize]
        [HttpGet("test-token")]
        public IActionResult TestToken()
        {
            // Start measuring this request.
            var stopwatch = Stopwatch.StartNew();

            stopwatch.Stop();

            return Ok(new
            {
                status = StatusCodes.Status200OK,
                message = "JWT authentication successful.",
                timerMs = stopwatch.Elapsed.TotalMilliseconds,
                userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                email = User.FindFirst(ClaimTypes.Email)?.Value
            });
        }
    }
}