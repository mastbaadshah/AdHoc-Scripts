using Data;
using Data.Model;
using Data.Model.Cobrand;
using Data.Model.Partners;
using Data.Model.Permissions;
using Data.Services.Accounts;
using System;
using System.Collections.Generic;
using Data.Enumerations.PayWall;
using MyProsperity.DTO;
using Data.Model.Yodlee;

namespace MyProsperity.Business.Interfaces
{
    public interface IAccountService
    {
        IMembershipService MembershipService { get; set; }
        //todo: @Mat refactor to 1 function instead of three different
        Account Get(int accountId, bool bypassCache = true);
        Account Get(int? accountId);
        Account GetAccount(int accountID);
        Account GetAccount(string loginRef);
        IEnumerable<Account> GetAccounts(IList<int> accountIds);
        Account GetAccountByEmailAddress(string email);
        Account GetAccountByEmailAddressOrYodleeEmailAddress(string email);
        Account GetAccountByGuid(Guid guid);
        AccountAccess GetAccountAccess(int accountAccessId);
        Account GetCreatorAccount(Entity entity);
        Account GetGroupAccount(Account logInAccount);
        Group GetGroup(int groupID);
        int? AddAccountAccess(Account account, Entity entity);
        void DeleteEntityAccount(Entity entity, bool skipSave = false);

        Group GetAccountOwnerGroup();

        bool UpdateAccount(Account account, string oldPassword, string newPassword);

        Account ChangePrimaryEmailAddress(string currentEmailAddress, string newEmailAddress);
        IEnumerable<Account> GetAccountsToRemindAboutActivation();
        bool UpdateAccount(Account account, bool updateUserLevelCaching = true);
        bool UpdateAccounts(IList<Account> accounts, bool updateUserLevelCaching = true);

        void ActivateAccount(Account account);
        IEnumerable<Account> GetAccountsByEntities(IEnumerable<Entity> entities);
        Account GetByEntity(Entity entity);
        Account GetAccountByEntity(Entity entity);

        IEnumerable<Account> GetAccountsToBatchUpdate(bool getFailed, int maxAccountsToReturn, int updateIntervalHours, DateTime minLastLoginDate);

        IEnumerable<Account> GetAccountsToBatchUpdate(double updateIntervalHours, DateTime minLastLoginDate);

        IEnumerable<Account> GetOldYodleeAccountsToBeMoved();

        IEnumerable<OldYodleeAccount> GetOldYodleeAccountsToBeUnregistered();

        void MarkAccountsAs(IEnumerable<Account> accounts, SynchStatusEnum synchState, bool isComplete = false);

        DateTime? GetLastLoginDate(Account account, Group @group);
        DateTime? GetLastLoginDate(Entity entity);

        void AddGroupMember(Account parentAccount, GroupMember newGroupMember, bool skipSave = false);

        bool HidenotificationCentral(TaxPartnerAgent taxPartnerAgent);

        IEnumerable<Account> GetNewAccountsToFollowUp();

        IEnumerable<Account> GetAllAccounts();

        IEnumerable<Account> GetAllAccountsByLastLoginDate(DateTime date);

        bool HardRemoveAccounts(string commaSeparatedEmailAddresses, out List<string> messages);

        bool HardRemoveAccount(string emailAddress);

        bool HardRemoveAccount(int accountID);

        bool SoftRemoveAccount(Account account);

        bool AddToDeleteAccounts(string emailAddress, Cobrand cobrand);

        IEnumerable<DeletedAccount> GetDeletedAccountsToProcessList();

        bool DeletedAccountsMarkAsMCProceessed(IEnumerable<DeletedAccount> deletedAccounts);

        IEnumerable<Account> GetAccountsToDelete();

        void ResetPassword(Account account, string newPassword, string userName);

        Account TrackUserLogin(int accountId, int groupId);
        IEnumerable<Account> GetAccountsLoggedInThreshold();
        IEnumerable<Account> GetAccountsThatHasScorePreferences(IList<int> accountIds);

        void RemoveGroupMember(Account parentAccount, Account guestAccount);
        GroupMember AddGroupMember(Account account, Entity guestEntity, Relationship relationship);
        Account AddGroupMemberNotRegistered(Account account, NewGuestModel newGroupMember);
        //Account GetAccount(PersonEntity person);
        GroupMember AddGroupMember(Account parentAccount, Account guestAccount, Relationship relationship, bool skipSave = false);
        [Obsolete("Use PermissionService.DeleteGroupMember instead")]
        void RemoveGroupMember(Account parentAccount, Entity guestEntity);
        bool HasAccessToPartnerAgent(TaxPartnerAgent loggedInPartnerAgent, TaxPartnerAgent partnerAgentToAccess);

