using System;
using System.Collections.Generic;
using System.Linq;
using Data.Model.Protection;
using MyProsperity.Framework.Model.Enums;

namespace Data.Model.Cobrand
{
    public class CobrandSettings
    {
        public CobrandSettings()
        {
            // Default values
            FreeProTrialPeriodOnJoinDays = 0;
            DiscoverReview = new DiscoverReviewSettings();
            AllowDocSignersWithSameEmail = false;
            ShowInactiveClients = false;
            ShouldSendActivationEmailManualDocSigning = true;
            DataFeeds = new DataFeeds();
            PartnerNotificationSettings = new PartnerNotificationSettings();
            //MobileApps will be initialised when deserializing from xml to avoid duplicate items in the list
            MobileApps = new List<MobileAppSettings>();
            IsEnterprise = false;
            IsCertifiedBookkeeper = false;
            EnableFirmWideMFA = false;
            IsKidsAppEnabled = false;
            WillSettings = new WillSettings();
            GobbillSetting = new GobbillSetting();
            CobrandAuthKey = string.Empty;
        }

        public WillSettings WillSettings { get; set; }
        public GobbillSetting GobbillSetting { get; set; }
        public int FreeProTrialPeriodOnJoinDays { get; set; }
        public DiscoverReviewSettings DiscoverReview { get; set; }
        public bool AllowDocSignersWithSameEmail { get; set; }
        public bool ShouldSendActivationEmailManualDocSigning { get; set; }
        public bool ShowInactiveClients { get; set; }
        public int ToDoReminderInternalForTaxAssistant { get; set; }

        public DateTime? DocSignersWithSameEmailFirstAllowedDate { get; set; }
        public DateTime? DocSignersWithSameEmailMostRecentChangedDate { get; set; }
        public DateTime? ShowInactiveClientsMostRecentChangedDate { get; set; }
        public DateTime? ShowInactiveClientsFirstAllowedDate { get; set; }
        public DataFeeds DataFeeds { get; set; }
        public PartnerNotificationSettings PartnerNotificationSettings { get; set; }
        public List<MobileAppSettings> MobileApps { get; set; }

        public bool IsKidsAppEnabled { get; set; }

        public bool IsEnterprise { get; set; }
        public bool IsCertifiedBookkeeper { get; set; }

        public bool EnableFirmWideMFA { get; set; }

        public string EnableFirmWideMFAByName { get; set; }

        public string EnableFirmWideMFAByEmail { get; set; }

        public DateTime? EnableFirmWideMFADate { get; set; }

        public string ReportCoverImageToken { get; set; }

        public bool ShowReportCoverOverlay { get; set; }

        public bool AppendReportHelpPage { get; set; }

        public string CobrandAuthKey { get; set; }

        public static CobrandSettings CreateCobrandSettings(string cobrandSettingsXml)
        {
            CobrandSettings cobrandSettings = new CobrandSettings();

            if (!string.IsNullOrEmpty(cobrandSettingsXml))
                cobrandSettings = MyProsperity.Framework.Xml.SerializationHelper.Deserialize<CobrandSettings>(cobrandSettingsXml);

            if (cobrandSettings != null)
            {
                foreach (MobilePlatformType platformType in Enum.GetValues(typeof (MobilePlatformType)))
                {
                    if (cobrandSettings.MobileApps.All(a => a.Platform != platformType))
                    {
                        cobrandSettings.MobileApps.Add(new MobileAppSettings(platformType));
                    }
                }
            }

            return cobrandSettings;
        }

        public string GetCobrandSettingsXml()
        {
            return MyProsperity.Framework.Xml.SerializationHelper.Serialize(this);
        }

        public Enumerations.Partners.Enumerations.DiscoverReviewPromptFrequency GetDiscoverSuperPromptFrequency()
        {
            return (Enumerations.Partners.Enumerations.DiscoverReviewPromptFrequency) DiscoverReview.Super.PromptFrequencyInternal;
        }

    }

    public class DataFeeds
    {
        public DataFeeds()
        {
            MyobDataFeedEnabled =
                XeroDataFeedEnabled =
                    ClassDataFeedEnabled =
                        BglDataFeedEnabled =
                            ImplementedPortfoliosDataFeedEnabled =
                                Hub24DataFeedEnabled =
                                    NetWealthFeedEnabled =
                                        ManagedAccountsFeedEnabled =
                                            MasonStevensFeedEnabled =
                                                MacquarieWrapFeedEnabled =
                                                    MacquarieCashFeedEnabled =
                                                        XPlanFeedEnabled =
                                                            PraemiumFeedEnabled = 
                                                                IoofLtsFeedEnabled =
                                                                    IoofMaxFeedEnabled =
                                                                        IoofTasFeedEnabled =
                                                                            MasonStevensRetailFeedEnabled =
                                                                                SharesightFeedEnabled =
                                                                                    WealthO2FeedEnabled = true;
        }

        public bool MyobDataFeedEnabled { get; set; }
        public bool XeroDataFeedEnabled { get; set; }
        public bool ClassDataFeedEnabled { get; set; }
        public bool BglDataFeedEnabled { get; set; }
        public bool ImplementedPortfoliosDataFeedEnabled { get; set; }
        public bool Hub24DataFeedEnabled { get; set; }
        public bool NetWealthFeedEnabled { get; set; }
        public bool ManagedAccountsFeedEnabled { get; set; }
        public bool MasonStevensFeedEnabled { get; set; }
        public bool MasonStevensRetailFeedEnabled { get; set; }
        public bool MacquarieWrapFeedEnabled { get; set; }
        public bool MacquarieCashFeedEnabled { get; set; }
        public bool XPlanFeedEnabled { get; set; }
        public bool PraemiumFeedEnabled { get; set; }
        public bool IoofLtsFeedEnabled { get; set; }
        public bool IoofMaxFeedEnabled { get; set; }
        public bool IoofTasFeedEnabled { get; set; }
        public bool SharesightFeedEnabled { get; set; }
        public bool WealthO2FeedEnabled { get; set; }

    }
}
