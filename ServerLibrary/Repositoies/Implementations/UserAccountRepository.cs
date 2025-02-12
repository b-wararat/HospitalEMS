﻿using BaseLibrary.DTOs;
using BaseLibrary.Entities;
using BaseLibrary.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ServerLibrary.Data;
using ServerLibrary.Helpers;
using ServerLibrary.Repositoies.Contracts;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ServerLibrary.Repositoies.Implementations
{
    public class UserAccountRepository : IUserAccount
    {
        private readonly IOptions<JwtSection> jwtSection;
        private readonly AppDbContext dbContext;
        public UserAccountRepository(IOptions<JwtSection> jwtSection, AppDbContext dbContext)
        {
            this.jwtSection = jwtSection;
            this.dbContext = dbContext;
        }
        public async Task<GeneralResponse> CreateAsync(Register user)
        {
            if (user is null) return new GeneralResponse(false, "Model is empty");

            var checkUser = await FindUserByEmailAsync(user.Email!);
            if (checkUser is not null) return new GeneralResponse(false, "User registered already");

            //Save user
            var applicationUser = await AddToDatabase(new ApplicationUser() {
                Fullname = user.Fullname,
                Email = user.Email,
                Password = BCrypt.Net.BCrypt.HashPassword(user.Password)
            });

            //check create and assign role
            var systemRoles = await dbContext.SystemRoles
                .Where(w => w.Name! == Constants.Admin || w.Name! == Constants.User).ToListAsync();
            var checkAdminRole = systemRoles.FirstOrDefault(w => w.Name! == Constants.Admin);
            //first user will assign to role admin
            if (checkAdminRole is null) {
                var createAdminRole = await AddToDatabase(new SystemRole() { Name = Constants.Admin });
                await AddToDatabase(new UserRole() { RoleId = createAdminRole.Id, UserId = applicationUser.Id });
                return new GeneralResponse(true, "Account created!");
            }

            //second user and more than assign to user
            var checkUserRole = systemRoles.FirstOrDefault(w => w.Name! == Constants.User);
            SystemRole response = new();
            if (checkUserRole is null)
            {
                response = await AddToDatabase(new SystemRole() { Name = Constants.User });
                await AddToDatabase(new UserRole() { RoleId = response.Id, UserId = applicationUser.Id });
            }
            else {
                await AddToDatabase(new UserRole() { RoleId = checkUserRole.Id, UserId = applicationUser.Id });
            }
            return new GeneralResponse(true, "Account created!");
        }

        public async Task<LoginResponse> SignInAsync(Login user)
        {
            if (user is null) return new LoginResponse(false, "Model is empty");
            var applicationUser = await FindUserByEmailAsync(user.Email!);
            if (applicationUser is null) return new LoginResponse(false, "user not found");

            //Verify password
            if (!BCrypt.Net.BCrypt.Verify(user.Password, applicationUser.Password)) {
                return new LoginResponse(false, "Email or password not vilid");
            }

            var roleName = await FindRoleName(applicationUser.Id);
            if (roleName is null) return new LoginResponse(false, "user role not found");

            string jwtToken = GenerateToken(applicationUser, roleName);
            string refreshToken = GenerateRefreshToken();

            //Save the refresh token to the database 
            var findUserToken = await dbContext.RefreshTokenInfos.FirstOrDefaultAsync(w => w.UserId == applicationUser.Id);
            if (findUserToken is not null)
            {
                findUserToken!.Token = refreshToken;
            }
            else {
                await AddToDatabase(new RefreshTokenInfo() { Token = refreshToken, UserId = applicationUser.Id });
            }
            await dbContext.SaveChangesAsync();

            return new LoginResponse(true, "Login Successfully!", jwtToken, refreshToken);

        }


        private string GenerateToken(ApplicationUser user, string role)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection.Value.Key!));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            var userClaims = new[] {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Fullname!),
                new Claim(ClaimTypes.Email, user.Email!),
                new Claim(ClaimTypes.Role, role!)
            };

            var token = new JwtSecurityToken(
                    issuer: jwtSection.Value.Issuer,
                    audience: jwtSection.Value.Audience,
                    claims: userClaims,
                    expires: DateTime.Now.AddMicroseconds(1),
                    signingCredentials: credentials
                );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }


        private async Task<string> FindRoleName(int userId)
        {
            return await dbContext.UserRoles.Where(w => w.UserId == userId)
                .Join(dbContext.SystemRoles, user => user.RoleId, role => role.Id, (user, role) => new { user, role })
                .Select(s => s.role.Name).FirstOrDefaultAsync();
        }

        private static string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        private async Task<ApplicationUser> FindUserByEmailAsync(string email) =>
            await dbContext.ApplicationUsers
            .FirstOrDefaultAsync(w => w.Email!.ToLower() == email!.ToLower());
        
        private async Task<T> AddToDatabase<T>(T model) {
            var result = dbContext.Add(model!);
            await dbContext.SaveChangesAsync();
            return (T)result.Entity;
        }

        public async Task<LoginResponse> RefreshTokenAsync(RefreshToken token)
        {
            if (token is null) return new LoginResponse(false, "model is empty");

            var findToken = await dbContext.RefreshTokenInfos.FirstOrDefaultAsync(w => w.Token!.Equals(token.Token));
            if (findToken is null) return new LoginResponse(false, "refresh token is required");

            //get user detail
            var user = await dbContext.ApplicationUsers.FirstOrDefaultAsync(w => w.Id == findToken.UserId);
            if (user is null) return new LoginResponse(false, "Refresh token could not be generated because user not found");

            var roleName = await FindRoleName(user.Id);
            string jwtToken = GenerateToken(user, roleName);
            string refreshToken = GenerateRefreshToken();

            findToken.Token = refreshToken;
            await dbContext.SaveChangesAsync();
            return new LoginResponse(true,"Token refreshed successfully", jwtToken, refreshToken);

        }

        public async Task<List<ManageUser>> GetUsersAsync()
        {
            var allUsers = await GetApplicationUsers();
            var allUserRoles = await GetUserRoles();
            var allRoles = await GetSystemRoels();
            if (!allRoles?.Any() == true || !allUserRoles?.Any() == true) return null;
            var users = new List<ManageUser>();
            foreach (var user in allUsers) {
                var userRole = allUserRoles!.FirstOrDefault(w => w.UserId == user.Id);
                var role = allRoles!.FirstOrDefault(w => w.Id == userRole!.RoleId);
                users.Add(new ManageUser
                {
                    UserId = user.Id,
                    Name = user.Fullname!,
                    Email = user.Email!,
                    Role = role!.Name!
                });
            }
            return users;
        }

        public async Task<List<SystemRole>> GetRoles()
        {
            return await GetSystemRoels();
        }

        public async Task<GeneralResponse> UpdateUser(ManageUser user)
        {
            var getRole = (await GetSystemRoels()).FirstOrDefault(w => w.Id.ToString().Equals(user.Role));
            var userRole = await dbContext.UserRoles.FirstOrDefaultAsync(w => w.UserId == user.UserId);
            if (getRole is null || getRole.Id == 0) return new GeneralResponse(false, "Role not found");
            userRole!.RoleId = getRole!.Id;
            await dbContext.SaveChangesAsync();
            return new GeneralResponse(true, "user role updated successfully");
        }

        public async Task<GeneralResponse> DeleteUser(int id)
        {
            var user = await dbContext.ApplicationUsers.FirstOrDefaultAsync(w => w.Id == id);
            if (user is null) return new GeneralResponse(false, "User not found");
            dbContext.ApplicationUsers.Remove(user);
            await dbContext.SaveChangesAsync();
            return new GeneralResponse(true,"user successfully deleted");
        }

        private async Task<List<SystemRole>> GetSystemRoels()
        {
            return await dbContext.SystemRoles.AsNoTracking().ToListAsync();
        }

        private async Task<List<UserRole>> GetUserRoles()
        {
            return await dbContext.UserRoles.AsNoTracking().ToListAsync();
        }

        private async Task<List<ApplicationUser>> GetApplicationUsers()
        {
            return await dbContext.ApplicationUsers.AsNoTracking().ToListAsync();
        }
    }
}
