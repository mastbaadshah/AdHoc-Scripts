using CreativeFactory.MVC;
using Data.Enumerations.Cobrands;
using Data.Enumerations.PayWall;
using Data.Model.Communications;
using Data.Model.DocumentSigning;
using Data.Model.PayWall;
using Data.Model.Permissions;
using Data.Model.Questions;
using Data.Model.RealEstate;
using MyProsperity.Framework;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using System.Linq;

namespace Data.Model.Cobrand
{
    public class Cobrand : EntityHasId
    {
        public Cobrand()
        {
            // set default mandatory fields - are these needed? // TODO Steve
            //DesktopSiteUrl = "https://myprosperity.com.au";
            //MobileSiteUrl = "https://myprosperity.com.au/m";
            //NotificationBaseUrl = "https://myprosperity.com.au/";
            Guid = System.Guid.NewGuid().ToString();
        }


        public DateTime? CreateDate { get; set; }

        [JsonIgnore]
        protected virtual Cobrand Parent { get; set; }

        [Required]
        [DisplayName("Cobrand")]
        public string Token { get; set; }

        [JsonIgnore]
        public virtual ICollection<AccountCobrand> Accounts { get; set; }

        [JsonIgnore]
        public virtual ICollection<Feature> Features { get; set; }

        [JsonIgnore]
        public virtual ICollection<CobrandComm> CobrandComms { get; set; }

        [JsonIgnore]
        public virtual ICollection<CobrandFormFillSetting> CobrandFormFillSettings { get; set; }

        [NotMapped]
        public string CobrandCommsCode
        {
            get
            {
                return !CobrandComms.AnyAndNotNull()
                    ? string.Empty
                    : string.Join("", CobrandComms.Select(x => x.ReportCode));
            }
        }

        [NotMapped]
        [JsonIgnore]
        public string UrlToken
        {
            get { return Token + "_" + Guid; }
        }

        [JsonIgnore]
        public virtual ICollection<DocumentSigningProvider> DocumentSigningProviders { get; set; }

        [JsonIgnore]
        public bool RequirePhoneToRegister { get; set; }

        [JsonIgnore]
        public bool EmphasiseProperty { get; set; }

        [JsonIgnore]
        public int? CobrandTypeInternal { get; set; }

        [JsonIgnore]
        public int CobrandStatusInternal { get; set; }

        [JsonIgnore]
        public string SupportEmailAddress { get; set; }

        [JsonIgnore]
        public string EmailSignature { get; set; }

        [JsonIgnore]
        public string DisplayName { get; set; }

        [JsonIgnore]
        public string HomeUrl { get; set; }

        [JsonIgnore]
        public string CompanyName { get; set; }

        [JsonIgnore]
        public bool ManageUpgrade { get; set; }

        [JsonIgnore]
        public bool WhiteHeader { get; set; }

        [JsonIgnore]
        public bool ShowRetirementGapCalc { get; set; }

        [JsonIgnore]
        public bool KeepAddressPrivate { get; set; }

        [JsonIgnore]
        public bool IsActive { get; set; }

        [JsonIgnore]
        public bool AutoUpgrade { get; set; }
        [JsonIgnore]
        public bool SkipPlanPage { get; set; }

        [JsonIgnore]
        public bool UseSecureRedirect { get; set; }

        [JsonIgnore]
        public bool FormFillEnabled { get; set; }

        public virtual RegionSettings RegionSettings { get; set; }
        //public virtual ICollection<DirectoryCategory> DirectoryCategories { get; set; }
        public virtual ICollection<CobrandDirectory> CobrandDirectories { get; set; }



        //public virtual Plan Plan { get; set; }
        public virtual Subscription Subscription { get; set; }

        /// <summary>
        /// domain name with protocol and no trailing slash eg "myprosperity.com.au" or "staging2.myprosperity.com.au"
        /// </summary>
        [JsonIgnore]
        public string DomainName { get; set; }

        [JsonIgnore]
        public string DesktopSiteUrl
        {
            get { return "https://" + DomainName; }
        }

        [JsonIgnore]
        public string MobileSiteUrl
        {
            get { return "https://" + DomainName + "/m"; }
        }

        [JsonIgnore]
        public string NotificationBaseUrl
        {
            get { return "https://" + DomainName + "/"; }
        }

        [JsonIgnore]
        public string FromEmailAddress { get; set; }

