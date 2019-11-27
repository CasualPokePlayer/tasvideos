﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

using Microsoft.EntityFrameworkCore;

using TASVideos.Data;
using TASVideos.Data.Entity;
using TASVideos.Data.SeedData;
using TASVideos.Legacy.Data.Forum;
using TASVideos.Legacy.Data.Site;

namespace TASVideos.Legacy.Imports
{
	public static class UserImporter
	{
		private const int ModeratorGroupId = 272; // This isn't going to change, so just hard code it
		private const int EmulatorCoder = 40; // The rank id in the ranks table

		private static readonly string[] SiteDevelopers = { "natt", "Darkpsy", "Scepheo" };
		private static readonly int[] UserRatingBanList = { 7194, 4805, 4485, 5243, 635, 3301 }; // These users where explicitly banned from rating
		public static void Import(
			string connectionStr,
			ApplicationDbContext context,
			NesVideosSiteContext legacySiteContext,
			NesVideosForumContext legacyForumContext)
		{
			// TODO:
			// gender?
			// user_avatar_type ?
			// mood avatars
			// TODO: what to do about these??
			//var wikiNoForum = legacyUsers
			//	.Select(u => u.Name)
			//	.Except(legacyForumUsers.Select(u => u.UserName))
			//	.ToList();
			var legacyUsers = legacySiteContext.Users
				.Include(u => u.UserRoles)
				.ThenInclude(ur => ur.Role)
				.ToList();

			var emuCoders = legacyForumContext.UserRanks
				.Where(ur => ur.RankId == EmulatorCoder)
				.ToList();

			var users = (from u in legacyForumContext.Users
						join b in legacyForumContext.BanList on u.UserId equals b.UserId into bb
						from b in bb.DefaultIfEmpty()
						join e in emuCoders on u.UserId equals e.UserId into ee
						from e in ee.DefaultIfEmpty()
						join ug in legacyForumContext.UserGroups on new { u.UserId, GroupId = ModeratorGroupId } equals new { ug.UserId, ug.GroupId } into ugg
						from ug in ugg.DefaultIfEmpty()
						where u.UserName != "Anonymous"
						select new
						{
							Id = u.UserId,
							u.UserName,
							u.RegDate,
							u.Password,
							u.EmailTime,
							u.PostCount,
							u.Email,
							u.Avatar,
							u.From,
							u.Signature,
							u.PublicRatings,
							u.LastVisitDate,
							u.TimeZoneOffset,
							u.BbcodeUid,
							IsBanned = b != null,
							IsModerator = ug != null,
							IsForumAdmin = u.UserLevel == 1,
							IsEmuCoder = e != null,
							u.MoodAvatar
						})
						.ToList();

			var timeZones = TimeZoneInfo.GetSystemTimeZones();
			var utc = TimeZoneInfo.Utc.StandardName;
			var userEntities = users
				.Select(u => new User
				{
					Id = u.Id,
					UserName = ImportHelper.ConvertNotNullLatin1String(u.UserName).Trim(),
					//NormalizedUserName = ImportHelper.ConvertLatin1String(u.UserName).ToUpper(), // TODO: this creates a unique constraint violation, two users must have the same normalized name??
					NormalizedUserName = u.UserName.ToUpper(),
					CreateTimeStamp = ImportHelper.UnixTimeStampToDateTime(u.RegDate),
					LastUpdateTimeStamp = ImportHelper.UnixTimeStampToDateTime(u.RegDate), // TODO
					LegacyPassword = u.Password,
					EmailConfirmed = u.EmailTime != null || u.PostCount > 0,
					Email = u.Email,
					NormalizedEmail = u.Email.ToUpper(),
					CreateUserName = "Automatic Migration",
					PasswordHash = "",
					Avatar = u.Avatar,
					From = WebUtility.HtmlDecode(ImportHelper.ConvertLatin1String(u.From)),
					Signature = WebUtility
						.HtmlDecode(
							ImportHelper.ConvertLatin1String(u.Signature.Replace(":" + u.BbcodeUid, "")))
						.Cap(1000),
					PublicRatings = u.PublicRatings,
					LastLoggedInTimeStamp = ImportHelper.UnixTimeStampToDateTime(u.LastVisitDate),
					// ReSharper disable once CompareOfFloatsByEqualityOperator
					TimeZoneId = timeZones.FirstOrDefault(t => t.BaseUtcOffset.TotalMinutes / 60 == (double)u.TimeZoneOffset)?.StandardName ?? utc,
					SecurityStamp = Guid.NewGuid().ToString("D"),
					UseRatings = !u.IsBanned && !UserRatingBanList.Contains(u.Id),
					MoodAvatarUrlBase = u.MoodAvatar.NullIfWhiteSpace()
				})
				.ToList();

			// Hacks
			var grue = userEntities.First(u => u.UserName == "TASVideos Grue");
			grue.UserName = "TASVideosGrue";
			
			var roles = context.Roles.ToList();

			var userRoles = new List<UserRole>();

			var joinedUsers = from user in users
					join su in legacyUsers on user.UserName equals su.Name into lsu
					from su in lsu.DefaultIfEmpty()
					select new { User = user, SiteUser = su };

			foreach (var user in joinedUsers)
			{
				if (user.SiteUser != null)
				{
					// not having user means they are effectively banned
					// limited = Limited User
					if ((user.SiteUser.UserRoles.Any(ur => ur.Role!.Name == "user") || user.User.Id == 505) // 505 is TASVideoAgent, which needs some roles but is not a user
						&& user.SiteUser.UserRoles.All(ur => ur.Role!.Name != "admin")) // There's no point in adding these roles to admins, they have these perms anyway
					{
						if (!user.User.IsBanned)
						{
							if (user.SiteUser.UserRoles.Any(ur => ur!.Role!.Name == "limited"))
							{
								userRoles.Add(new UserRole
								{
									RoleId = roles.Single(r => r.Name == RoleSeedNames.LimitedUser).Id,
									UserId = user.User.Id
								});
							}
							else
							{
								userRoles.Add(new UserRole
								{
									RoleId = roles.Single(r => r.Name == RoleSeedNames.DefaultUser).Id,
									UserId = user.User.Id
								});
							}

							if (user.User.PostCount >= SiteGlobalConstants.VestedPostCount)
							{
								userRoles.Add(new UserRole
								{
									RoleId = roles.Single(r => r.Name == RoleSeedNames.ExperiencedForumUser).Id,
									UserId = user.User.Id
								});
							}
						}
					}

					if (user.User.IsForumAdmin
						&& user.SiteUser.UserRoles.All(ur => ur.Role!.Name != "admin")) // There's no point in adding roles to admins, they have these perms anyway
					{
						userRoles.Add(new UserRole
						{
							RoleId = roles.Single(r => r.Name == RoleSeedNames.ForumAdmin).Id,
							UserId = user.User.Id
						});
					}
					else if (user.User.IsModerator
						&& user.SiteUser.UserRoles.All(ur => ur.Role!.Name != "admin")) // There's no point in adding roles to admins, they have these perms anyway
					{
						userRoles.Add(new UserRole
						{
							RoleId = roles.Single(r => r.Name == RoleSeedNames.ForumModerator).Id,
							UserId = user.User.Id
						});
					}

					if (user.User.UserName == "dwangoAC")
					{
						userRoles.Add(new UserRole
						{
							RoleId = roles.Single(r => r.Name == RoleSeedNames.Ambassador).Id,
							UserId = user.User.Id
						});
					}

					if (SiteDevelopers.Contains(user.User.UserName))
					{
						userRoles.Add(new UserRole
						{
							RoleId = roles.Single(r => r.Name == RoleSeedNames.SiteDeveloper).Id,
							UserId = user.User.Id
						});
					}

					if (user.User.IsEmuCoder)
					{
						userRoles.Add(new UserRole
						{
							RoleId = roles.Single(r => r.Name == RoleSeedNames.EmulatorCoder).Id,
							UserId = user.User.Id
						});
					}

					foreach (var userRole in user.SiteUser.UserRoles.Select(ur => ur.Role)
						.Where(r => r!.Name != "user" && r.Name != "limited"))
					{
						var role = GetRoleFromLegacy(userRole!.Name, roles);
						if (role != null)
						{
							userRoles.Add(new UserRole
							{
								RoleId = role.Id,
								UserId = user.User.Id
							});
						}
					}
				}
			}

			userEntities.Add(new User
			{
				Id = -1,
				UserName = "Unknown User",
				NormalizedUserName = "UNKNOWN USER",
				Email = "",
				EmailConfirmed = true,
				LegacyPassword = ""
			});

			// Some published authors that have no forum account
			// Note that by having no password nor legacy password they effectively can not log in without a database change
			// I think this is correct since these are not active users
			var portedPlayerNames = new[]
			{
				"Morimoto",
				"Tokushin",
				"Yy",
				"Mathieu P",
				"Linnom",
				"Mclaud2000",
				"Ryosuke",
				"JuanPablo",
				"qcommand",
				"Mana.",
				"RaverMeister",
				"SjoerdH",
				"snark",
				"ToT",

				// Submitters with no published movies
				"Deathray",
				"Ginger",
				"JakeRyansDad",
				"KMFDManic",
				"Remy B.",
				"ScouSin",
				"Vazor",
				"KennyBoy",
				"megaman",
				"Oguz",
				"Vlass14",
				"dex88",
				"VladimirContreras"
			};

			var portedPlayers = portedPlayerNames.Select(p => new User
			{
				UserName = p,
				NormalizedUserName = p.ToUpper(),
				Email = $"imported{p}@tasvideos.org",
				NormalizedEmail = $"imported{p}@tasvideos.org".ToUpper(),
				CreateTimeStamp = DateTime.UtcNow,
				LastUpdateTimeStamp = DateTime.UtcNow,
				SecurityStamp = Guid.NewGuid().ToString("D")
			});

			var userColumns = new[]
			{
				nameof(User.Id),
				nameof(User.UserName),
				nameof(User.NormalizedUserName),
				nameof(User.CreateTimeStamp),
				nameof(User.LegacyPassword),
				nameof(User.EmailConfirmed),
				nameof(User.Email),
				nameof(User.NormalizedEmail),
				nameof(User.CreateUserName),
				nameof(User.PasswordHash),
				nameof(User.AccessFailedCount),
				nameof(User.LastUpdateTimeStamp),
				nameof(User.LockoutEnabled),
				nameof(User.PhoneNumberConfirmed),
				nameof(User.TwoFactorEnabled),
				nameof(User.Avatar),
				nameof(User.From),
				nameof(User.Signature),
				nameof(User.PublicRatings),
				nameof(User.LastLoggedInTimeStamp),
				nameof(User.TimeZoneId),
				nameof(User.SecurityStamp),
				nameof(User.UseRatings),
				nameof(User.MoodAvatarUrlBase)
			};

			var userRoleColumns = new[]
			{
				nameof(UserRole.UserId),
				nameof(UserRole.RoleId)
			};

			userEntities.BulkInsert(connectionStr, userColumns, "[User]");
			userRoles.BulkInsert(connectionStr, userRoleColumns, "[UserRoles]");

			var playerColumns = userColumns.Where(p => p != nameof(User.Id)).ToArray();
			portedPlayers.BulkInsert(connectionStr, playerColumns, "[User]");
		}

		private static Role? GetRoleFromLegacy(string role, IEnumerable<Role> roles)
		{
			switch (role.ToLower())
			{
				default:
					return null;
				case "editor":
					return roles.Single(r => r.Name == RoleSeedNames.Editor);
				case "vestededitor":
					return roles.Single(r => r.Name == RoleSeedNames.VestedEditor);
				case "publisher":
					return roles.Single(r => r.Name == RoleSeedNames.Publisher);
				case "seniorpublisher":
					return roles.Single(r => r.Name == RoleSeedNames.SeniorPublisher);
				case "judge":
					return roles.Single(r => r.Name == RoleSeedNames.Judge);
				case "seniorjudge":
					return roles.Single(r => r.Name == RoleSeedNames.SeniorJudge);
				case "adminassistant":
					return roles.Single(r => r.Name == RoleSeedNames.AdminAssistant);
				case "admin":
					return roles.Single(r => r.Name == RoleSeedNames.Admin);
			}
		}
	}
}
