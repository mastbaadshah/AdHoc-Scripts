using CreativeFactory.MVC;
using Data;
using Data.Enumerations.Cobrands;
using Data.Model;
using Data.Model.AdviserBilling;
using Data.Model.Cobrand;
using Data.Model.Communications;
using Data.Model.Partners;
using Data.Model.PayWall;
using Data.Model.RealEstate;
using MyProsperity.Resources;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Web.Mvc;

namespace MyProsperity.Web.UI.Admin.Areas.PartnersManagement.Models
{
    public class UpdateClientCobrandModel
    {
        public string AccountEmailAddress { get; set; }

        public Cobrand CurrentCobrand { get; set; }

        public IEnumerable<Cobrand> Cobrands { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class AccountantListModel
    {
        public List<AccountantModel> AccountantModels { get; set; }

        public string Messsage { get; set; }
    }

    public class CobrandEditModel
    {
        public Cobrand Cobrand { get; set; }

        public int SelectedUserPreferencePromptFrequencyInt { get; set; }
        public int SelectedCobrandTypeInt { get; set; }
        public int SelectedCobrandStatusInt { get; set; }

        public string SelectedAgencyBranchID { get; set; }

        public IList<SelectListItem> AgencyBranchSelectors { get; set; }

        public IList<SelectListItem> AdviserBillingSelectors { get; set; }

        public IEnumerable<MailChimpSetting> MailChimpSettings { get; set; }
        public IEnumerable<SelectListItem> Plans { get; set; }
        public IEnumerable<SelectListItem> PlanAddons { get; set; }
        public IEnumerable<SelectListItem> CobrandStatusSelectors { get; set; }
        public IEnumerable<SelectListItem> RegionSettings { get; set; }
        public IList<MobileAppPreferenceVisibilitySetting> MobileAppPreferenceVisibilitySettings { get; set; }

        public string SelectedAdviserBillingID { get; set; }

        [Required(ErrorMessageResourceName = "Required", ErrorMessageResourceType = typeof(MP_ErrorMessages))]
        public string SelectedMailChimpSettingID { get; set; }

        public string SelectedPlanId { get; set; }
        public string SelectedPlanAddonId { get; set; }
        public string SelectedRegionSettingsId { get; set; }
        public CobrandStatus CobrandStatus { get; set; }

        public bool CreateDemo { get; set; }

        public string MagicTags { get; set; }

        public string ZohoId { get; set; }

        //public IEnumerable<Agent> Agent { get; set; }           // future feature - ability to choose which RE agent at a branch should get leads

        public static IList<SelectListItem> ConvertAdviserBillingIntoSelectors(IEnumerable<AdviserBilling> adviserBillings)
        {
            return adviserBillings.Select(adviserBilling => new SelectListItem()
            {
                Text = string.Format("{0} - {1}", adviserBilling.Name, adviserBilling.EmailAddress),
                Value = adviserBilling.ID.ToString(),
            }).OrderBy(x => x.Text).ToList();

        }

        public static IList<SelectListItem> ConvertPlansIntoSelectors(IEnumerable<Plan> plans)
        {
            return plans.Select(plan => new SelectListItem()
            {
                Text = plan.Name,
                Value = plan.ID.ToString(),
            }).OrderBy(x => x.Text).ToList();
        }

        public static IList<SelectListItem> ConvertPlanAddonsIntoSelectors(IEnumerable<PlanAddon> addons)
        {
            return addons.OrderBy(addon => addon.Quantity).Select(addon => new SelectListItem()
            {
                Text = addon.Name,
                Value = addon.Id.ToString(),
            }).ToList();
        }


        public static IList<SelectListItem> ConvertAgencyBranchIntoSelectors(IEnumerable<AgencyBranch> agencyBranches)
        {
            var agencyBranchSelectors = new List<SelectListItem>();
            foreach (var agencyBranch in agencyBranches)
            {
                agencyBranchSelectors.Add(new SelectListItem()
                {
                    Text = GetDescriptionFromAgencyBranch(agencyBranch),
                    Value = agencyBranch.ID.ToString(),
                });
            }
            return agencyBranchSelectors;
        }

        public static string GetDescriptionFromAgencyBranch(AgencyBranch agencyBranch)
        {
            if (agencyBranch == null)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.Append(agencyBranch.Name);
            sb.Append(" ");
            sb.Append(agencyBranch.Agency.Name);
            sb.Append(" ");
            sb.Append(agencyBranch.Suburb);
            sb.Append(" ");
            sb.Append(agencyBranch.State);
            sb.Append(" ");
            sb.Append(agencyBranch.BranchStatus.ToString());

            return sb.ToString();
        }

        public static IList<SelectListItem> ConvertCobrandStatusesIntoSelectors(IEnumerable<CobrandStatus> statuses)
        {
            return statuses.Select(status => new SelectListItem()
            {
                Text = status.GetDescription(),
                Value = status.ToString(),
            }).OrderBy(x => x.Text).ToList();
        }

        public static IList<SelectListItem> ConvertRegionSettingsIntoSelectors(IEnumerable<RegionSettings> regionSettings)
        {
            return regionSettings.Select(r => new SelectListItem()
            {
                Text = r.Name,
                Value = r.ID.ToString(),
            }).ToList();
        }
    }

    public class TaxPartnerAgentModel
    {
        public TaxPartnerAgent TaxPartnerAgent;

        public IEnumerable<NotificationQueue> NotificationQueues;

        public bool HasLoggedIn;
    }

    public class MobileAppPreferenceVisibilitySetting
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public bool IsVisible { get; set; }
    }
}