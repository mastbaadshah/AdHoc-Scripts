using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using CreativeFactory.MVC;
using Data.Enumerations.Score;
using Data.Model;
using Data.Model.Cobrand;
using Data.Model.Communications;
using Data.Model.Partners;
using Data.Model.PayWall;
using Data.Model.Permissions;
using Data.Model.Score;
using Data.Model.Subscriptions;
using MyProsperity.Framework.Caching;
using MyProsperity.Framework.Extensions;
using MyProsperity.Framework;
using MyProsperity.Framework.Logging;
using MyProsperity.Resources;

namespace Data
{
    public class Account : IHasID, IHasCobrand
    {
        public static readonly int MAX_CONSEC_FAILURES = ConfigHelper.TryGetOrDefault("MaxConsecYodleeUpdateFailures", 3);
        private static bool _useLocalCacheForCobrand = ConfigHelper.TryGetOrDefault("UseLocalCacheForCobrand", true);   // TODO performance issues serializing CobrandDTO for shared cache

        public Account()
        {
            PlanExpiryDate = DateTime.MaxValue;
            Guid = System.Guid.NewGuid().ToString();
        }

        [Key]
        public int ID { get; set; }

        [MaxLength(256)]
        public string LoginRef { get; set; }

        [MaxLength(128)]
        [Required]
        public string EmailAddress { get; set; }

        public string ActivateToken { get; set; }

        public DateTime LastUpdate { get; set; }

        public string StripeCustomerId { get; set; }

        public DateTime? TimeCapsuleLastRun { get; set; }

        public int PreferenceTargetInternal { get; set; }

        [MaxLength(255)]
        public string Guid { get; set; }

        public Guid? MFASecretkey { get; set; }

        [NotMapped]
        public bool IsMFAOn
        {
            get { return MFASecretkey != null; }
        }

        public bool IsMFAConfirmed { get; set; }

        public bool IsMFAEnabledByAgent { get; set; }

        //public Group  Group { get; set; }

        public virtual ICollection<GroupMember> Members { get; set; }

        #region Profile Members

        [MaxLength(64)]
        [Required]
        public string GroupName { get; set; }

        //[MaxLength(128)]
        //[Required]
        //public string ReferralSourceName { get; set; }

        //[MaxLength(128)]
        //public string ReferralSourceImageToken { get; set; }

        public int? ReferralSourceInternal { get; set; }


        [NotMapped]
        public ReferralSourceItemlist? ReferralSourceItemlist
        {
            get { return (ReferralSourceItemlist?)ReferralSourceInternal; }
            set { ReferralSourceInternal = (int?)value; }
        }

        [MaxLength(128)]
        public string ReferralSourceOther { get; set; }

        public int? SecurityQuestion1Internal { get; set; }

        [NotMapped]
        public SecurityQuestion? SecurityQuestion1
        {
            get { return (SecurityQuestion?)SecurityQuestion1Internal; }
            set { SecurityQuestion1Internal = (int?)value; }
        }

        [MaxLength(128)]
        public string SecurityAnswer1 { get; set; }

        public int? SecurityQuestion2Internal { get; set; }

        [NotMapped]
        public SecurityQuestion? SecurityQuestion2
        {
            get { return (SecurityQuestion?)SecurityQuestion2Internal; }
            set { SecurityQuestion2Internal = (int?)value; }
        }

        [MaxLength(128)]
        public string SecurityAnswer2 { get; set; }

        public string RecoveryEmailAddress { get; set; }

        public int FailedMFAAttemptCount { get; set; }

        #endregion

        public virtual ICollection<AccountAccess> Access { get; set; }

        public virtual ICollection<AccountNotification> AccountNotifications { get; set; }

        public virtual ICollection<TaxPartnerAgent> TaxPartnerAgents { get; set; }

        public virtual ICollection<AccountCobrand> AccountCobrands { get; set; }

        public virtual ICollection<AccountComm> AccountComms { get; set; }

        public virtual ICollection<DiscoverReview> DiscoverReviews { get; set; }

        public virtual ICollection<Bookkeeper> Bookkeepers { get; set; }

        public virtual ICollection<SortedReview> SortedReviews { get; set; }

        public virtual ICollection<FormFillEvent> FormFillEvents { get; set; }

        public virtual ICollection<AccountTaskResult> AccountTaskResults { get; set; }

        public virtual ICollection<Document> ProtectedDocuments { get; set; }

