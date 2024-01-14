﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TASVideos.Core.Services.ExternalMediaPublisher;
using TASVideos.Data;
using TASVideos.Data.Entity;
using TASVideos.Pages.UserFiles.Models;

namespace TASVideos.Pages.UserFiles;

[AllowAnonymous]
public class IndexModel : BasePageModel
{
	private readonly ApplicationDbContext _db;
	private readonly ExternalMediaPublisher _publisher;

	public IndexModel(
		ApplicationDbContext db,
		ExternalMediaPublisher publisher)
	{
		_db = db;
		_publisher = publisher;
	}

	public UserFileIndexModel Data { get; set; } = new();

	public async Task OnGet()
	{
		Data = new UserFileIndexModel
		{
			UsersWithMovies = await _db.UserFiles
				.ThatArePublic()
				.GroupBy(gkey => gkey.Author!.UserName, gvalue => gvalue.UploadTimestamp).Select(
					uf => new UserFileIndexModel.UserWithMovie { UserName = uf.Key, Latest = uf.Max() })
				.ToListAsync(),
			LatestMovies = await _db.UserFiles
				.ThatArePublic()
				.ByRecentlyUploaded()
				.ToUserMovieListModel()
				.Take(10)
				.ToListAsync(),
			GamesWithMovies = await _db.Games
				.Where(g => g.UserFiles.Any(uf => !uf.Hidden))
				.OrderBy(g => g.DisplayName)
				.Select(g => new UserFileIndexModel.GameWithMovie
				{
					GameId = g.Id,
					GameName = g.DisplayName,
					Dates = g.UserFiles.Select(uf => uf.UploadTimestamp).ToList()
				})
				.ToListAsync(),
			UncatalogedFiles = await _db.UserFiles
				.Where(uf => uf.GameId == null)
				.Where(uf => !uf.Hidden)
				.Select(uf => new UncatalogedViewModel
				{
					Id = uf.Id,
					FileName = uf.FileName,
					SystemCode = uf.System != null ? uf.System.Code : null,
					UploadTimestamp = uf.UploadTimestamp,
					Author = uf.Author!.UserName
				})
				.Take(25)
				.ToListAsync()
		};
	}

	public async Task<IActionResult> OnPostDelete(long fileId)
	{
		var userFile = await _db.UserFiles.SingleOrDefaultAsync(u => u.Id == fileId);
		if (userFile is not null)
		{
			if (User.GetUserId() == userFile.AuthorId
				|| User.Has(PermissionTo.EditUserFiles))
			{
				_db.UserFiles.Remove(userFile);

				await ConcurrentSave(_db, $"{userFile.FileName} deleted", $"Unable to delete {userFile.FileName}");
			}
		}

		return BaseReturnUrlRedirect();
	}

	public async Task<IActionResult> OnPostComment(long fileId, string comment)
	{
		if (User.Has(PermissionTo.CreateForumPosts)
			&& !string.IsNullOrWhiteSpace(comment))
		{
			var userFile = await _db.UserFiles.SingleOrDefaultAsync(u => u.Id == fileId);
			if (userFile is not null)
			{
				_db.UserFileComments.Add(new UserFileComment
				{
					UserFileId = fileId,
					Text = comment,
					UserId = User.GetUserId(),
					Ip = IpAddress,
					CreationTimeStamp = DateTime.UtcNow
				});

				await _db.SaveChangesAsync();
				await _publisher.SendUserFile(
					userFile.Hidden,
					$"New user file comment by {User.Name()}",
					$"New [user file]({{0}}) comment by {User.Name()}",
					$"UserFiles/Info/{fileId}",
					$"{userFile.Title}");
			}
		}

		return BaseReturnUrlRedirect();
	}

	public async Task<IActionResult> OnPostEditComment(long commentId, string comment)
	{
		if (User.Has(PermissionTo.CreateForumPosts)
			&& !string.IsNullOrWhiteSpace(comment))
		{
			var fileComment = await _db.UserFileComments
				.Include(c => c.UserFile)
				.SingleOrDefaultAsync(u => u.Id == commentId);

			if (fileComment is not null)
			{
				fileComment.Text = comment;

				var result = await ConcurrentSave(_db, "Comment edited", "Unable to edit comment");
				if (result)
				{
					await _publisher.SendUserFile(
						fileComment.UserFile!.Hidden,
						$"User file comment edited by {User.Name()}",
						$"[User file]({{0}}) comment edited by {User.Name()}",
						$"UserFiles/Info/{fileComment.UserFile.Id}",
						$"{fileComment.UserFile!.Title}");
				}
			}
		}

		return BaseReturnUrlRedirect();
	}

	public async Task<IActionResult> OnPostDeleteComment(long commentId)
	{
		if (User.Has(PermissionTo.CreateForumPosts))
		{
			var fileComment = await _db.UserFileComments
				.Include(c => c.UserFile)
				.Include(c => c.User)
				.SingleOrDefaultAsync(u => u.Id == commentId);

			if (fileComment is not null)
			{
				_db.UserFileComments.Remove(fileComment);
				var result = await ConcurrentSave(_db, "Comment deleted", "Unable to delete comment");
				if (result)
				{
					await _publisher.SendUserFile(
						fileComment.UserFile!.Hidden,
						$"User file comment DELETED by {User.Name()}",
						$"[User file]({{0}}) comment DELETED by {User.Name()}",
						$"UserFiles/Info/{fileComment.UserFile.Id}",
						$"{fileComment.UserFile!.Title}");
				}
			}
		}

		return BaseReturnUrlRedirect();
	}
}
