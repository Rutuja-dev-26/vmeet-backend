using Microsoft.IdentityModel.JsonWebTokens;
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
        // Add these keys to your Web.config <appSettings>:
        //   <add key="JwtSecretKey"       value="YOUR_SECRET_KEY_MIN_32_CHARS" />
        //   <add key="JwtIssuer"          value="VMeetTool" />
        //   <add key="JwtAudience"        value="VMeetToolUsers" />
        //   <add key="JwtExpiryMinutes"   value="60" />

        private static string SecretKey => ConfigurationManager.AppSettings["JwtSecretKey"];
        private static string Issuer => ConfigurationManager.AppSettings["JwtIssuer"];
        private static string Audience => ConfigurationManager.AppSettings["JwtAudience"];
        private static int ExpiryMins => int.Parse(ConfigurationManager.AppSettings["JwtExpiryMinutes"] ?? "60");

        /// <summary>
        /// Generates a signed JWT token for the given user.
        /// </summary>
        public static string GenerateToken(int userId, string userName, string email, string fullName)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub,   userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, userName),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim("full_name",                   fullName),
                new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),   // unique token ID
                new Claim(JwtRegisteredClaimNames.Iat,
                          DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                          ClaimValueTypes.Integer64)
            };

            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(ExpiryMins),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Validates a JWT token and returns its ClaimsPrincipal.
        /// Returns null if invalid or expired.
        /// </summary>
        public static ClaimsPrincipal ValidateToken(string token)
        {
            try
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
                var handler = new JwtSecurityTokenHandler();

                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero   // no tolerance on expiry
                };

                return handler.ValidateToken(token, validationParams, out _);
            }
            catch
            {
                return null;
            }
        }

        public static int ExpirySeconds => ExpiryMins * 60;
    }
}