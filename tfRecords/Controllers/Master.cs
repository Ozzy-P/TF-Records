
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net;
using Dapper;
using Npgsql;
using tfRecord.Models;
using tfRecord.Services;

namespace tfRecord.Controllers;
public class Master : Controller
{
    private readonly ILogger<Master> _logger;
    private readonly NpgsqlConnection _connection;
    private readonly ISecurityService _security;
    private readonly TFRecords _tfRecords;   

    public Master(ILogger<Master> logger, NpgsqlConnection connection, ISecurityService security, TFRecords tfRecords )
    {
        _logger = logger;
        _connection = connection;
        _security = security;
        _tfRecords = tfRecords;
    }

    public async Task<IActionResult> Index()
    {
        await _connection.OpenAsync();
        {
            IEnumerable<MatchIndex> matches = await _connection.QueryAsync<MatchIndex>(_tfRecords.FetchMatch());
            foreach(MatchIndex match in matches)
            {
                match.secureid = _security.Encrypt(match.matchid.ToString());
            }
            return View(matches);
        }
    }

    // LAX
    public async Task<IActionResult> Details(string id)
    {
        // Doesn't matter if it's leaked, not OPSEC for now.
        string rawID;
        try
        {
            rawID = _security.Decrypt(id);
        }
        catch
        {
            return StatusCode(403);
        }
        string decodedID = WebUtility.UrlDecode(rawID);
        if (!_tfRecords.IsValid(decodedID))
        {
            return StatusCode(403);
        }
        MatchDetailsViewModel viewModel = new();

        await _connection.OpenAsync();
        {
            try
            {
                viewModel.Match = await _connection.QueryFirstAsync<MatchEntry>(_tfRecords.QueryMatch(), new { MatchId = decodedID });
                viewModel.Users = await _connection.QueryAsync<MatchUser>(_tfRecords.QueryUser(), new { MatchId = decodedID });
                viewModel.Chats = await _connection.QueryAsync<MatchChat>(_tfRecords.QueryChat(), new { MatchId = decodedID });
                viewModel.Kills = await _connection.QueryAsync<MatchKill>(_tfRecords.QueryScore(), new { MatchId = decodedID });
            }
            catch (Exception err)
            {
                // Stop setting the PC on fire please.. (aka you know what to do.)
                // Debug.WriteLine(error);
                Func<Exception, bool>? x = _tfRecords.Administration().GetValueOrDefault("Log");
                _ = x != null && x(err).Equals(true);
                viewModel.Users = null;
            }

            if (viewModel.Match == null || viewModel.Users == null)
            {
                return StatusCode(500);
            }

            // A (Not Here): Query -> ON UPDATE -> All FKey
            // B (  Here  ): Query -> QueryGDPR -> Masked
            foreach (MatchUser user in viewModel.Users)
            {
                if (await _tfRecords.QueryGDPR(user.steamid))
                {
                    user.steamid = _tfRecords.defaultUser.Item1;
                    user.username = _tfRecords.defaultUser.Item2;
                }
            }

            if(viewModel.Kills != null) {
                // Not schizo if you don't take the name(s) at face value
                foreach(MatchKill kills in viewModel.Kills)
                {
                    kills.killerid = await _tfRecords.QueryGDPR(kills.killerid) ? _tfRecords.defaultUser.Item1 : kills.killerid;
                    kills.victimid = await _tfRecords.QueryGDPR(kills.victimid) ? _tfRecords.defaultUser.Item1 : kills.victimid;
                }
            }
            return View(viewModel);
        }
    }

    // Honestly don't know, don't care, only surprises here.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