        public virtual ICollection<TermAndCondition> TermsAndConditionsOwned { get; set; }

        public virtual ICollection<TermAndCondition> TermsAndConditionsCreated { get; set; }

        [NotMapped]
        public PreferenceTarget PreferenceTarget
        {
            get { return (PreferenceTarget)PreferenceTargetInternal; }
            set { PreferenceTargetInternal = (int)value; }
        }

        [NotMapped]
        public Cobrand CobrandToUse
        {
            get
            {
                try
                {
                    Cobrand cobrand = null;
                    if (TaxPartnerAgents.AnyAndNotNull())
                    {
                        var partnerAgent = TaxPartnerAgents.SingleOrDefault();
                        if (partnerAgent != null
                        && partnerAgent.Branch != null
                        && partnerAgent.Branch.Partner != null
                        && partnerAgent.Branch.Partner.Cobrand != null)
                        {
                            cobrand = partnerAgent.Branch.Partner.Cobrand;
                        }
                    }

                    if (cobrand == null && AccountCobrands.AnyAndNotNull())
                    {
                        cobrand = AccountCobrands.SingleOrDefault().Cobrand;
                    }

                    return cobrand;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    return null;
                }
            }
        }

        [NotMapped]
        public string AccCommsCode
        {
            get
            {
                return !AccountComms.AnyAndNotNull() ? string.Empty : string.Join("", AccountComms.Select(x => x.ReportCode));
            }
        }

        [NotMapped]
        public bool IsBookkeeper
        {
            get { return Bookkeepers.AnyAndNotNull(); }
        }

        [NotMapped]
        public bool IsPartnerAgent
        {
            get { return TaxPartnerAgents.AnyAndNotNull(); }
        }
        //protected virtual ICollection<TimeCapsule> TimeCapsuleStrucs { get; set; } 

        public virtual AccountSetting AccountSettings { get; set; }

        public bool? IsActive { get; set; }

        public AccountSetting GetAccountSettingCached()
        {
            var cachekey = CacheHelperKeys.GetCacheKeyForAccountSetting_ForAccount(ID);
            AccountSetting accountSetting = (AccountSetting)CacheHelper.GetItemFromCache(cachekey);
            if (accountSetting == null)
            {
                accountSetting = AccountSettings;
                CacheHelper.AddItemToCache(cachekey, accountSetting);
            }

            return accountSetting;
        }

        public Entity GetEntity(bool bypassCache = true)
        {
            return Access
                .Where(aa => aa.IsCreator)
                .Select(aa => aa.Entity)
                .FirstOrDefault();
        }

        //public List<Entity> GetEntities(bool includeNotInTeam = false)
        public List<Entity> GetEntities(bool includeNotInTeam = false, bool includeCompanyEntities = true)
        {
            var groupEntities = Group.Members.Where(
                    m =>
                        m.Entity != null &&
                        (!m.Entity.Relationship.HasValue || m.Entity.Relationship.Value != Relationship.NotListed))
                .Select(m => (Entity)m.Entity).ToList();

            List<Entity> accountEntities = new List<Entity>(groupEntities);
           // accountEntities.Union(groupEntities).ToList();
            if (includeCompanyEntities)
            {
                var companyEntities = Access.Select(a => a.Entity).OfType<CompanyEntity>();
                accountEntities = groupEntities.Union(companyEntities).ToList();
            }

            accountEntities.Insert(0, GetEntity());

            if (includeNotInTeam)
            {
                var nitAccess = Access.FirstOrDefault(aa => aa.Entity != null && aa.Entity.RelationshipInternal != null && (int)aa.Entity.RelationshipInternal == (int)Relationship.NotListed);
                if (nitAccess != null)
                    accountEntities.Add(nitAccess.Entity);
            }

            return accountEntities;
        }

        public GroupMember GetGroupOwnerMember()
        {
            return Members.FirstOrDefault(x => x.IsOwner == true);
        }

        //public Group GetGroup()
        //{
        //    return Members
        //        .Where(aa => aa.Account.EmailAddress == this.EmailAddress);
        //}


        public Entity GetEntity(int entityId)
        {
            var e = Access.Where(x => x.Entity.ID == entityId).Select(aa => aa.Entity).FirstOrDefault();
            return e;
        }

        public string GetAlternateVerifiedEmailAddress()
        {
            return (AccountSettings != null && AccountSettings.AlternateVerifiedEmailAddress != null)
                ? AccountSettings.AlternateVerifiedEmailAddress
                : String.Empty;
        }