        [JsonIgnore]
        public bool CobrandAdsEnabled { get; set; }

        [NotMapped]
        [JsonIgnore]
        public string FromName
        {
            get { return CompanyName; }
            set { CompanyName = value; }
        }

        [NotMapped]
        [JsonIgnore]
        public CobrandType CobrandType
        {
            get { return CobrandTypeInternal.HasValue ? (CobrandType)CobrandTypeInternal : CobrandType.Default; }
            set { CobrandTypeInternal = (int)value; }
        }

        [NotMapped]
        [JsonIgnore]
        public CobrandStatus CobrandStatus
        {
            get { return (CobrandStatus)CobrandStatusInternal; }
            set { CobrandStatusInternal = (int)value; }
        }

        [NotMapped]
        [JsonIgnore]
        public IEnumerable<AccessType> FeatureTypes
        {
            get { return Features.Select(f => f.FeatureType).ToList(); }
        }

        [JsonIgnore]
        protected virtual ICollection<Cobrand> Children { get; set; }

        [JsonIgnore]
        public virtual ICollection<CobrandBranch> CobrandBranches { get; set; }

        [JsonIgnore]
        public virtual ICollection<CobrandAd> CobrandAds { get; set; }

        [JsonIgnore]
        public virtual ICollection<CobrandReport> CobrandReports { get; set; }

        [JsonIgnore]
        public virtual ICollection<CobrandTransactionCategoryGroup> CobrandTransactionCategoryGroups { get; set; }

        [NotMapped]
        public string HeaderImagePath { get; set; }

        [NotMapped]
        public string IconImagePath { get; set; }

        [NotMapped]
        public string BookMarkIconImagePath { get; set; }

        [JsonIgnore]
        public bool HidePricingReg { get; set; }

        [JsonIgnore]
        public bool HideFeaturesReg { get; set; }

        [JsonIgnore]
        public bool HideRegistration { get; set; }

        [NotMapped]
        public string BackgroundImgRegPath { get; set; }

        [NotMapped]
        public string HighlightImgRegPath { get; set; }

        [NotMapped]
        public DocumentSigningProvider DocumentSigningProviderGetFirstActive
        {
            get
            {
                if (!DocumentSigningProviders.AnyAndNotNull())
                    return null;
                // Get the most recently added one for this cobrand
                return DocumentSigningProviders.Where(x => x.IsEslDocSigningActive)
                    .OrderByDescending(x => x.EslCreateDate)
                    .FirstOrDefault();
            }
        }

        [JsonIgnore]
        public string SubTitleReg { get; set; }

        [JsonIgnore]
        public virtual AdviserBilling.AdviserBilling AdviserBilling { get; set; }

        [JsonIgnore]
        public virtual AdviserJoinDetail AdviserJoinDetail { get; set; }

        public virtual MailChimpSetting MailChimpSetting { get; set; }

        [JsonIgnore]
        public int UserPreferencePromptFrequencyInternal { get; set; }

        [JsonIgnore]
        public string PaymentRedirectUrl { get; set; }

        [JsonIgnore]
        [MaxLength(255)]
        public string Guid { get; set; }

        [JsonIgnore]
        public string CobrandSettingsXml { get; set; }

        [JsonIgnore]
        public int DashboardCtaInternal { get; set; }

        [JsonIgnore]
        public virtual ICollection<DefaultAccessRight> DefaultAccessRights { get; set; }

        /// <summary>
        /// The purpose of this field is for random stuff, initially for internal reporting. Values are comma separated
        /// </summary>
        [JsonIgnore]
        [MaxLength(255)]
        public string MagicTags { get; set; }

        public List<string> GetMagicTags()
        {
            if (MagicTags == null)
                return new List<string>();

            var magicTags = MagicTags.Split(',').ToList();

            return magicTags;
        }

        [NotMapped]
        [JsonIgnore]
        public Enumerations.Partners.Enumerations.DashboardCta DashboardCta
        {
            get { return (Enumerations.Partners.Enumerations.DashboardCta)DashboardCtaInternal; }
            set { DashboardCtaInternal = (int)value; }
        }

        [NotMapped]
        public CobrandSettings _CobrandSettings { get; set; }

