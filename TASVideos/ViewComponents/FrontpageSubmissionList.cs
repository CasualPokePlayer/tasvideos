﻿using Microsoft.AspNetCore.Mvc;
using TASVideos.Data;
using TASVideos.Data.Entity;
using TASVideos.Pages.Submissions.Models;
using TASVideos.WikiEngine;

namespace TASVideos.ViewComponents;

[WikiModule(WikiModules.FrontpageSubmissionList)]
public class FrontpageSubmissionList : ViewComponent
{
	private readonly ApplicationDbContext _db;

	public FrontpageSubmissionList(ApplicationDbContext db)
	{
		_db = db;
	}

	public async Task<IViewComponentResult> InvokeAsync(int? limit)
	{
		// Legacy system supported a max days value, which isn't easily translated to the current filtering
		// However, we currently have it set to 365 which greatly exceeds any max number
		// And submissions are frequent enough to not worry about too stale submissions showing up on the front page
		var request = new SubmissionSearchRequest();

		var subs = await _db.Submissions
			.ThatAreActive()
			.FilterBy(request)
			.ByMostRecent()
			.Take(limit ?? 5)
			.ToSubListEntry()
			.ToListAsync();

		return View(subs);
	}
}