        IEnumerable<Entity> GetEntities(IList<int> entityIds);
        IEnumerable<Account> GetClientAccountsCheckingFinPartnerAccess(Account partnerAccount);
        IEnumerable<Account> GetClientAccountsCheckingPartnerAccess(Account partnerAccount, IList<Relationship> relationships);
        IEnumerable<Account> GetClientAccountsCheckingPartnerAccessAllRelationships(Account partnerAccount);
        Account GetClientAccountCheckingFinPartnerAccess(Account partnerAccount, int clientAccountId);
        Account GetClientAccountCheckingFinPartnerAccess(Account partnerAccount, string clientEmailAddress);
        Account GetClientAccountCheckingPartnerAccess(Account partnerAccount, int clientAccountId, IList<Relationship> relationships);
        IEnumerable<Account> GetClientAccountsCheckingFinPartnerAccess(Account partnerAccount, IList<int> accountIdList, bool includeHidden = false);
        IEnumerable<Account> GetClientAccountsCheckingPartnerAccess(Account partnerAccount, IList<int> accountIdList, IList<Relationship> relationships, bool includeHidden = false);
        IEnumerable<Entity> GetShadowEntities(Account partnerAccount);
        IEnumerable<Entity> GetShadowEntities(Account partnerAccount, IList<Relationship> relationships);


        IEnumerable<Account> GetClientAccountsCheckingPartnerAccess(IEnumerable<Account> partnerAccounts,
                                                                    IList<Relationship> relationships, bool includeHiddenAccounts = false);

        IEnumerable<Account> GetClientAccountsForCobrand(Cobrand cobrand, bool includeHiddenAccounts = false);
        IEnumerable<Account> GetClientAccountsForCobrand(CobrandDTO cobrand, bool includeHiddenAccounts = false);
        int GetCobrandAccountsCount(CobrandDTO cobrand, MpPlanType? planType = null, bool includeHiddenAccounts = true);
        IEnumerable<GroupMember> GetGroupMembersByRelationships(List<TaxPartnerAgent> agents, IList<Relationship> relationships);
        IEnumerable<Account> GetMatchingAccounts(string emailStartsWith);
        IEnumerable<Account> GetMatchingAccountsIncludingAlternateVerifiedEmailAddress(string emailStartsWith, bool prioritiseAccountMatches = true);
        bool ScheduleAccountForDeletion(Account account);

        void CompleteFirstTimeWizard(Account account);
        bool UpdateAccountTracking(Account account);

        void AddAccountantToGroup(Account account, Account accountant, bool skipSave = false);
        IEnumerable<Account> GetExpiredAccounts();
        IEnumerable<Account> GetExpiredAccountsWithThreshold(DateTime thresholdStart, DateTime thresholdEnd);
        /// <summary>
        /// Hotwires registration. Does not send activation email as this requires MVC components. Therefore this method call
        /// should be followed by a call to MyProsperity.Framework.MVC.Extensions.AccountExtensions.SendPasswordReset method
        /// </summary>
        /// <param name="email"></param>
        /// <param name="name"></param>
        /// <param name="password"></param>
        /// <param name="token"></param>
        /// <param name="cobrand"></param>
        /// <param name="accountantId"></param>
        /// <param name="isAccountant"></param>
        /// <returns></returns>
        Account ShortCircuitRegistration(string email, string name, string password, out string token, Cobrand cobrand, int? accountantId = null, bool isAccountant = false);

        Account UpdateAccountCobrand(string email, int cobrandId);

        Account ConvertTempGuestAccountIntoRealAccount(Account tempAccount, string realEmailAddress, Cobrand cobrand);

        Account ShortCircuitRegistrationWithEntityInfo(string email, string name, string password, string phoneNumber, bool isBusinessAccount,
                                             out string token, Cobrand cobrand, int? accountantId = null, DarkWorld darkWorld = null);

        string GenerateAndSavePasswordResetToken(string userName, double expiry, out DateTime expiryDate, out bool saveOk);
        IEnumerable<Account> GetAllAccountsToEmail();

        bool SendActivationEmailsToList(Account partnerAccount, Cobrand partnerCobrand,
            IList<int> clientIds, int resendLimitDays, bool bypassPartnerCheck = false);
        bool SendActivationEmailsToAllClients(Account partnerAccount, Cobrand partnerCobrand, int resendLimitDays);
        int GetTotalNumberOfAccounts();

        bool SendTaxReturnInviteEmailsToList(Account partnerAccount, Cobrand partnerCobrand,
            IList<int> clientIds, int resendLimitDays, bool bypassPartnerCheck = false);

        MpPartnerAccountConversionToken GetPartnerAccountConversionToken(int accountID);
        MpPartnerAccountConversionToken GetDefaultPartnerAccountConversionTokenByBranch(int branchID);
        bool PartnerAccountConversionTokenIsValid(int accountID, string token);
        bool AddOrUpdatePartnerAccountConversionToken(int accountID, int branchID, bool isDefault, out string encryptedToken, int numOfDaysToExpire = 1);
        bool ExpirePartnerAccountConversionToken(MpPartnerAccountConversionToken token);

        void IncrementFailedMFAAttemptCount(Account account);

        void ResetFailedMFAAttemptCount(Account account);

        bool IsEntityShouldBePrimaryEntity(Account parentAccount, PersonEntity guestEntity);
        bool ClearAccountYodleeInformation(IEnumerable<Account> accounts);
        void AddToOldYodleeAccounts(IEnumerable<Account> accounts);

        int UnregisterOldYodleeAccounts(IEnumerable<OldYodleeAccount> oldYodleeAccounts, int numberToDelete);
        bool HideAccounts(Account partnerAccount, IEnumerable<int> clientAccountIds);

        GroupMember GetGroupMember(Account ownerAccount, Entity groupMemberEntity);

        IEnumerable<TaxPartnerAgent> GetAccountantsFromAccount(Account account);

    }
}