﻿using Microsoft.EntityFrameworkCore;
using TASVideos.Data;
using TASVideos.Data.Entity.Game;

namespace TASVideos.Core.Services;

public enum SystemEditResult { Success, Fail, NotFound, DuplicateCode, DuplicateId }
public enum SystemDeleteResult { Success, Fail, NotFound, InUse }

public interface IGameSystemService
{
	ValueTask<ICollection<SystemsResponse>> GetAll();
	ValueTask<SystemsResponse?> GetById(int id);
	Task<bool> InUse(int id);
	ValueTask<int> NextId();
	Task<SystemEditResult> Add(int id, string code, string displayName);
	Task<SystemEditResult> Edit(int id, string code, string displayName);
	Task<SystemDeleteResult> Delete(int id);
}

internal class GameSystemService : IGameSystemService
{
	internal const string SystemsKey = "AllSystems";
	private readonly ApplicationDbContext _db;
	private readonly ICacheService _cache;

	public GameSystemService(ApplicationDbContext db, ICacheService cache)
	{
		_db = db;
		_cache = cache;
	}

	public async ValueTask<ICollection<SystemsResponse>> GetAll()
	{
		if (_cache.TryGetValue(SystemsKey, out List<SystemsResponse> systems))
		{
			return systems;
		}

		systems = await _db.GameSystems
			.Select(s => new SystemsResponse
			{
				Id = s.Id,
				Code = s.Code,
				DisplayName = s.DisplayName,
				SystemFrameRates = s.SystemFrameRates.Select(sf => new SystemsResponse.FrameRates
				{
					FrameRate = sf.FrameRate,
					RegionCode = sf.RegionCode,
					Preliminary = sf.Preliminary,
					Obsolete = sf.Obsolete
				})
			})
			.ToListAsync();
		_cache.Set(SystemsKey, systems);
		return systems;
	}

	public async ValueTask<SystemsResponse?> GetById(int id)
	{
		var systems = await GetAll();
		return systems.SingleOrDefault(s => s.Id == id);
	}

	public async Task<bool> InUse(int id)
	{
		if (await _db.GameVersions.AnyAsync(r => r.SystemId == id))
		{
			return true;
		}

		if (await _db.Publications.AnyAsync(p => p.SystemId == id))
		{
			return true;
		}

		if (await _db.Submissions.AnyAsync(s => s.SystemId == id))
		{
			return true;
		}

		if (await _db.UserFiles.AnyAsync(uf => uf.SystemId == id))
		{
			return true;
		}

		return false;
	}

	public async ValueTask<int> NextId()
	{
		var systems = await GetAll();
		if (systems.Any())
		{
			return systems.Max(s => s.Id) + 1;
		}

		return 0;
	}

	public async Task<SystemEditResult> Add(int id, string code, string displayName)
	{
		var system = await _db.GameSystems.SingleOrDefaultAsync(s => s.Id == id);
		if (system is not null)
		{
			return SystemEditResult.DuplicateId;
		}

		_db.GameSystems.Add(new GameSystem
		{
			Id = id,
			Code = code,
			DisplayName = displayName
		});

		try
		{
			await _db.SaveChangesAsync();
			_cache.Remove(SystemsKey);
			return SystemEditResult.Success;
		}
		catch (DbUpdateConcurrencyException)
		{
			return SystemEditResult.Fail;
		}
		catch (DbUpdateException ex)
		{
			if (ex.InnerException?.Message.Contains("unique constraint") ?? false)
			{
				return SystemEditResult.DuplicateCode;
			}

			return SystemEditResult.Fail;
		}
	}

	public async Task<SystemEditResult> Edit(int id, string code, string displayName)
	{
		var system = await _db.GameSystems.SingleOrDefaultAsync(s => s.Id == id);
		if (system is null)
		{
			return SystemEditResult.NotFound;
		}

		system.Code = code;
		system.DisplayName = displayName;

		try
		{
			await _db.SaveChangesAsync();
			_cache.Remove(SystemsKey);
			return SystemEditResult.Success;
		}
		catch (DbUpdateConcurrencyException)
		{
			return SystemEditResult.Fail;
		}
		catch (DbUpdateException ex)
		{
			if (ex.InnerException?.Message.Contains("unique constraint") ?? false)
			{
				return SystemEditResult.DuplicateCode;
			}

			return SystemEditResult.Fail;
		}
	}

	public async Task<SystemDeleteResult> Delete(int id)
	{
		if (await InUse(id))
		{
			return SystemDeleteResult.InUse;
		}

		try
		{
			var system = await _db.GameSystems.SingleOrDefaultAsync(t => t.Id == id);
			if (system is null)
			{
				return SystemDeleteResult.NotFound;
			}

			_db.GameSystems.Remove(system);
			await _db.SaveChangesAsync();
			_cache.Remove(SystemsKey);
		}
		catch (DbUpdateConcurrencyException)
		{
			return SystemDeleteResult.Fail;
		}

		return SystemDeleteResult.Success;
	}
}