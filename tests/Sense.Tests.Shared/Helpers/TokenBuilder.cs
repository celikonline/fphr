using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Sense.Tests.Shared.Helpers;

/// <summary>
/// Builder for creating test JWT tokens with custom claims (tenantId, userId, roles).
/// Tokens are unsigned and for testing only.
/// </summary>
public sealed class TokenBuilder
{
    private readonly string _secretKey;
    private Guid _userId = Guid.NewGuid();
    private Guid _tenantId = Guid.NewGuid();
    private Guid? _employeeId;
    private string _email = "test@example.com";
    private string _displayName = "Test User";
    private bool _isPlatformAdmin = false;
    private Guid? _impersonatorId;
    private Guid? _apiKeyId;
    private readonly List<string> _roles = new();

    public TokenBuilder(string secretKey = "test-secret-key-for-testing-only-do-not-use-in-production")
    {
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
    }

    /// <summary>Set the user ID for this token.</summary>
    public TokenBuilder WithUserId(Guid userId)
    {
        _userId = userId;
        return this;
    }

    /// <summary>Set the tenant ID for this token.</summary>
    public TokenBuilder WithTenantId(Guid tenantId)
    {
        _tenantId = tenantId;
        return this;
    }

    /// <summary>Set the employee ID for this token.</summary>
    public TokenBuilder WithEmployeeId(Guid employeeId)
    {
        _employeeId = employeeId;
        return this;
    }

    /// <summary>Set the email address.</summary>
    public TokenBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    /// <summary>Set the display name.</summary>
    public TokenBuilder WithDisplayName(string displayName)
    {
        _displayName = displayName;
        return this;
    }

    /// <summary>Mark as platform admin.</summary>
    public TokenBuilder AsPlatformAdmin()
    {
        _isPlatformAdmin = true;
        return this;
    }

    /// <summary>Set the impersonator user ID.</summary>
    public TokenBuilder WithImpersonatorId(Guid impersonatorId)
    {
        _impersonatorId = impersonatorId;
        return this;
    }

    /// <summary>Set the API key ID.</summary>
    public TokenBuilder WithApiKeyId(Guid apiKeyId)
    {
        _apiKeyId = apiKeyId;
        return this;
    }

    /// <summary>Add a role to this token.</summary>
    public TokenBuilder WithRole(string role)
    {
        if (!_roles.Contains(role))
        {
            _roles.Add(role);
        }

        return this;
    }

    /// <summary>Add multiple roles to this token.</summary>
    public TokenBuilder WithRoles(params string[] roles)
    {
        foreach (var role in roles)
        {
            WithRole(role);
        }

        return this;
    }

    /// <summary>Build the JWT token as a string.</summary>
    public string Build()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_secretKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _userId.ToString()),
            new("tenant_id", _tenantId.ToString()),
            new(ClaimTypes.Email, _email),
            new("display_name", _displayName),
            new("is_platform_admin", _isPlatformAdmin.ToString()),
        };

        if (_employeeId.HasValue)
        {
            claims.Add(new("employee_id", _employeeId.ToString()));
        }

        if (_impersonatorId.HasValue)
        {
            claims.Add(new("impersonator_user_id", _impersonatorId.ToString()));
        }

        if (_apiKeyId.HasValue)
        {
            claims.Add(new("api_key_id", _apiKeyId.ToString()));
        }

        foreach (var role in _roles)
        {
            claims.Add(new(ClaimTypes.Role, role));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature),
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>Create a token for a specific tenant and user.</summary>
    public static string CreateForUser(Guid tenantId, Guid userId, string email = "test@example.com")
    {
        return new TokenBuilder()
            .WithTenantId(tenantId)
            .WithUserId(userId)
            .WithEmail(email)
            .Build();
    }

    /// <summary>Create an admin token for a tenant.</summary>
    public static string CreateAdminToken(Guid tenantId)
    {
        return new TokenBuilder()
            .WithTenantId(tenantId)
            .WithUserId(Guid.NewGuid())
            .WithEmail("admin@example.com")
            .WithDisplayName("Admin User")
            .AsPlatformAdmin()
            .WithRole("Admin")
            .Build();
    }
}
