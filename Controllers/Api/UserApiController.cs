using CMS.Data;
using CMS.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace CMS.Controllers.Api;

[ApiController]
[Route("api/users")]
public class UserApiController : ControllerBase
{
    private readonly DapperContext _context;

    public UserApiController(DapperContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserSearchResult>>> GetAll()
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string sql = "SELECT id, full_name, email FROM users";

        var users = await connection.QueryAsync<UserSearchResult>(sql);
        return Ok(users);
    }

    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<UserSearchResult>>> Search([FromQuery] string? q)
    {
        var search = q?.Trim();
        if (string.IsNullOrWhiteSpace(search))
        {
            return Ok(Array.Empty<UserSearchResult>());
        }

        var normalizedSearch = $"%{search.ToLower()}%";

        await using var connection = await _context.CreateOpenConnectionAsync();

        const string sql = "SELECT id, full_name, email FROM users WHERE LOWER(full_name) LIKE @Search OR LOWER(email) LIKE @Search LIMIT 10";

        var users = await connection.QueryAsync<UserSearchResult>(sql, new { Search = normalizedSearch });
        return Ok(users);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<User>> Get(Guid id)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string sql = @"
            SELECT
                id,
                full_name,
                email,
                created_at
            FROM users
            WHERE id = @Id";

        var user = await connection.QuerySingleOrDefaultAsync<User>(sql, new { Id = id });
        if (user == null)
        {
            return NotFound();
        }

        return Ok(user);
    }
}

public sealed class UserSearchResult
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