        [NotMapped]
        [JsonIgnore]
        public CobrandSettings CobrandSettings
        {
            get
            {
                if (_CobrandSettings != null)
                    return _CobrandSettings;

                _CobrandSettings = CobrandSettings.CreateCobrandSettings(CobrandSettingsXml);

                return _CobrandSettings;
            }
            set { _CobrandSettings = value; }
        }

        public DateTime? SnapshotProcessedDate { get; set; }

        public void UpdateCobrandSettingsXmlFromCobrandSettings()
        {
            CobrandSettingsXml = CobrandSettings.GetCobrandSettingsXml();
            _CobrandSettings = null;
        }
        
        [NotMapped]
        [JsonIgnore]
        public Enumerations.Partners.Enumerations.UserPreferencePromptFrequency UserPreferencePromptFrequency
        {
            get { return (Enumerations.Partners.Enumerations.UserPreferencePromptFrequency)UserPreferencePromptFrequencyInternal; }
            set { UserPreferencePromptFrequencyInternal = (int)value; }
        }

        public void ClearParent()
        {
            Parent = null;
        }


        public void SetParent(Cobrand parent)
        {
            if (parent == null)
                throw new NullReferenceException("@parent is null. Use clear parent method instead!");
            if (parent == this || parent.AllParents.Any(p => p == this))
                throw new Exception("Cannot set self as a parent or passed cobrand has this object as a parent");
            else if (parent.SelfAndChildren.Any(c => c == this))
                throw new Exception("@parent is already among children of this cobrand!");
            Parent = parent;
        }

        [NotMapped]
        [JsonIgnore]
        public IEnumerable<Cobrand> AllParents
        {
            get
            {
                if (Parent == null)
                {
                    return new List<Cobrand>();
                }

                var parents = new List<Cobrand> { Parent };
                return parents.Concat(Parent.AllParents);
            }
        }

        [NotMapped]
        [JsonIgnore]
        public IEnumerable<Cobrand> SelfAndChildren
        {
            get
            {
                var selfAndChildren = new List<Cobrand> { this };

                foreach (var child in Children)
                {
                    selfAndChildren.AddRange(child.SelfAndChildren);
                }
                return selfAndChildren;
            }
        }

        public bool IsDocSigningActive()
        {
            return DocumentSigningProviderGetFirstActive != null;
        }

        public bool IsMemberPlan()
        {
            return Subscription == null || Subscription.Plan == null || Subscription.Plan.ID == (int)MpPlanType.MpPartnerMember;
        }

        public virtual ICollection<IntegrationVault> IntegrationVaults { get; set; }

        public virtual ICollection<CobrandTaskResult> CobrandTaskResults { get; set; }

        public virtual ICollection<QuestionPreference> QuestionPreferences { get; set; }

        internal class CobrandEfMapping : EntityTypeConfiguration<Cobrand>
        {
            public CobrandEfMapping()
            {
                HasOptional(t => t.Parent).
                    WithMany(t => t.Children).
                    WillCascadeOnDelete();

                HasMany(t => t.Children);
                HasMany(t => t.Features).WithMany(f => f.Cobrands);
            }
        }


