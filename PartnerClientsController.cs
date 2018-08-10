using System;
using System.Web.Http;
using System.Web.Http.Description;
using MyProsperity.API.Filters;
using MyProsperity.API.Helpers;
using MyProsperity.API.Models;
using MyProsperity.API.Models.Partner;
using MyProsperity.Framework.Logging;

namespace MyProsperity.API.Controllers
{
    /// <summary>
    /// Provides end points for a partner's clients.
    /// </summary>
    [Scope("partneruser")]
    [PartnerAgentOnly]
    public class PartnerClientsController : BaseAuthenticatedController
    {
        [HttpPost]
        [ApiExplorerSettings(IgnoreApi = true)]
        public IHttpActionResult UploadClientList([FromBody]UploadClientCsvModel model)
        {
            try
            {
                var result = PartnerClientService.UploadClientList(model.ClientCsvList, model.BranchId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return InternalServerError();
            }
        }

        [HttpPost]
        [ApiExplorerSettings(IgnoreApi = true)]
        public IHttpActionResult UploadClientEntityList([FromBody]UploadClientEntityCsvModel model)
        {
            try
            {
                var result = PartnerClientService.UploadClientEntityList(model.ClientEntityCsvList, model.BranchId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return InternalServerError();
            }
        }


        /// <summary>
        /// Gets the list of clients for the current partner user.
        /// </summary>
        /// <param name="page">When provided, specifies the returned page number.</param>
        /// <param name="pageSize">When provided, specifies the returned page size.</param>
        /// <param name="orderBy">When provided, overrides the default sorting field. Default value is "CreateDate". To be used with paging.</param>
        /// <param name="ascending">When provided, overrides the default sorting order. The default is descending. To be used with paging.</param>
        /// <returns></returns>
        [ResponseType(typeof(PagedResults<PortalClient>))]
        [HttpGet]
        public IHttpActionResult Clients(int? page = null, int pageSize = 10, string orderBy = "CreateDate", bool ascending = false)
        {
            var account = GetLogInAccount();
            var clientlist = PartnerClientCompleteService.GetPartnerClientsCompleteView(account);
            if (!page.HasValue)
            {
                var portalClientsModel = clientlist.ToApiModel(typeof(PortalClient));
                return Ok(portalClientsModel);
            }
            else
            {
                try
                {
                    var portalClientsModel = clientlist.ToPagedApiModel(page.Value, pageSize, orderBy, ascending, typeof(PortalClient));
                    return Ok(portalClientsModel);
                }
                catch (ArgumentException e)
                {
                    ExceptionHelper.Log(e);
                    ModelState.AddModelError(e.ParamName, e.Message);
                    return BadRequest(ModelState);
                }
            }
        }
    }
}
