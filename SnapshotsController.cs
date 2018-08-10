using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Description;
using MyProsperity.API.Filters;
using MyProsperity.API.Helpers;
using MyProsperity.API.Models;

namespace MyProsperity.API.Controllers
{
    /// <summary>
    /// Provides end points for snapshots.
    /// </summary>
    [Scope("clientuser")]
    public class SnapshotsController : BaseAuthenticatedController
    {
        /// <summary>
        /// Gets a list of monthly snapshots for the current user.
        /// </summary>
        /// <param name="startDate">When provided, filters the returned list by date.</param>
        /// <param name="endDate">When provided, filters the returned list by date.</param>
        /// <returns></returns>
        [HttpGet]
        [Permission(Data.Model.Permissions.Permission.READ_WEALTH)]
        [ResponseType(typeof(List<Snapshot>))]
        public IHttpActionResult MonthlySnapshots(DateTime? startDate = null, DateTime? endDate = null)
        {
            //var account = GetLogInAccount();
            var groupOwner = GetGroupOwnerAccount();

            var snapshots = SnapshotService.GetMonthlySnapshots(groupOwner);
            if (startDate.HasValue)
            {
                snapshots = snapshots.Where(s => s.DateTaken >= startDate.Value.ToLocalTime());
            }
            if (endDate.HasValue)
            {
                // one day is added to the end date to ignore the actual time.
                snapshots = snapshots.Where(s => s.DateTaken <= endDate.Value.ToLocalTime().AddDays(1));
            }

            return Ok(snapshots.ToList().ToApiModel());
        }
    }
}