        public bool HasAccessTo<T>(IHasOwners<T> item)
            where T : IEntityOwner
        {
            var ids = Access.Select(x => x.Entity.ID).ToList()
                .Union(Group.Members.Where(x => x.Entity != null).Select(x => x.Entity.ID).ToList());
            var ok = item.Owners.Any(x => ids.Contains(x.Entity.ID));
            return ok;
        }

        public bool HasAccessTo(IEnumerable<IEntityOwner> owners)
        {
            var ids = Access.Select(x => x.Entity.ID).ToList()
                .Union(Group.Members.Where(x => x.Entity != null).Select(x => x.Entity.ID).ToList());
            var ok = owners.Any(x => ids.Contains(x.Entity.ID));
            return ok;
        }

        public bool HasAccessTo(IHasOwner item)
        {
            var ok = HasAccessTo(item.Owner);
            return ok;
        }

        public bool HasAccessTo(Entity entity)
        {
            return Access.Any(x => x.Entity.ID == entity.ID);
        }

        public DateTime WealthReview { get; set; }

        public DateTime CashflowReview { get; set; }

        public virtual Group Group { get; set; }

        public DateTime? FirstTimeWizardCompletedDate { get; set; }

        public override string ToString()
        {
            return string.Format("{0}, {1}, {2}", this.ID, this.EmailAddress, this.GroupName);
        }

        public DateTime? DeactivatedDate { get; set; }

        public bool? HasSetBudget { get; set; }

        public virtual AccountTracking AccountTracking { get; set; }

        public DateTime? PlanChangedDate { get; set; }

        //[NotMapped]
        //public string AccountSource
        //{
        //    get
        //    {
        //        var accountCobrand = Cobrands.FirstOrDefault(c => c.IsMain);
        //        return accountCobrand != null ? accountCobrand.Cobrand.Token : null;

        //    }
        //}

        public DateTime? LastLoginDateTime { get; set; }

        public string ReferrerEmail { get; set; }

        public DateTime? LastSnapshotTaken { get; set; }

        #region BatchUpdateFields

        public int? SynchStatusInternal { get; set; }

        [NotMapped]
        public SynchStatusEnum? SynchStatus
        {
            get { return (SynchStatusEnum?)SynchStatusInternal; }
            set { SynchStatusInternal = (int?)value; }
        }


        public DateTime? LastSynchedTime { get; set; }

        public DateTime? CreateDate { get; set; }

        public DateTime? ActivateDate { get; set; }

        public bool? FollowupNotificationSent { get; set; }

        public int ScoreUpdateStatus { get; set; }
        public int CashFlowHistoryUpdateStatus { get; set; }

        #endregion


        [NotMapped]
        public SynchStatusEnum ScoreUpdateStatusEnum
        {
            get { return (SynchStatusEnum)ScoreUpdateStatus; }
            set { ScoreUpdateStatus = (int)value; }
        }

        [NotMapped]
        public SynchStatusEnum CashFlowHistoryUpdateStatusEnum
        {
            get { return (SynchStatusEnum)CashFlowHistoryUpdateStatus; }
            set { CashFlowHistoryUpdateStatus = (int)value; }
        }

        #region ScoreFields

        public decimal Score { get; set; }

        public DateTime? ScoreLastUpdate { get; set; }
        public DateTime? UserPreferencesLastUpdate { get; set; }

        public virtual ICollection<UserScorePreference> UserScorePreferences { get; set; }

        public DateTime? CashFlowHistoryLastUpdate { get; set; }
        public bool? IsScoreVisible { get; set; }

        #endregion

