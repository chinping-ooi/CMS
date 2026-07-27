using CMS.Models;

namespace CMS.Services.Auth;

public interface IJwtTokenService
{
    string GenerateToken(User user);
}