        //to sync freshly loaded cobrand with updated model.
        public static Cobrand SyncCobrand(Cobrand original, Cobrand updated)
        {
            original.BackgroundImgRegPath = updated.BackgroundImgRegPath;
            original.BookMarkIconImagePath = updated.BookMarkIconImagePath;
            original.CobrandType = updated.CobrandType;
            original.CobrandTypeInternal = updated.CobrandTypeInternal;
            original.CompanyName = updated.CompanyName;

            original.DisplayName = updated.DisplayName;
            original.DomainName = updated.DomainName;
            original.EmailSignature = updated.EmailSignature;
            original.EmphasiseProperty = updated.EmphasiseProperty;
            original.FromEmailAddress = updated.FromEmailAddress;
            original.FromName = updated.FromName;
            original.HeaderImagePath = updated.HeaderImagePath;
            original.HideFeaturesReg = updated.HideFeaturesReg;
            original.HidePricingReg = updated.HidePricingReg;
            original.HideRegistration = updated.HideRegistration;
            original.HighlightImgRegPath = updated.HighlightImgRegPath;
            original.HomeUrl = updated.HomeUrl;
            original.IconImagePath = updated.IconImagePath;
            original.ManageUpgrade = updated.ManageUpgrade;
            original.AutoUpgrade = updated.AutoUpgrade;
            original.SkipPlanPage = updated.SkipPlanPage;
            original.FormFillEnabled = updated.FormFillEnabled;
            original.UseSecureRedirect = updated.UseSecureRedirect;
            original.RequirePhoneToRegister = updated.RequirePhoneToRegister;
            original.ShowRetirementGapCalc = updated.ShowRetirementGapCalc;
            original.SubTitleReg = updated.SubTitleReg;
            original.SupportEmailAddress = updated.SupportEmailAddress;
            original.Token = updated.Token;
            original.WhiteHeader = updated.WhiteHeader;
            original.CobrandAdsEnabled = updated.CobrandAdsEnabled;
            original.IsActive = updated.IsActive;
            original.KeepAddressPrivate = updated.KeepAddressPrivate;
            original.UserPreferencePromptFrequencyInternal = updated.UserPreferencePromptFrequencyInternal;
            original.PaymentRedirectUrl = updated.PaymentRedirectUrl;
            original.CobrandStatus = updated.CobrandStatus;
            original.CobrandStatusInternal = updated.CobrandStatusInternal;
            original.MagicTags = updated.MagicTags.IsNullOrEmpty() ? null : updated.MagicTags;
            original.CobrandSettings.DataFeeds.MyobDataFeedEnabled = updated.CobrandSettings.DataFeeds.MyobDataFeedEnabled;
            original.CobrandSettings.DataFeeds.XeroDataFeedEnabled = updated.CobrandSettings.DataFeeds.XeroDataFeedEnabled;
            original.CobrandSettings.DataFeeds.ClassDataFeedEnabled = updated.CobrandSettings.DataFeeds.ClassDataFeedEnabled;
            original.CobrandSettings.DataFeeds.BglDataFeedEnabled = updated.CobrandSettings.DataFeeds.BglDataFeedEnabled;
            original.CobrandSettings.DataFeeds.ImplementedPortfoliosDataFeedEnabled = updated.CobrandSettings.DataFeeds.ImplementedPortfoliosDataFeedEnabled;
            original.CobrandSettings.DataFeeds.Hub24DataFeedEnabled = updated.CobrandSettings.DataFeeds.Hub24DataFeedEnabled;
            original.CobrandSettings.DataFeeds.ManagedAccountsFeedEnabled = updated.CobrandSettings.DataFeeds.ManagedAccountsFeedEnabled;
            original.CobrandSettings.DataFeeds.NetWealthFeedEnabled = updated.CobrandSettings.DataFeeds.NetWealthFeedEnabled;
            original.CobrandSettings.DataFeeds.MasonStevensFeedEnabled = updated.CobrandSettings.DataFeeds.MasonStevensFeedEnabled;
            original.CobrandSettings.DataFeeds.MacquarieWrapFeedEnabled = updated.CobrandSettings.DataFeeds.MacquarieWrapFeedEnabled;
            original.CobrandSettings.DataFeeds.MacquarieCashFeedEnabled = updated.CobrandSettings.DataFeeds.MacquarieCashFeedEnabled;
            original.CobrandSettings.DataFeeds.PraemiumFeedEnabled = updated.CobrandSettings.DataFeeds.PraemiumFeedEnabled;
            original.CobrandSettings.DataFeeds.XPlanFeedEnabled = updated.CobrandSettings.DataFeeds.XPlanFeedEnabled;
            original.CobrandSettings.DataFeeds.IoofLtsFeedEnabled = updated.CobrandSettings.DataFeeds.IoofLtsFeedEnabled;
            original.CobrandSettings.DataFeeds.IoofMaxFeedEnabled = updated.CobrandSettings.DataFeeds.IoofMaxFeedEnabled;
            original.CobrandSettings.DataFeeds.IoofTasFeedEnabled = updated.CobrandSettings.DataFeeds.IoofTasFeedEnabled;
            original.CobrandSettings.DataFeeds.MasonStevensRetailFeedEnabled = updated.CobrandSettings.DataFeeds.MasonStevensRetailFeedEnabled;
            original.CobrandSettings.DataFeeds.SharesightFeedEnabled = updated.CobrandSettings.DataFeeds.SharesightFeedEnabled;
            original.CobrandSettings.DataFeeds.WealthO2FeedEnabled = updated.CobrandSettings.DataFeeds.WealthO2FeedEnabled;
            original.CobrandSettings.IsCertifiedBookkeeper = updated.CobrandSettings.IsCertifiedBookkeeper;
            original.CobrandSettings.CobrandAuthKey = updated.CobrandSettings.CobrandAuthKey;
            //original.Plan = updated.Plan;
            if (original.Subscription == null)
            {
                original.Subscription = updated.Subscription;
            }
            else
            {
                original.Subscription.Plan = updated.Subscription.Plan;
                original.Subscription.PlanStartDate = updated.Subscription.PlanStartDate;
            }
            original.CobrandSettings.MobileApps = updated.CobrandSettings.MobileApps;
            original.CobrandSettings.IsEnterprise = updated.CobrandSettings.IsEnterprise;
            original.CobrandSettings.IsKidsAppEnabled = updated.CobrandSettings.IsKidsAppEnabled;
            original.CobrandSettings.WillSettings.IsWillGenerationAllowed = updated.CobrandSettings.WillSettings.IsWillGenerationAllowed;
            original.CobrandSettings.GobbillSetting.IsGobbillEnabled = updated.CobrandSettings.GobbillSetting.IsGobbillEnabled;
            //original.DesktopSiteUrl = updated.DesktopSiteUrl;
            //original.MobileSiteUrl = updated.MobileSiteUrl;
            //original.NotificationBaseUrl = updated.NotificationBaseUrl;
            //original.AdviserBilling = null;
            //original.Accounts = updated.Accounts;
            //original.AdviserBilling = updated.AdviserBilling;
            //original.AllParents = updated.AllParents;
            //original.Children = updated.Children;
            //original.CobrandBranches = updated.CobrandBranches;
            //original.FeatureTypes = updated.FeatureTypes;
            //original.Features = updated.Features;
            //original.Parent = updated.Parent;
            //original.SelfAndChildren = updated.SelfAndChildren;
            return original;
        }

