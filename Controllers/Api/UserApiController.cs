using CMS.Data;
using CMS.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMS.Controllers.Api;

[ApiController]
[Route("api/users")]
[Authorize]
public class UserApiController : ControllerBase
{
    private readonly DapperContext _context;

    public UserApiController(DapperContext context)
    {
        _context = context;
    }

    // GET: api/users
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserSearchResult>>> GetAll()
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string sql = @"
            SELECT USER_ID AS Id
                , FULL_NAME AS Name
                , EMAIL
            FROM MM_USER WHERE STATUS = 1;
        ";

        var users = await connection.QueryAsync<UserSearchResult>(sql);
        return Ok(users);
    }

    // GET: api/users/search
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<UserSearchResult>>> Search([FromQuery] string? q)
    {
        var search = q?.Trim();
        if (string.IsNullOrWhiteSpace(search))
        {
            return Ok(Array.Empty<UserSearchResult>());
        }

        var normalizedSearch = $"%{search.ToLower()}";

        await using var connection = await _context.CreateOpenConnectionAsync();

        const string sql = @"
            SELECT TOP 10 USER_ID AS Id
                , FULL_NAME AS Name
                , EMAIL
            FROM MM_USER
            WHERE LOWER(FULL_NAME) LIKE @Search
                OR LOWER(EMAIL) LIKE @Search
                AND STATUS = 1
            ORDER BY FULL_NAME;
        ";

        var users = await connection.QueryAsync<UserSearchResult>(sql, new { Search = normalizedSearch });
        return Ok(users);
    }

    // GET: api/users/id
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<User>> Get(Guid id)
    {
        await using var connection = await _context.CreateOpenConnectionAsync();

        const string sql = @"
            SELECT USER_ID AS Id
                , FULL_NAME AS FullName
                , EMAIL
                , CREATED_DATE AS CreatedAt
            FROM MM_USER
            WHERE USER_ID = @Id
                AND STATUS = 1;
        ";

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
