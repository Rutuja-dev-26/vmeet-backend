using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using JwtRegisteredClaimNames = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;

namespace VMeetTool.Helpers
{
    public class JwtHelper
    {
        private static string SecretKey   => ConfigurationManager.AppSettings["JwtSecretKey"];
        private static string Issuer      => ConfigurationManager.AppSettings["JwtIssuer"];
        private static string Audience    => ConfigurationManager.AppSettings["JwtAudience"];
        private static int    ExpiryMins  => int.Parse(ConfigurationManager.AppSettings["JwtExpiryMinutes"] ?? "60");

        public static string GenerateToken(int userId, string userName, string email, string fullName)
            => BuildToken(userId, userName, email, fullName, ExpiryMins, "access");

        public static string GenerateRefreshToken(int userId, string userName, string email, string fullName)
            => BuildToken(userId, userName, email, fullName, ExpiryMins * 24 * 7, "refresh");

        private static string BuildToken(int userId, string userName, string email, string fullName, int expiryMinutes, string tokenType)
        {
            var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub,        userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, userName),
                new Claim(JwtRegisteredClaimNames.Email,      email),
                new Claim("full_name",                        fullName),
                new Claim("token_type",                       tokenType),
                new Claim(JwtRegisteredClaimNames.Jti,        Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat,
                          DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                          ClaimValueTypes.Integer64)
            };

            var token = new JwtSecurityToken(
                issuer:            Issuer,
                audience:          Audience,
                claims:            claims,
                notBefore:         DateTime.UtcNow,
                expires:           DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public static ClaimsPrincipal ValidateToken(string token)
        {
            try
            {
                var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
                var handler = new JwtSecurityTokenHandler();

                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = key,
                    ValidateIssuer           = true,
                    ValidIssuer              = Issuer,
                    ValidateAudience         = true,
                    ValidAudience            = Audience,
                    ValidateLifetime         = true,
                    ClockSkew                = TimeSpan.Zero
                };

                return handler.ValidateToken(token, validationParams, out _);
            }
            catch
            {
                return null;
            }
        }

        public static int ExpirySeconds => ExpiryMins * 60;

        public static DateTime AccessTokenExpiresAt => DateTime.UtcNow.AddMinutes(ExpiryMins);
    }
}
