using CMS.Data;
using CMS.Models;
using CMS.Services.Auth;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CMS.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthApiController : ControllerBase
{
    private readonly DapperContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtSettings _jwtSettings;

    public AuthApiController(
        DapperContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IOptions<JwtSettings> jwtSettings)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _jwtSettings = jwtSettings.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Email and password are required.");
        }

        await using var connection = await _context.CreateOpenConnectionAsync();

        const string sql = @"
            SELECT id, full_name AS FullName, email, password_hash AS PasswordHash,
                   password_salt AS PasswordSalt, created_at AS CreatedAt
            FROM users
            WHERE LOWER(email) = LOWER(@Email)
            LIMIT 1";

        var user = await connection.QuerySingleOrDefaultAsync<User>(sql, new { Email = request.Email.Trim() });
        if (user == null || !_passwordHasher.Verify(request.Password, user.PasswordHash ?? string.Empty, user.PasswordSalt ?? string.Empty))
        {
            return Unauthorized("Invalid email or password.");
        }

        var token = _jwtTokenService.GenerateToken(user);
        SetAuthCookie(token);

        return Ok(new LoginResponse
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiresMinutes),
            User = new AuthUserResponse
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
            },
        });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest("Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
        {
            return BadRequest("Password must be at least 6 characters.");
        }

        var (hash, salt) = _passwordHasher.HashPassword(request.Password);

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = hash,
            PasswordSalt = salt,
            CreatedAt = DateTime.UtcNow,
        };

        await using var connection = await _context.CreateOpenConnectionAsync();

        const string emailExistsSql = "SELECT COUNT(1) FROM users WHERE LOWER(email) = LOWER(@Email)";
        var emailExists = await connection.ExecuteScalarAsync<int>(emailExistsSql, new { user.Email }) > 0;
        if (emailExists)
        {
            return Conflict("A user with this email already exists.");
        }

        const string insertSql = @"
            INSERT INTO users (id, full_name, email, password_hash, password_salt, created_at)
            VALUES (@Id, @FullName, @Email, @PasswordHash, @PasswordSalt, @CreatedAt)";

        await connection.ExecuteAsync(insertSql, user);

        var token = _jwtTokenService.GenerateToken(user);
        SetAuthCookie(token);

        return Ok(new LoginResponse
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiresMinutes),
            User = new AuthUserResponse
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
            },
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("cms_token");
        return NoContent();
    }

    private void SetAuthCookie(string token)
    {
        Response.Cookies.Append(
            "cms_token",
            token,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddMinutes(_jwtSettings.ExpiresMinutes),
            });
    }
}

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public AuthUserResponse User { get; set; } = new();
}

public sealed class AuthUserResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
