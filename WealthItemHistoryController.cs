using System;
using System.Collections.Generic;
using System.Web.Http;
using System.Web.Http.Description;
using MyProsperity.API.Filters;
using MyProsperity.API.Models;

namespace MyProsperity.API.Controllers
{
    /// <summary>
    /// Provides end points for wealth items history.
    /// </summary>
    [ApiExplorerSettings(IgnoreApi = true)]
    [Scope("clientuser")]
    public class WealthItemHistoryController : BaseAuthenticatedController
    {
        /// <summary>
        /// Gets a list of monthly history points for a given wealth item.
        /// </summary>
        /// <param name="wealthItemId">The wealth item's ID.</param>
        /// <param name="startDate">When provided, filters the returned list by date.</param>
        /// <param name="endDate">When provided, filters the returned list by date.</param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        [HttpGet]
        [Permission(Data.Model.Permissions.Permission.READ_WEALTH)]
        [ResponseType(typeof(List<WealthItemHistory>))]
        public IHttpActionResult MonthlyWealthItemHistory(int wealthItemId, DateTime? startDate = null, DateTime? endDate = null)
        {
            throw new NotImplementedException();
        }
    }
}