        public static string GetCommsGeneratedCode(Cobrand cobrand, Account account)
        {
            if (cobrand == null || !cobrand.CobrandComms.AnyAndNotNull())
                return string.Empty;

            var cobrandCommsIds = cobrand.CobrandComms.Where(x => x.Enabled).Select(y => y.CommsController.ID).ToList();

            if (!cobrandCommsIds.AnyAndNotNull())
                return string.Empty;

            var accCommsCode = account.AccountComms.Where(x => x.Enabled && cobrandCommsIds.Contains(x.CommsController.ID)).Select(y => y.ReportCode).ToList();

            return !accCommsCode.AnyAndNotNull() ? string.Empty : string.Join("", accCommsCode);
        }

        public bool ShouldPromptForUserPreferences(DateTime? userPreferencesLastUpdate)
        {
            if (!userPreferencesLastUpdate.HasValue)
                return true;

            var result = false;

            switch (UserPreferencePromptFrequency)
            {
                case Enumerations.Partners.Enumerations.UserPreferencePromptFrequency.Always:
                    result = true;
                    break;
                case Enumerations.Partners.Enumerations.UserPreferencePromptFrequency.TwoWeeks:
                    result = userPreferencesLastUpdate.Value <= DateTime.Now.AddDays(-14);
                    break;
                case Enumerations.Partners.Enumerations.UserPreferencePromptFrequency.OneMonth:
                    result = userPreferencesLastUpdate.Value <= DateTime.Now.AddDays(-30);
                    break;
                case Enumerations.Partners.Enumerations.UserPreferencePromptFrequency.ThreeMonths:
                    result = userPreferencesLastUpdate.Value <= DateTime.Now.AddDays(-91);
                    break;
                case Enumerations.Partners.Enumerations.UserPreferencePromptFrequency.SixMonths:
                    result = userPreferencesLastUpdate.Value <= DateTime.Now.AddDays(-182);
                    break;
                case Enumerations.Partners.Enumerations.UserPreferencePromptFrequency.NineMonths:
                    result = userPreferencesLastUpdate.Value <= DateTime.Now.AddDays(-273);
                    break;
                case Enumerations.Partners.Enumerations.UserPreferencePromptFrequency.OneYear:
                    result = userPreferencesLastUpdate.Value <= DateTime.Now.AddDays(-365);
                    break;
                case Enumerations.Partners.Enumerations.UserPreferencePromptFrequency.Never:
                    result = false;
                    break;
            }

            return result;
        }

    }
}