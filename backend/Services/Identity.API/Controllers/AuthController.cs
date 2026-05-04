using Identity.API.Models;
using Identity.API.Models.DTOs;
using Identity.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Identity.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ITokenService _tokenService;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ITokenService tokenService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _tokenService = tokenService;
        }

        [HttpPost("register")]
        public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
        {
            if (await _userManager.FindByEmailAsync(request.Email) != null)
            {
                return BadRequest(new AuthResponse { Success = false, Message = "Email is already taken" });
            }

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                FullName = request.FullName
            };

            var result = await _userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
            {
                return BadRequest(new AuthResponse { Success = false, Message = string.Join(", ", result.Errors.Select(e => e.Description)) });
            }

            return Ok(new AuthResponse 
            { 
                Success = true, 
                Token = _tokenService.CreateToken(user),
                Message = "User registered successfully" 
            });
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);

            if (user == null) return Unauthorized(new AuthResponse { Success = false, Message = "Invalid email or password" });

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);

            if (!result.Succeeded) return Unauthorized(new AuthResponse { Success = false, Message = "Invalid email or password" });

            return Ok(new AuthResponse
            {
                Success = true,
                Token = _tokenService.CreateToken(user),
                Message = "Login successful"
            });
        }
    }
}