        public bool IsReadyForForceRefreshAfterLogin
        {
            get
            {
                if (this.GetEntity().HasYodleeAccount)
                {
                    GroupMember member =
                        Group.Members.SingleOrDefault(r => r.Account != null && r.Account.EmailAddress == EmailAddress);
                    if (member != null)
                    {
                        if (member.LoginHistory.Count > 1)
                        {
                            LoginHistory history =
                                Enumerable.SingleOrDefault(
                                    member.LoginHistory.OrderByDescending(r => r.LoginDateTime).Skip(1).Take(1));
                            if (history != null && history.LoginDateTime > DateTime.MinValue)
                            {
                                int batchUpdateThresholdDays =
                                    ConfigHelper.TryGetOrDefault("YodleeUpdateLastLoginDaysThreshold", 730);    // two years
                                var minDateToInvokeBatchUpdate = DateTime.Now.AddDays(-batchUpdateThresholdDays);
                                if (history.LoginDateTime < minDateToInvokeBatchUpdate)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
                return false;
            }
        }


        public bool IsReadyToBatchUpdate(DateTime updateDateTimeThreshold, DateTime minLastLoginDate, DateTime dateTimeNow)
        {
            if (LastSynchedTime.HasValue && LastSynchedTime > updateDateTimeThreshold)
                return false;

            if (LastLoginDateTime < minLastLoginDate)
                return false;

            var entity = GetEntity();

            if (entity == null || string.IsNullOrEmpty(entity.YodleeUserName))
                return false;

            if (!AccountYodleeUpdateLogs.IsNullOrEmpty())
            {
                var recentFailuresThreshold = dateTimeNow.AddDays(-1);
                var executionsWithPast24H =
                    AccountYodleeUpdateLogs.Where(t => t.DateTime > recentFailuresThreshold).
                                            OrderByDescending(t => t.DateTime).
                                            ToList();

                return executionsWithPast24H.Count < MAX_CONSEC_FAILURES ||
                       executionsWithPast24H.Take(MAX_CONSEC_FAILURES).Any(t => t.IsSuccess);
            }

            return true;
        }

        public virtual ICollection<AccountYodleeUpdateLog> AccountYodleeUpdateLogs { get; set; }

        public string ActivationUrl { get; set; }

        public DateTime? ActivationEmailSentDt { get; set; }

        public virtual ICollection<AbstractAccountSuggestion> Suggestions { get; set; }


        [NotMapped]
        public bool PaypalIsCancelled
        {
            get { return PaypalCancelDate.HasValue; }
        }

        [NotMapped]
        public bool PaypalIsModified
        {
            get { return PaypalModificationDate.HasValue; }
        }

        public DateTime? PaypalCancelDate { get; set; }

        public DateTime? PaypalModificationDate { get; set; }

        public DateTime? PaypalModificationEffectiveDate { get; set; }

        [NotMapped]
        public SubscriptionLevel? PaypalModificationNewLevel
        {
            get
            {
                return PaypalModificationNewLevelInternal.HasValue
                           ? (SubscriptionLevel?)PaypalModificationNewLevelInternal
                           : null;
            }
            set { PaypalModificationNewLevelInternal = value == null ? null : (int?)value; }
        }

        public int? PaypalModificationNewLevelInternal { get; set; }

        public int PaypalSubscriptionLevelInternal { get; set; }

        [NotMapped]
        public SubscriptionLevel PaypalSubscriptionLevel
        {
            get { return (SubscriptionLevel)PaypalSubscriptionLevelInternal; }
            set { PaypalSubscriptionLevelInternal = (int)value; }
        }

        [NotMapped]
        public bool HasPaypalSubscription
        {
            get { return !string.IsNullOrEmpty(PaypalSubscriptionID); }
        }

        public string PaypalSubscriptionID { get; set; }
        public DateTime? PaypalSubscriptionTs { get; set; }
        public virtual Plan Plan { get; set; }
        public DateTime PlanExpiryDate { get; set; }
        public virtual Plan FallbackPlan { get; set; }

        [NotMapped]
        public PersonEntity Person
        {
            get { return GetEntity() as PersonEntity; }
        }

        [NotMapped]
        public bool IsUnionsMember
        {
            get
            {
                const string ACCT_SRC_UNIONS = "unions";
                const string ACCT_SRC_BLOOM = "bloom";
                var isUnionsMember = this.Matches(ACCT_SRC_UNIONS) || this.Matches(ACCT_SRC_BLOOM);
                return isUnionsMember;
            }
        }

        [NotMapped]
        public bool IsTempGuestAccount
        {
            get { return !EmailAddress.Contains("@"); }
        }

        public bool IsTestAccount()
        {
            return !this.EmailAddress.Contains("@")
                   || this.EmailAddress.EndsWith("@test.com")
                   || this.EmailAddress.EndsWith("@brian.com")
                   || this.EmailAddress.EndsWith("@mailinator.com")
                   || this.EmailAddress.EndsWith("@asdf.com")
                   || this.EmailAddress.EndsWith("@dd.com")
                   || this.EmailAddress.EndsWith("@alba.com")
                   || this.EmailAddress.EndsWith("@dsa.com")
                   || this.EmailAddress.EndsWith("@catwoman.com")
                   || this.EmailAddress.EndsWith("@123123.com")
                   || this.EmailAddress.EndsWith("@1.com")
                   || this.EmailAddress.EndsWith("@mp.com")
                   || this.EmailAddress.EndsWith("@rrttt.com")
                   || this.EmailAddress.EndsWith("@sseeff.com")
                   || this.EmailAddress.EndsWith("@asdf.com")
                   || this.EmailAddress.EndsWith("@shania.com")
                   || this.EmailAddress.EndsWith("@myprosperity.com.au")
                   || this.EmailAddress.EndsWith("@stephenjackel.com")
                   || this.EmailAddress.ToLowerInvariant() == "trevzky@gmail.com";
        }

        public void SetCobrand(Cobrand cobrand, bool isMain = true)
        {
            if (AccountCobrands == null)
                AccountCobrands = new List<AccountCobrand>();

            // first remove any old cobrands
            while (AccountCobrands.Any())
            {
                AccountCobrands.Remove(AccountCobrands.FirstOrDefault());
            }

            CacheHelper.RemoveItemFromCache(CacheHelperKeys.GetCacheKeyForAccountSetting_ForAccount(ID));

            if (cobrand != null)    // null means remove cobrand
            {
                AccountSettings.EmphasiseProperty = cobrand.EmphasiseProperty;
                var accountCobrand = new AccountCobrand { Cobrand = cobrand, IsMain = isMain, IsCurrentClient = true };
                AccountCobrands.Add(accountCobrand);
            }

            CacheHelper.RemoveItemFromCache(CacheHelperKeys.GetCacheKeyForCobrandId_ForAccount(ID));
            CacheHelper.RemoveItemFromCache(CacheHelperKeys.GetCacheKeyForCobrandDTOId_ForAccount(ID), _useLocalCacheForCobrand);
            CacheHelper.RemoveItemFromCache(CacheHelperKeys.GetCacheKeyFor_ActiveCobrandBranchID_ForAccount(ID));
        }



        public Plan GetActivePlan()
        {
            if (Plan == null)
                return null;

            if (IsActivePlanExpired())
                return FallbackPlan;

            return Plan;
        }

        public int GetActivePlanID()
        {
            Plan activePlan = GetActivePlan();
            return activePlan != null ? activePlan.ID : -1;
        }

        public string GetActivePlanName()
        {
            Plan activePlan = GetActivePlan();
            string name;
            if (activePlan != null)
            {
                switch (activePlan.ID)
                {
                    case 1:
                        name = MP.PaywallPlan_Starter;
                        break;
                    case 2:
                        name = MP.PaywallPlan_Pro;
                        break;
                    case 3:
                        name = MP.PaywallPlan_Premium;
                        break;
                    case 4:
                        name = MP.PaywallPlan_Property;
                        break;
                    case 5:
                        name = MP.PaywallPlan_Partner;
                        break;
                    case 6:
                        name = MP.PaywallPlan_PartnerDocSign;
                        break;
                    default:
                        name = string.Empty;
                        break;
                }
            }
            else
            {
                name = string.Empty;
            }
            return name;
        }

        public bool IsActivePlanExpired()
        {
            return Plan != null && PlanExpiryDate < DateTime.Now;
        }

        public virtual ICollection<SNSEndpoint> SnsEndpoints { get; set; }

        [NotMapped]
        public string DisplayName
        {
            get
            {
                var entity = GetEntity();
                const int maxCharacter = 23;
                return FormatHelper.Ellipsify(entity.DisplayName, maxCharacter);
            }
        }

        //public bool HasValidEmailAddress()
        //{
        //    //  todo create regex validator, unit test against existing accounts in DB 
        //    return EmailAddress.Contains("@");
        //}
    }

    public class AccountAccess
    {
        [Key]
        public int ID { get; set; }

        public bool IsCreator { get; set; }
        public virtual Entity Entity { get; set; }
        public virtual Account Account { get; set; }

        [Column("Entity_ID")]
        public int EntityID { get; set; }
    }

    public class AccountYodleeUpdateLog
    {
        public int ID { get; set; }
        public virtual Account Account { get; set; }
        public DateTime DateTime { get; set; }
        public bool IsSuccess { get; set; }
        public int DurationSeconds { get; set; }
    }
}
