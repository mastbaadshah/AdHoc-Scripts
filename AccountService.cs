using Business.Helpers;
using CreativeFactory.MVC;
using Data;
using Data.Enumerations.Notifications;
using Data.Enumerations.PayWall;
using Data.Enumerations.Team;
using Data.Model;
using Data.Model.Cobrand;
using Data.Model.Partners;
using Data.Model.Permissions;
using Data.Services.Accounts;
using MyProsperity.Business.Interfaces;
using MyProsperity.Framework;
using MyProsperity.Framework.Extensions;
using MyProsperity.Framework.Logging;
using MyProsperity.Framework.MVC.Attributes;
using MyProsperity.ServiceAgents;
using Stripe;
using StructureMap;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Transactions;
using Data.Enumerations.ClassSA;
using MyProsperity.DTO;
using Data.Model.Yodlee;
using MyProsperity.Framework.Caching;
using MyProsperity.Framework.Core;
using Entity = Data.Entity;
using MembershipCreateStatus = Data.Services.Accounts.MembershipCreateStatus;

namespace Business
{
    public class AccountService : DBService, IAccountService
    {
        public IMembershipService MembershipService { get; set; }
        public IWealthService WealthService { get; set; }
        public ITimeService TimeService { get; set; }
        public IPlanService PlanService { get; set; }
        public IRegistrationService RegistrationService { get; set; }
        public ICobrandService CobrandService { get; set; }
        public IPortalClientViewService PortalClientViewService { get; set; }


        public AccountService(DBContext ctx)
            : base(ctx)
        {
        }

        public IEnumerable<Account> GetAllAccountsToEmail()
        {
            return DB.Accounts.Where(x => x.EmailAddress.Contains("@"));
        }

        public IEnumerable<Account> GetAllAccounts()
        {
            using (Profiler.Step("AccountService.GetAllAccounts"))
            {
                return DB.Accounts;
            }
        }

        public IEnumerable<Account> GetAllAccountsByLastLoginDate(DateTime date)
        {
            using (Profiler.Step("AccountService.GetAllAccounts"))
            {
                return DB.Accounts.Where(x => x.LastLoginDateTime >= date);
            }
        }

        public IEnumerable<Account> GetAccountsByEntities(IEnumerable<Entity> entities)
        {
            using (Profiler.Step("AccountService.GetAccountsByEntities"))
            {
                var entityIds = entities.Select(x => x.ID);

                var accounts = DB.AccountAccesses.Where(a => entityIds.Contains(a.Entity.ID) && a.IsCreator)
                    .Select(x => x.Account).ToList();

                accounts.AddRange(
                    DB.GroupMembers.Where(x => x.Entity != null && entityIds.Contains(x.Entity.ID))
                    .Select(x => x.Account).ToList()
                    );

                return accounts;
            }
        }

        /// <summary>
        /// Duplicate to fix naming convention
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public Account GetAccountByEntity(Entity entity)
        {
            return GetByEntity(entity);
        }

        public Account GetByEntity(Entity entity)
        {
            using (Profiler.Step("AccountService.GetByEntity"))
            {
                Account account = null;
                // if it is a primary entity, then there will be an entry in the AccountAccesses table
                var accountAccess = DB.AccountAccesses.SingleOrDefault(a => a.Entity.ID == entity.ID && a.IsCreator);

                if (accountAccess != null)
                {
                    account = accountAccess.Account;
                }
                else
                {
                    // else, it could be a guest/shadow entity, so check group members
                    var groupMember = DB.GroupMembers.SingleOrDefault(x => x.Entity != null && x.Entity.ID == entity.ID);
                    if (groupMember != null)
                        account = groupMember.Account;
                }

                return account;
            }
        }

        public Account Get(int? accountId)
        {
            return !accountId.HasValue ? null : Get(accountId.Value);
        }

        public Account Get(int accountId, bool bypassCache = true)
        {
            using (Profiler.Step("AccountService.Get AccountId: " + accountId))
            {
                var cacheKey = CacheHelperKeys.GetCacheKeyForAccount_ByAccountId(accountId);
                var account = CacheHelper.GetItemFromCache(cacheKey) as Account;

                if (account == null || bypassCache)
                {
                    account = DB.Accounts
                        .Include(a => a.Access.Select(aa => aa.Entity))
                        .FirstOrDefault(a => a.ID == accountId);

                    CacheHelper.AddItemToCache(cacheKey, account, 300);

                    if (ConfigHelper.TryGetOrDefault("EnableAccountServiceExtraLogging", false))
                        LogHelper.LogInfo("AccountService.Get Just got account with email: " + (account != null ? account.EmailAddress : "null"));
                }
                return account;
            }
        }

        public void ActivateAccount(Account account)
        {
            account.IsActive = true;
            account.ActivateDate = DateTime.Now;
            account.Score = ConfigHelper.TryGetOrDefault("DefaultStartingScore", 29);

            var currentPlan = PlanService.GetActivePlan(account);
            if (currentPlan == null || currentPlan.MpPlanType == MpPlanType.MpStarter)
            {
                var cobrand = CobrandService.GetCobrandByAccount(account);
                if (cobrand != null && cobrand.CobrandSettings.FreeProTrialPeriodOnJoinDays > 0)
                    PlanService.SetPlan(account, PlanService.GetProPlan(), DateTime.Now.AddDays(cobrand.CobrandSettings.FreeProTrialPeriodOnJoinDays));
            }

            DB.Entry(account).State = System.Data.Entity.EntityState.Modified;
            DB.SaveChanges();
            CacheCrusher3000.VaporiseAccountCache(account);

            if (ConfigHelper.TryGetOrDefault("EnableZohoIncrementalUpdates", false) && account.GetActivePlan().MpPlanType == MpPlanType.MpPro)
            {
                var cobrand = CobrandService.GetCobrandByAccount(account);
                if (cobrand != null)
                {
                    CobrandService.SetAndSaveLastProActivationForZohoIntegration(cobrand);
                }
            }
        }

        public Account GetAccount(string loginRef)
        {
            using (Profiler.Step("AccountService.GetAccount"))
            {
                try
                {
                    return DB.Accounts.FirstOrDefault(a => a.LoginRef == loginRef);
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    throw;
                }
            }
        }

        public Account GetAccount(int accountID)
        {
            return Get(accountID);
        }

        public IEnumerable<Account> GetAccounts(IList<int> accountIds)
        {
            if (!accountIds.AnyAndNotNull())
                return new List<Account>();

            using (Profiler.Step("AccountService.GetAccounts"))
            {
                try
                {
                    return DB.Accounts.Where(a => accountIds.Contains(a.ID));
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    throw;
                }
            }
        }

        public IEnumerable<Account> GetAccountsThatHasScorePreferences(IList<int> accountIds)
        {
            if (!accountIds.AnyAndNotNull())
                return new List<Account>();

            using (Profiler.Step("AccountService.GetAccounts"))
            {
                try
                {
                    return DB.Accounts.Where(a => accountIds.Contains(a.ID) && a.UserPreferencesLastUpdate.HasValue).Include(x => x.UserScorePreferences);
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    throw;
                }
            }
        }

        public Account GetAccountByEmailAddress(string email)
        {
            return DB.Accounts.FirstOrDefault(a => a.EmailAddress == email);
        }

        public Account GetAccountByEmailAddressOrYodleeEmailAddress(string email)
        {
            var account = GetAccountByEmailAddress(email);
            if (account == null)
                account = DB.Accounts.FirstOrDefault(a => a.Access.Any(aa => aa.IsCreator && aa.Entity.YodleeEmailAddress == email));
            return account;
        }

        public Account GetAccountByGuid(Guid guid)
        {
            using (Profiler.Step("AccountService.GetAccountByGuid"))
            {
                try
                {
                    return DB.Accounts.FirstOrDefault(a => a.Guid.ToLower() == guid.ToString().ToLower());
                    //return DB.Accounts.FirstOrDefault(a => String.Equals(a.Guid, guid.ToString(), StringComparison.CurrentCultureIgnoreCase));
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    throw;
                }
            }
        }

        public AccountAccess GetAccountAccess(int accountAccessId)
        {
            using (Profiler.Step("AccountService.GetAccountAccess"))
            {
                return DB.Find<AccountAccess>(accountAccessId);
            }
        }

        public Account GetCreatorAccount(Entity entity)
        {
            return entity == null
                       ? null
                : DB.Accounts
                    .Include(a => a.Access.Select(aa => aa.Entity))
                           .FirstOrDefault(a => a.Access.Any(aa => aa.Entity.ID == entity.ID && aa.IsCreator == true));
        }

        public Account GetGroupAccount(Account logInAccount)
        {
            Account groupAccount = null;

            var logInEntity = logInAccount.GetEntity();
            if (logInEntity != null)
            {
                var otherAccount =
                    DB.Accounts.FirstOrDefault(
                        a =>
                        a.ID != logInAccount.ID &&
                        a.Access.Any(aa => aa.Entity.ID == logInEntity.ID && aa.IsCreator == false));
                if (otherAccount != null)
                {
                    // The entity of the login account is a dependant entity of another account ==> the other account is the group
                    groupAccount = otherAccount;
                }
            }

            if (groupAccount == null)
            {
                // The entity of the login account is NOT a dependant entity of another account ==> the login account is the group
                groupAccount = logInAccount;
            }

            return groupAccount;
        }

        public Group GetGroup(int groupID)
        {
            return DB.Groups.FirstOrDefault(x => x.ID == groupID);
        }

        public int? AddAccountAccess(Account account, Entity entity)
        {
            int? result = null;
            using (Profiler.Step("AccountService.AddAccountAccess"))
            {
                try
                {
                    var accountAccess = new AccountAccess { Entity = entity };
                    account.Access.Add(accountAccess);
                    DB.SaveChanges();
                    result = accountAccess.ID;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    result = null;
                }
            }
            return result;
        }

        public void DeleteEntityAccount(Entity entity, bool skipSave = false)
        {
            using (Profiler.Step("AccountService.DeleteAccount"))
            {
                if (entity != null)
                {
                    if (entity.IsPersonEntity)
                    {
                        Account account = GetCreatorAccount(entity);
                        if (account != null && entity != null &&
                            account.Access != null && account.Access.Count == 1 &&
                            account.Access.ElementAt(0).Entity.ID == entity.ID)
                        // Delete the account only if it has just the one entity
                        {
                            try
                            {
                                bool result = MembershipService.DeleteUser(account.LoginRef, true);
                                if (result == true)
                                {
                                    DB db = DB;
                                    db.Entry(account.Access.ElementAt(0)).State = System.Data.Entity.EntityState.Deleted;
                                    db.Entry(account).State = System.Data.Entity.EntityState.Deleted;

                                    if (!skipSave)
                                        db.SaveChanges();
                                    CacheCrusher3000.VaporiseAccountCache(account);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogHelper.LogError(string.Format("Delete account failed for user {0}: {1}",
                                                                 account.LoginRef,
                                                                 ex.Message));
                                ExceptionHelper.Log(ex);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get the account owner's group from database using the RequestContext value
        /// </summary>
        /// <returns></returns>
        public Group GetAccountOwnerGroup()
        {
            Group aoGroup = null;
            int groupID = -2;
            try
            {
                groupID = RequestContext.Current.AccountOwnerGroupID;

                if (groupID > 0)
                {
                    try
                    {
                        // try loading from DB
                        aoGroup = DB.Groups.FirstOrDefault(g => g.ID == groupID);
                    }
                    catch (Exception)
                    {
                        ExceptionHelper.Log(new Exception(string.Format("Failed to load group from DB in GetAccountOwnerGroup, groupID {0}", groupID)));
                    }
                }
            }
            catch (Exception)
            {
                ExceptionHelper.Log(new Exception(string.Format("Failed to GetAccountOwnerGroup, groupID {0}", groupID)));
            }
            return aoGroup;
        }

        public Account ChangePrimaryEmailAddress(string currentEmailAddress, string newEmailAddress)
        {
            var account = GetAccountByEmailAddress(currentEmailAddress);
            if (account == null)
            {
                ExceptionHelper.Log(
                        new Exception(string.Format("Couldn't change email address: no account found with address {0}", currentEmailAddress)));
                return null;
            }
            CacheCrusher3000.VaporiseAccountCache(account);

            if (string.IsNullOrEmpty(currentEmailAddress) || string.IsNullOrEmpty(newEmailAddress) || currentEmailAddress == newEmailAddress)
            {
                ExceptionHelper.Log(
                        new Exception(string.Format("Change email null value detected currentEmailAddress:{0}, newEmailAddress:{1}", currentEmailAddress, newEmailAddress)));
                return null;
            }

            var userName = MembershipService.GetUserNameByEmail(currentEmailAddress);


            if (string.IsNullOrEmpty(userName))
            {
                ExceptionHelper.Log(
                        new Exception(string.Format("username is null for currentEmailAddress:{0}", currentEmailAddress)));
                return null;
            }

            using (Profiler.Step("MembershipService.ChangeEmailAddress"))
            {
                if (!string.IsNullOrEmpty(MembershipService.GetUserNameByEmail(newEmailAddress)))
                {
                    ExceptionHelper.Log(
                        new Exception(string.Format("Change email failed: {0} {1}", newEmailAddress,
                                                    "Email address is already registered")));

                    return null;
                }

                var changeEmailStatus = MembershipService.ChangeEmailAddress(userName, newEmailAddress);

                if (changeEmailStatus != MembershipCreateStatus.Success &&
                    !currentEmailAddress.Contains("@")) // current email address is a GUID (which means it prob won't exist in membership)
                {
                    changeEmailStatus = MembershipService.CreateUser(userName,
                        RegistrationService.GenerateRandomPasswordThatCompliesWithMpStandards(), newEmailAddress, null, null);
                    var token = MembershipService.GenerateToken();

                    MembershipService.SavePasswordResetToken(userName, token);
                }

                if (changeEmailStatus != MembershipCreateStatus.Success)
                {
                    ExceptionHelper.Log(
                        new Exception(string.Format("Change email failed: {0} {1}", userName,
                                                    changeEmailStatus)));
                    return null;
                }


            }

            try
            {
                var entity = account.GetEntity();
                account.EmailAddress = newEmailAddress;
                entity.PreferredEmailAddress = newEmailAddress;
                DB.Entry(account).State = System.Data.Entity.EntityState.Modified;
                DB.SaveChanges();
                CacheCrusher3000.VaporiseAccountCache(account);

                LogHelper.LogInfo(string.Format("Account Updated: {0}, {1}", account.EmailAddress,
                                                account.LoginRef));

                account = ValidateOrRevertEmailChange(currentEmailAddress, newEmailAddress);

                if (account == null)
                {
                    throw new Exception(
                        string.Format(
                            "Change email failed to update account from: {0} to {1} ",
                            currentEmailAddress, newEmailAddress));
                }

                if (account.IsPartnerAgent)
                {
                    var entityService = ObjectFactory.GetInstance<IEntityService>();
                    entityService.UpdateAllChildEntities(account, entity, TaxService.GetValidPersonRelationshipsListStatic());
                }

                if (!AddToDeleteAccounts(currentEmailAddress, account.CobrandToUse))
                    ApplicationNotification.SendEnquiryToAdmin("Unable to add account to deleted list",
                                                               string.Format(
                                                                   "account id:{0} changed emailaddress from {1} to {2}. unable to add {1} to deleted account list.",
                                                                   account.ID, currentEmailAddress, newEmailAddress));

                return account;
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                ValidateOrRevertEmailChange(currentEmailAddress, newEmailAddress);
                return null;
            }
        }

        private Account ValidateOrRevertEmailChange(string currentEmail, string newEmail)
        {
            var account = GetAccountByEmailAddress(newEmail);
            if (account == null)
                return RevertEmailChange(currentEmail, newEmail);

            var userEmail = MembershipService.GetEmailByUserName(account.LoginRef);

            if (string.IsNullOrEmpty(userEmail) || !userEmail.Equals(newEmail))
                return RevertEmailChange(currentEmail, newEmail);

            return account;
        }

        private bool RevertEmailChangeCheck(string currentEmail)
        {
            var account = GetAccountByEmailAddress(currentEmail);
            var user = MembershipService.GetUserNameByEmail(currentEmail);
            return account == null || string.IsNullOrEmpty(user);
        }

        private Account RevertEmailChange(string currentEmail, string newEmail)
        {
            try
            {
                if (!RevertEmailChangeCheck(currentEmail))
                    return null;

                MembershipService.RevertChangeEmailAddress(currentEmail, newEmail);
                var account = GetAccountByEmailAddress(currentEmail);
                if (account != null)
                    return null;

                account = GetAccountByEmailAddress(newEmail);

                if (account == null)
                    throw new Exception(string.Format("both email return null for account currentemail:{0} - newemail:{1}", currentEmail, newEmail));

                var entity = account.GetEntity();
                account.EmailAddress = currentEmail;
                entity.PreferredEmailAddress = currentEmail;
                DB.Entry(account).State = System.Data.Entity.EntityState.Modified;
                DB.SaveChanges();

                return null;
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return null;
            }
        }

        public bool UpdateAccount(Account account, string oldPassword, string newPassword)
        {
            using (Profiler.Step("AccountService.UpdateAccount"))
            {
                var db = DB;

                if (!string.IsNullOrEmpty(oldPassword) && !string.IsNullOrEmpty(newPassword))
                {
                    using (Profiler.Step("MembershipService.ChangePassword"))
                    {
                        bool changePasswordSuccess = MembershipService.ChangePassword(account.LoginRef, oldPassword,
                                                                                      newPassword);
                        if (!changePasswordSuccess)
                        {
                            ExceptionHelper.Log(
                                new Exception(string.Format("Change password failed: {0}", account.LoginRef)));
                            return false;
                        }
                    }
                }

                try
                {
                    db.Entry(account).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChanges();
                    CacheCrusher3000.VaporiseAccountCache(account);

                    LogHelper.LogInfo(string.Format("Account Updated: {0}, {1}", account.EmailAddress,
                                                    account.LoginRef));

                    return true;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    return false;
                }
            }
        }

        public bool UpdateAccountTracking(Account account)
        {
            var db = DB;
            using (Profiler.Step("AccountService.updateAccountTracking"))
            {
                try
                {
                    db.Entry(account.AccountTracking).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChanges();
                    return true;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.HandleException(ex);
                    return false;
                }
            }
        }

        public Account ShortCircuitRegistration(string email, string name, string password, out string token, Cobrand cobrand, int? accountantId = null, bool isAccountant = false)
        {
            string userName;
            Account account;
            var cobrandToken = cobrand != null ? cobrand.Token : string.Empty;
            //var createStatus = RegistrationService.Register(
            //    email, password, name, out userName, out token, string.Empty, string.Empty, string.Empty,
            //    string.Empty);

            var relationship = string.Empty;

            var createStatus = RegistrationService.Register(true, email, password, name, null, out userName, out account,
                                                            out token, null, relationship, string.Empty, null,
                                                            cobrandToken, string.Empty, string.Empty, accountantId,
                                                            isAccountant);

            if (createStatus != MembershipCreateStatus.Success)
                return null;

            MembershipService.ActivateAccount(userName);
            //var account = cobrand != null ? 
            //    UpdateAccountCobrand(email, cobrand.ID) : GetAccountByEmailAddress(email);

            //if (accountantId.HasValue)
            //{
            //    var accountant = Get(accountantId.Value);


            //    var taxService = ObjectFactory.GetInstance<ITaxService>();
            //    taxService.AddInitialTaxItem(account, accountant);
            //}

            return account; //  http://www.historyfactory.com/wp-content/uploads/2012/11/bf1_lightning.jpeg
        }

        public Account ShortCircuitRegistrationWithEntityInfo(string email, string name, string password, string phoneNumber, bool isBusinessAccount, out string token, Cobrand cobrand, int? accountantId = null, DarkWorld darkWorld = null)
        {
            var cobrandToken = cobrand != null ? cobrand.Token : string.Empty;
            string userName;
            Account account;
            Entity entity = new PersonEntity
            {
                DisplayName = name,
                Created = DateTime.Now,
                LastReviewed = DateTime.Now,
                PreferredEmailAddress = email,
                Relationship = Relationship.Owner,
                ShowGettingStarted = false,
                TelephoneMobile = phoneNumber,
                DarkWorld = darkWorld
            };

            var createStatus = RegistrationService.Register(true, email, password, name, entity, out userName, out account, out token, null, string.Empty, string.Empty, null, cobrandToken, accountantId: accountantId, isBusinessAccount: isBusinessAccount);

            if (createStatus != MembershipCreateStatus.Success)
                return null;

            MembershipService.ActivateAccount(userName);


            return account; //  http://www.historyfactory.com/wp-content/uploads/2012/11/bf1_lightning.jpeg
        }


        public void ResetPassword(Account account, string newPassword, string userName)
        {
            if (account == null)
                throw new ArgumentNullException("account");

            if (!account.ActivateDate.HasValue)
            {
                ActivateAccount(account);
                MembershipService.ActivateAccount(userName);
            }

            //  reset password and delete token
            MembershipService.ResetPassword(userName, newPassword);
            MembershipService.DeletePasswordResetToken(userName);
        }

        /// <summary>
        /// Update and save new cobrand for the account
        /// </summary>
        /// <param name="email"></param>
        /// <param name="cobrandId"></param>
        /// <returns></returns>
        public Account UpdateAccountCobrand(string email, int cobrandId)
        {
            var account = GetAccountByEmailAddress(email);

            if (account != null)
            {
                var cobrand = DB.Cobrands.FirstOrDefault(x => x.ID == cobrandId);
                account.SetCobrand(cobrand);        // null cobrand means remove cobrand
            }

            DB.SaveChanges();

            return account;
        }

        public void AddAccountantToGroup(Account account, Account accountant, bool skipSave = false)
        {
            if (account.IsGroupMember(accountant)) return;

            Relationship relationship = accountant.Person.Relationship ?? Relationship.Other;
            var groupMember = AddGroupMember(account, accountant, relationship, skipSave);

            var permissionService = ObjectFactory.GetInstance<IPermissionService>();
            permissionService.SetDefaultAccountantPermissions(groupMember, skipSave);
        }



        public bool UpdateAccount(Account account, bool updateUserLevelCaching = true)
        {
            var db = DB;
            using (Profiler.Step(string.Format("AccountService.UpdateAccount ID: {0} Email: {1} ", account.ID, account.EmailAddress)))
            {
                try
                {
                    db.Entry(account).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChangesControlCaching(updateUserLevelCaching);
                    CacheCrusher3000.VaporiseAccountCache(account);
                    LogHelper.LogInfo(string.Format("Account Updated: {0}, {1}", account.EmailAddress,
                                                    account.LoginRef));

                    return true;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    return false;
                }
            }
        }



        public bool UpdateAccounts(IList<Account> accounts, bool updateUserLevelCaching = true)
        {
            if (!accounts.AnyAndNotNull())
                return false;

            var db = DB;

            using (Profiler.Step("AccountService.UpdateAccount"))
            {
                try
                {
                    foreach (var account in accounts)
                    {
                        db.Entry(account).State = System.Data.Entity.EntityState.Modified;
                    }

                    db.SaveChangesControlCaching(updateUserLevelCaching);   // single DB call

                    foreach (var account in accounts)
                    {
                        CacheCrusher3000.VaporiseAccountCache(account);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// return the group member record for entity for specific owner
        /// </summary>
        /// <param name="ownerAccount"></param>
        /// <param name="groupMemberEntity"></param>
        /// <returns></returns>
        public GroupMember GetGroupMember(Account ownerAccount, Entity groupMemberEntity)
        {
            var group = ownerAccount.Group;
            //var grpmemberForOwner = DB.GroupMembers.SingleOrDefault(gm => gm.Account == ownerAccount);
            //var gForEntity = (grpmemberForOwner != null ? grpmemberForOwner.Group : null);
            var gmemberForEntity = group != null
                ? group.Members.FirstOrDefault(m => m.Entity != null && m.Entity.ID == groupMemberEntity.ID)
                : null;
            return gmemberForEntity;
        }

        public Account TrackUserLogin(int accountId, int groupId)
        {
            var account = Get(accountId);
            if (account == null)
                throw new NullReferenceException("Account not found for accountID: " + accountId);

            var group = DB.Groups.SingleOrDefault(g => g.ID == groupId) ?? account.Group;

            if (group != null)
            {
                var groupMember = group.Members.FirstOrDefault(m => m.Account != null && m.Account.ID == account.ID);
                if (groupMember != null)
                {
                    var now = DateTime.Now;
                    var loginRecord = new LoginHistory { LoginDateTime = now, GroupMember = groupMember };
                    groupMember.LoginHistory.Add(loginRecord);

                    groupMember.LoginDateTime = now;
                    account.LastLoginDateTime = now;

                    UpdateAccount(account);
                }

                LogHelper.LogInfo(string.Format("Login for accountID: {0} Email: {1} Group: {2}",
                    account.ID, account.EmailAddress, group.ID), "LoginTracking");
            }

            return account;
        }

        public IEnumerable<Account> GetAccountsLoggedInThreshold()
        {
            var minLastLoginDate =
                DateTime.Now.AddDays(-ConfigHelper.TryGetOrDefault("YodleeUpdateLastLoginDaysThreshold", 30));
            return DB.Accounts.Where(t => t.LastLoginDateTime > minLastLoginDate).ToList();
        }

        public IEnumerable<Account> GetAccountsToBatchUpdate(bool getFailed, int maxAccountsToReturn,
                                                             int updateIntervalHours, DateTime minLastLoginDate)
        {
            IEnumerable<Account> accounts = null;
            var lastUpdateTime = DateTime.Now.Subtract(new TimeSpan(updateIntervalHours, 0, 0));

            try
            {
                var db = DB;

                accounts = db.Accounts.
                            Include(a => a.AccountYodleeUpdateLogs).
                            Include(a => a.Access).
                            Include(a => a.Access.Select(c => c.Entity)).
                            Where(a =>
                                (getFailed == (a.SynchStatusInternal.HasValue && a.SynchStatusInternal == (int)SynchStatusEnum.Failed))
                                &&//These checks are performed in IsReadyToBatchUpdate, but include them here to optimise DB query
                                !(a.LastSynchedTime.HasValue && a.LastSynchedTime > lastUpdateTime)
                                &&
                                !(a.Access.Select(c => c.Entity).All(e => e.YodleeUserName == null))
                                ).
                            ToList().
                            Where(a => a.IsReadyToBatchUpdate(lastUpdateTime, minLastLoginDate, DateTime.Now)).
                            Take(maxAccountsToReturn).
                            ToList();
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                throw;
            }

            return accounts;
        }

        public IEnumerable<Account> GetAccountsToBatchUpdate(double updateIntervalHours, DateTime minLastLoginDate)
        {
            IEnumerable<Account> accounts = null;
            var lastUpdateTime = DateTime.Now.AddHours(-1 * updateIntervalHours);

            try
            {
                accounts = DB.Accounts
                    .Include(a => a.Access)
                    .Include(a => a.Access.Select(c => c.Entity))
                    .Include(a => a.AccountYodleeUpdateLogs)
                    .Where(a => !(a.LastSynchedTime.HasValue && a.LastSynchedTime > lastUpdateTime) &&
                                !(a.Access.Select(c => c.Entity).All(e => e.YodleeUserName == null))).ToList()
                    .Where(a => a.IsReadyToBatchUpdate(lastUpdateTime, minLastLoginDate, DateTime.Now)).ToList();
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                throw;
            }

            return accounts;
        }

        public IEnumerable<Account> GetOldYodleeAccountsToBeMoved()
        {
            IEnumerable<Account> accounts = null;
            var finalDate = DateTime.Now.AddDays(ConfigHelper.TryGetOrDefault("GetOldYodleeAccountsToBeMovedThresholdDays", -30));

            try
            {
                var db = DB;

                accounts = db.Accounts.
                    Include(a => a.Access).
                    Include(a => a.Access.Select(c => c.Entity)).
                    Where(a =>
                        (a.ActivateDate.HasValue)
                        &&
                       (a.Access.Any(aa => aa.IsCreator && aa.Entity.YodleeUserName != null && aa.Entity.YodleeUserName != ""))
                        &&
                        (a.Plan != null && (a.Plan.MpPlanTypeInternal == (int)MpPlanType.MpStarter || (a.PlanExpiryDate <= finalDate && (a.FallbackPlan != null && a.FallbackPlan.ID == (int)MpPlanType.MpStarter))))
                    ).
                    ToList();
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                throw;
            }

            return accounts;
        }

        public IEnumerable<OldYodleeAccount> GetOldYodleeAccountsToBeUnregistered()
        {
            return DB.OldYodleeAccounts.Where(x => x.StatusInternal == (int)OldYodleeAccountStatus.Pending);
        }

        public bool ClearAccountYodleeInformation(IEnumerable<Account> accounts)
        {
            try
            {
                var db = DB;
                accounts.Select(c =>
                {
                    var entity = c.GetEntity(true);
                    entity.YodleeUserName = null;
                    entity.YodleePassword = null;
                    entity.YodleeEmailAddress = null;
                    return c;
                }).ToList();

                IList<WealthItem> wealthItemsWithYodleeInfo = new List<WealthItem>();

                //get wealthitems and set the flags
                foreach (var account in accounts)
                {
                    var accountWealthItems = WealthService.GetYodleeWealthItems(account);
                    foreach (var accountWealthItem in accountWealthItems)
                    {
                        wealthItemsWithYodleeInfo.Add(accountWealthItem);
                    }
                }

                if (wealthItemsWithYodleeInfo.Any())
                {
                    var relatedWealthItemIds = wealthItemsWithYodleeInfo.Select(x => x.ID);

                    var wealthItemsList = DB.WealthItems.Where(x => relatedWealthItemIds.Contains(x.ID)).ToList();

                    foreach (var wealthItem in wealthItemsList)
                    {
                        wealthItem.UseDataFeed = false;
                        wealthItem.HasYodleeData = false;
                    }

                    try
                    {
                        DB.SaveChanges();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        throw;
                    }


                }

                return true;
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return false;
            }


        }

        public void AddToOldYodleeAccounts(IEnumerable<Account> accounts)
        {
            var skipSave = true;

            var oldYodleeAccount = new OldYodleeAccount();

            foreach (var account in accounts)
            {
                var entity = account.GetEntity(true);

                oldYodleeAccount = new OldYodleeAccount
                {
                    Account = account,
                    Status = OldYodleeAccountStatus.Pending,
                    CreateDate = DateTime.Now,
                    LastUpdated = DateTime.Now,
                    YodleeUserName = entity.YodleeUserName,
                    YodleePassword = entity.YodleePassword,
                    YodleeEmailAddress = entity.YodleeEmailAddress,
                };

                DB.OldYodleeAccounts.Add(oldYodleeAccount);
                skipSave = false;
            }

            if (!skipSave)
                DB.SaveChanges();
        }

        public int UnregisterOldYodleeAccounts(IEnumerable<OldYodleeAccount> oldYodleeAccounts, int numberToDelete)
        {
            var failedAccounts = 0;

            foreach (var oldYodleeAccount in oldYodleeAccounts.Take(numberToDelete))
            {
                var entityShell = new PersonEntity
                {
                    YodleeUserName = oldYodleeAccount.YodleeUserName,
                    YodleePassword = oldYodleeAccount.YodleePassword,
                    YodleeEmailAddress = oldYodleeAccount.YodleeEmailAddress
                };
                // Delete Yodlee account (so that we don't need to keep paying for it for another month)
                if (!string.IsNullOrEmpty(entityShell.YodleeEmailAddress))
                {
                    try
                    {
                        var oldYodleeAccountToSave = DB.OldYodleeAccounts.FirstOrDefault(x => x.ID == oldYodleeAccount.ID && x.StatusInternal == (int)OldYodleeAccountStatus.Pending);
                        if (oldYodleeAccountToSave != null)
                        {
                            ObjectFactory.GetInstance<IYodleeService>().UnRegister(RequestContext.Current, entityShell);
                            oldYodleeAccountToSave.Status = OldYodleeAccountStatus.Deleted;
                            DB.SaveChanges();
                        }
                        else
                        {
                            failedAccounts++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failedAccounts++;
                        ExceptionHelper.Log(new Exception(String.Format("UnregisterOldYodleeAccounts : Could not remove or save oldYodleeAccount ID: {0}", oldYodleeAccount.ID), ex));
                    }

                }
            }

            return failedAccounts;
        }


        public void MarkAccountsAs(IEnumerable<Account> accounts, SynchStatusEnum synchState, bool isComplete = false)
        {
            var db = DB;
            accounts.Select(c =>
                {
                    c.SynchStatus = synchState;
                    if (isComplete) c.LastSynchedTime = DateTime.Now;
                    return c;
                }).ToList();
            db.SaveChanges();
        }

        public DateTime? GetLastLoginDate(Entity entity)
        {
            var groupMember = DB.GroupMembers.SingleOrDefault(x => x.Entity != null && x.Entity.ID == entity.ID);

            return groupMember == null ? null : groupMember.LoginDateTime;
        }


        public DateTime? GetLastLoginDate(Account account, Group @group)
        {
            var groupMember = @group.Members.SingleOrDefault(x => x.Account.ID == account.ID);

            return groupMember == null ? null : groupMember.LoginDateTime;
        }


        public GroupMember AddGroupMember(Account parentAccount, Account guestAccount, Relationship relationship, bool skipSave = false)
        {
            var u = guestAccount.Members;
            var guestPrimaryEntity = guestAccount.Person;

            var guestEntity = new PersonEntity
            {
                Name = guestPrimaryEntity.DisplayName,
                PreferredEmailAddress = guestPrimaryEntity.PreferredEmailAddress,
                Relationship = relationship,
                ImageToken = guestPrimaryEntity.ImageToken,
                PreferredTelephone = guestPrimaryEntity.PreferredTelephone,
                TelephoneMobile = guestPrimaryEntity.TelephoneMobile,
                TelephoneWork = guestPrimaryEntity.TelephoneWork,
                Title = guestPrimaryEntity.Title,
                GuestAccessDisabled = false
            };
            //---check if this entity is the first Groupmember-professional , then set the IsDefault=1 
            if (guestAccount.IsPartnerAgent && guestAccount.CobrandToUse != null && guestAccount.CobrandToUse.ID == parentAccount.CobrandToUse.ID)
            {
                guestEntity.IsPrimary = IsEntityShouldBePrimaryEntity(parentAccount, guestEntity);
            }
            var newGroupMember = new GroupMember
            {
                Entity = guestEntity,
                Account = guestAccount
            };

            AddGroupMember(parentAccount, newGroupMember, skipSave);

            return newGroupMember;
        }

        public bool IsEntityShouldBePrimaryEntity(Account parentAccount, PersonEntity guestEntity)
        {
            try
            {
                var isAnyPrimaryAddedBefore = parentAccount.GetEntities().Any(e => e.Relationship != null && e.IsPrimary
                                             && (guestEntity.Relationship).GetAttributeValue<CustomName>().Name == TeamMemberCategory.Professionals.ToString());
                if (isAnyPrimaryAddedBefore)
                    return false;
                return true;
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return false;
                //todo:check if account is created and handle this exception. 
            }
        }
        public GroupMember AddGroupMember(Account account, Entity guestEntity, Relationship relationship)
        {
            var guestAccount = DB.Accounts.Single(a => a.Access.Any(c => c.Entity.ID == guestEntity.ID));

            //var newGroupMemberEntity = new PersonEntity
            //    {
            //        Name = guestEntity.Name,
            //        PreferredEmailAddress = guestEntity.PreferredEmailAddress,
            //        Relationship = relationship,
            //        GuestAccessDisabled = false,
            //        ImageToken = guestEntity.ImageToken,
            //    };

            guestEntity.Relationship = relationship;

            var newGroupMember = new GroupMember
            {
                Entity = (PersonEntity)guestEntity,
                Account = guestAccount
            };
            AddGroupMember(account, newGroupMember);
            return newGroupMember;
        }


        //Not a correct function 
        //public Account GetAccount(PersonEntity person)
        //{
        //    try
        //    {

        //        //DB.AccountAccesses.Where(ac=>ac.EntityID)
        //        return DB.Accounts.FirstOrDefault(a => a.Access.Any(c => c.Entity.ID == person.ID));
        //    }
        //    catch (Exception ex)
        //    {
        //        ExceptionHelper.Log(ex);
        //        return null;
        //    }
        //}


        public Account AddGroupMemberNotRegistered(Account account, NewGuestModel newGroupMember)
        {
            var email = Guid.NewGuid().ToString() + "@test.com";
            const string DUMMY_PASSWORD = "Mp#2012#";
            string userName, token;

            var createStatus = RegistrationService.Register(email, DUMMY_PASSWORD, newGroupMember.Name,
                                                            out userName, out token, string.Empty);

            if (createStatus != MembershipCreateStatus.Success)
                throw new Exception("Could not create an account!");

            var accountant = DB.Accounts.Single(a => a.EmailAddress == email);
            var accountantEntity = accountant.GetEntity();
            accountantEntity.PreferredEmailAddress = string.Empty;
            accountantEntity.Relationship = Relationship.Accountant;
            DB.SaveChanges();
            AddAccountantToGroup(account, accountant);
            return accountant;
        }


        [Obsolete("Use PermissionService.DeleteGroupMember instead")]
        public void RemoveGroupMember(Account parentAccount, Entity guestEntity)
        {
            var groupMembers = parentAccount.Group.Members;
            var groupMemberToRemove =
                parentAccount.Group.Members.Single(gm => gm.Entity != null && gm.Entity.ID == guestEntity.ID);
            groupMembers.Remove(groupMemberToRemove);
            DB.Entry(groupMemberToRemove).State = System.Data.Entity.EntityState.Deleted;
            DB.SaveChanges();
        }


        public IEnumerable<GroupMember> GetGroupMembersByRelationships(List<TaxPartnerAgent> agents, IList<Relationship> relationships)
        {
            if (relationships == null || !relationships.Any())
                return new List<GroupMember>();

            var agentIds = agents.Select(x => x.Account.ID).ToList();
            var relationshipIds = relationships.Select(x => (int)x).ToList(); // convert to a list of ints

            // Not sure what the underlying DB query looks like for this - need to watch performance
            return
                DB.GroupMembers.Where(
                    x =>
                    agentIds.Contains(x.Account.ID) &&
                    x.Entity.RelationshipInternal.HasValue &&
                    relationshipIds.Contains(x.Entity.RelationshipInternal.Value));

        }

        public bool HasAccessToPartnerAgent(TaxPartnerAgent loggedInPartnerAgent, TaxPartnerAgent partnerAgentToAccess)
        {
            if (loggedInPartnerAgent == null || partnerAgentToAccess == null)
                return false;

            // check if branch/partner is the same
            if (loggedInPartnerAgent.Branch == null || loggedInPartnerAgent.Branch.Partner == null ||
                partnerAgentToAccess.Branch == null || partnerAgentToAccess.Branch.Partner == null ||
                loggedInPartnerAgent.Branch.Partner.ID != partnerAgentToAccess.Branch.Partner.ID)
                return false;

            // Extra paranoid check, ensure cobrand is the same (it should be if partner is the same)
            return (loggedInPartnerAgent.Cobrand != null && partnerAgentToAccess.Cobrand != null &&
                    loggedInPartnerAgent.Cobrand.ID == partnerAgentToAccess.Cobrand.ID);
        }

        public bool HidenotificationCentral(TaxPartnerAgent taxPartnerAgent)
        {
            try
            {
                taxPartnerAgent.HasDismissedNotification = true;
                taxPartnerAgent.DisMissNotificationLastUpDated = DateTime.Now;
                DB.Entry(taxPartnerAgent).State = System.Data.Entity.EntityState.Modified;
                DB.SaveChanges();
                return true;
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return false;
            }
        }

        public IEnumerable<Entity> GetEntities(IList<int> entityIds)
        {
            if (!entityIds.AnyAndNotNull())
                return new List<Entity>();

            using (Profiler.Step("EntityService.GetEntities"))
            {
                try
                {
                    return DB.Entities.Where(x => entityIds.Contains(x.ID));
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    throw;
                }
            }
        }




        public Account GetClientAccountCheckingFinPartnerAccess(Account partnerAccount, int clientAccountId)
        {
            return GetClientAccountCheckingPartnerAccess(partnerAccount, clientAccountId, TaxService.GetValidPersonRelationshipsListStatic());
        }

        public Account GetClientAccountCheckingFinPartnerAccess(Account partnerAccount, string clientEmailAddress)
        {
            var account = GetAccountByEmailAddress(clientEmailAddress);
            if (account == null)
                return null;

            return GetClientAccountCheckingPartnerAccess(partnerAccount, account.ID, TaxService.GetValidPersonRelationshipsListStatic());
        }

        public Account GetClientAccountCheckingPartnerAccess(Account partnerAccount, int clientAccountId, IList<Relationship> relationships)
        {
            try
            {
                if (partnerAccount == null)
                    throw new ArgumentNullException("partnerAccount");

                if (relationships == null || !relationships.Any())
                    return null;

                var members = PortalClientViewService.GetPortalClientsViews(partnerAccount, relationships, includeHiddenAccounts: true, includeInactiveClients: true);

                var portalClient = members.SingleOrDefault(x => x.Account_ID == clientAccountId);
                if (portalClient == null)
                    throw new Exception(string.Format("Invalid portal client account id: {0} for partner account ID: {1} with relationships: {2} ",
                        clientAccountId, partnerAccount.ID, string.Join(",", relationships.ToArray())));

                var account = Get(portalClient.Account_ID);
                if (account == null)
                    throw new Exception(string.Format("Client account id: {0} not found for partner account ID: {1} with relationships: {2} ",
                        clientAccountId, partnerAccount.ID, string.Join(",", relationships.ToArray())));

                return account;
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return null;
            }
        }

        public IEnumerable<Account> GetClientAccountsCheckingFinPartnerAccess(Account partnerAccount)
        {
            return GetClientAccountsCheckingPartnerAccess(partnerAccount, TaxService.GetValidPersonRelationshipsListStatic());
        }


        public IEnumerable<Account> GetClientAccountsForCobrand(Cobrand cobrand, bool includeHiddenAccounts = false)
        {
            var relationships = TaxService.GetValidPersonRelationshipsListStatic();

            if (relationships == null || !relationships.Any() || cobrand == null)
                return new List<Account>();

            return GetClientAccountsForCobrand(cobrand.ID, relationships, includeHiddenAccounts);
        }

        public IEnumerable<Account> GetClientAccountsForCobrand(CobrandDTO cobrandDTO, bool includeHiddenAccounts = false)
        {
            var relationships = TaxService.GetValidPersonRelationshipsListStatic();

            if (relationships == null || !relationships.Any() || cobrandDTO == null)
                return new List<Account>();

            return GetClientAccountsForCobrand(cobrandDTO.ID, relationships, includeHiddenAccounts);
        }

        private IEnumerable<Account> GetClientAccountsForCobrand(int cobrandId, IList<Relationship> relationships, bool includeHiddenAccounts = false)
        {
            var partnerAccounts = CobrandService.GetPartnerAgentsByCobrand(cobrandId).Select(x => x.Account);

            if (!partnerAccounts.AnyAndNotNull())
                return new List<Account>();

            var accountIds = GetClientAccountsCheckingPartnerAccess(partnerAccounts, relationships, includeHiddenAccounts: includeHiddenAccounts).Select(x => x.ID);
            var accountsForCobrand = DB.Accounts
                .Where(x => accountIds.Contains(x.ID)
                    && x.AccountCobrands.Any(y => y.CobrandID == cobrandId));

            return accountsForCobrand;
        }

        public int GetCobrandAccountsCount(CobrandDTO cobrandDTO, MpPlanType? planType = null, bool includeHiddenAccounts = true)
        {
            var allAccounts = GetClientAccountsForCobrand(cobrandDTO, includeHiddenAccounts).ToList();

            if (!planType.HasValue)
            {
                return allAccounts.Count();
            }

            return allAccounts
                .Select(a => a.GetActivePlan())
                .Count(p => p.MpPlanType == planType);
        }


        public IEnumerable<Account> GetClientAccountsCheckingPartnerAccess(IEnumerable<Account> partnerAccounts, IList<Relationship> relationships, bool includeHiddenAccounts = false)
        {
            if (relationships == null || !relationships.Any())
                return new List<Account>();

            var members = PortalClientViewService.GetPortalClientsViews(partnerAccounts, relationships, includeHiddenAccounts: includeHiddenAccounts);

            var portalClientIds = members.Select(m => m.Account_ID).ToList();
            if (!portalClientIds.AnyAndNotNull())
                return new List<Account>();

            var accounts = GetAccounts(portalClientIds);

            return accounts;
        }


        public IEnumerable<Account> GetClientAccountsCheckingPartnerAccess(Account partnerAccount, IList<Relationship> relationships)
        {
            if (relationships == null || !relationships.Any())
                return new List<Account>();

            var members = PortalClientViewService.GetPortalClientsViews(partnerAccount, relationships);

            var portalClientIds = members.Select(m => m.Account_ID).ToList();
            if (!portalClientIds.AnyAndNotNull())
                return new List<Account>();

            var accounts = GetAccounts(portalClientIds);

            return accounts;
        }


      
        public IEnumerable<Account> GetClientAccountsCheckingPartnerAccessAllRelationships(Account partnerAccount)
        {
            var members = PortalClientViewService.GetPortalClientsViewsAllRelationships(partnerAccount);

            var portalClientIds = members.Select(m => m.Account_ID).ToList();
            if (!portalClientIds.AnyAndNotNull())
                return new List<Account>();

            var accounts = GetAccounts(portalClientIds);

            return accounts;
        }

        public IEnumerable<Account> GetClientAccountsCheckingFinPartnerAccess(Account partnerAccount, IList<int> accountIdList, bool includeHiddenAccounts = false)
        {
            return GetClientAccountsCheckingPartnerAccess(partnerAccount, accountIdList, TaxService.GetValidPersonRelationshipsListStatic(), includeHiddenAccounts);
        }

        public IEnumerable<Account> GetClientAccountsCheckingPartnerAccess(Account partnerAccount, IList<int> accountIdList, IList<Relationship> relationships, bool includeHiddenAccounts = false)
        {
            if (relationships == null || !relationships.Any())
                return new List<Account>();

            var members = PortalClientViewService.GetPortalClientsViews(partnerAccount, relationships, includeHiddenAccounts: includeHiddenAccounts);

            var portalClientIds = members.Where(x => accountIdList.Contains(x.Account_ID)).Select(m => m.Account_ID).ToList();
            if (!portalClientIds.AnyAndNotNull())
                return new List<Account>();

            var accounts = GetAccounts(portalClientIds);

            return accounts;
        }

        public IEnumerable<Entity> GetShadowEntities(Account partnerAccount)
        {
            return GetShadowEntities(partnerAccount, TaxService.GetValidPersonRelationshipsListStatic());
        }

        public IEnumerable<Entity> GetShadowEntities(Account partnerAccount, IList<Relationship> relationships)
        {
            if (relationships == null || !relationships.Any())
                return new List<Entity>();

            List<int> relationshipIds = relationships.Select(x => (int)x).ToList(); // convert to a list of ints

            // Not sure what the underlying DB query looks like for this - need to watch performance
            var accountGroupMembers = DB.GroupMembers.Where(x => x.Account.ID == partnerAccount.ID &&
                x.Entity.RelationshipInternal.HasValue && relationshipIds.Contains(x.Entity.RelationshipInternal.Value)).ToList();

            return accountGroupMembers.Select(x => x.Entity);
        }

        public IEnumerable<Account> GetMatchingAccounts(string emailStartsWith)
        {
            return (from a in DB.Accounts
                    where a.EmailAddress.ToLower().StartsWith(emailStartsWith.ToLower())
                    select a);
        }

        public IEnumerable<Account> GetMatchingAccountsIncludingAlternateVerifiedEmailAddress(string emailStartsWith, bool prioritiseAccountMatches = true)
        {
            string emailStartesWithLower = emailStartsWith.ToLower();
            // Direct matches on account primary email address
            var accountsMatched = DB.Accounts.Where(a => a.EmailAddress.ToLower().StartsWith(emailStartesWithLower)).ToList();

            // Matches on alternate verified email address
            var accountsAlternateMatch = DB.Accounts.Where(a =>
                a.AccountSettings != null
                && a.AccountSettings.AlternateVerifiedEmailAddress != null
                && a.AccountSettings.AlternateVerifiedEmailAddress.ToLower().StartsWith(emailStartesWithLower)).ToList();

            if (prioritiseAccountMatches)
            {
                // Won't include any accounts if the alternate email address matches any existing email addresses
                // (i.e. get primary email addresses and then add unique alt email addresses)
                string altEmailLower = null;
                foreach (var account in accountsAlternateMatch)
                {
                    altEmailLower = account.GetAlternateVerifiedEmailAddress().ToLower();

                    if (!accountsMatched.Exists(a =>
                        a.EmailAddress.ToLower() == account.EmailAddress.ToLower()
                        || a.EmailAddress.ToLower() == altEmailLower
                        || (a.AccountSettings != null
                            && a.AccountSettings.AlternateVerifiedEmailAddress != null
                            && a.AccountSettings.AlternateVerifiedEmailAddress.ToLower() == altEmailLower)))
                    {
                        accountsMatched.Add(account);
                    }
                }
            }
            else
            {
                // Note: This *may* include accounts where the primary email address matches another account's alternate email address
                // or where multiple accounts have the same alternate email address
                foreach (var account in accountsAlternateMatch)
                {
                    if (!accountsMatched.Exists(a => a.EmailAddress.ToLower() == account.EmailAddress.ToLower()))
                    {
                        accountsMatched.Add(account);
                    }
                }
            }

            return accountsMatched;
        }


        public void RemoveGroupMember(Account parentAccount, Account guestAccount)
        {
            var groupMembers = parentAccount.Group.Members;
            var groupMemberToRemove =
                parentAccount.Group.Members.Single(gm => gm.Entity.ID == guestAccount.GetEntity().ID);
            groupMembers.Remove(groupMemberToRemove);
            DB.Entry(groupMemberToRemove).State = System.Data.Entity.EntityState.Deleted;
            DB.SaveChanges();
        }

        public void AddGroupMember(Account parentAccount, GroupMember newGroupMember, bool skipSave = false)
        {
            parentAccount.Group.Members.Add(newGroupMember);

            if (!skipSave)
                DB.SaveChanges();
        }


        public IEnumerable<Account> GetNewAccountsToFollowUp()
        {
            using (Profiler.Step("AccountService.GetNewAccountsToFollowUp"))
            {
                var db = DB;

                var d1 = DateTime.Now.Date.AddDays(-5);
                //var d2 = DateTime.Now.Date.AddMilliseconds(-1);
                var accounts = db.Accounts.Include(a => a.Access.Select(b => b.Entity))
                                 .Where(a => a.IsActive.HasValue
                                             && a.IsActive.Value
                                             && a.ActivateDate.HasValue
                                             &&
                                             (!a.FollowupNotificationSent.HasValue || !a.FollowupNotificationSent.Value)
                                             && a.ActivateDate.Value >= d1);
                //&& a.ActivateDate.Value <= d2);


                return accounts.ToList();
            }
        }

        //todo find a better way to remove this from delete accounts
        public bool DeleteStripeSubscriptionsForCustomer(Account account)
        {
            try
            {
                //var stripeCustomer = GetCustomer(account);
                //var customerId = stripeCustomer.Id;
                var customerId = account.StripeCustomerId;
                var subscriptionService = new StripeSubscriptionService();
                var subscriptions = subscriptionService.List(customerId);

                foreach (var subscription in subscriptions)
                {
                    subscriptionService.Cancel(customerId, subscription.Id);
                }

                return true;
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return false;
            }
        }

        public bool HardRemoveAccounts(string commaSeparatedEmailAddresses, out List<string> messages)
        {
            var emailAddresses = commaSeparatedEmailAddresses.Split(',');
            messages = new List<string>();
            foreach (var emailAddress in emailAddresses)
            {
                var trimmedEmail = emailAddress.Trim();
                var account = GetAccountByEmailAddress(trimmedEmail);
                if (account == null)
                {
                    messages.Add(string.Format("Account not found: {0}", trimmedEmail));
                    continue;
                }
                if (account.IsPartnerAgent)
                {
                    messages.Add(string.Format("Not deleting partner agent account: {0}", trimmedEmail));
                    continue;
                }

                if (!AddToDeleteAccounts(trimmedEmail, account.CobrandToUse))
                {
                    messages.Add(string.Format("Error adding to DeleteAccounts: {0}", trimmedEmail));
                    continue;
                }
                if (!HardRemoveAccount(trimmedEmail))
                {
                    messages.Add(string.Format("Error deleting account {0}", trimmedEmail));
                }
            }
            return (messages.Count == 0);
        }

        public bool HardRemoveAccount(string emailAddress)
        {
            try
            {
                var account = GetAccountByEmailAddress(emailAddress);
                if (account == null)
                    throw new Exception(string.Format("Account is null for emailaddress: {0}", emailAddress));

                return HardRemoveAccount_Core(emailAddress, account);
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return false;
            }
        }

        public bool HardRemoveAccount(int accountID)
        {
            try
            {
                var account = GetAccount(accountID);
                if (account == null)
                    throw new Exception(string.Format("Account is null for accountID: {0}", accountID));

                return HardRemoveAccount_Core(account.EmailAddress, account);
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return false;
            }
        }

        private bool HardRemoveAccount_Core(string emailAddress, Account account)
        {
            using (Profiler.Step("AccountService.HardRemoveAccount_Core: " + emailAddress))
            {
                Entity entity = account.GetEntity();

                var ok = string.IsNullOrEmpty(account.StripeCustomerId) || DeleteStripeSubscriptionsForCustomer(account);
                if (!ok)
                    return false;

                using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted }))
                {

                    // Delete Yodlee account (so that we don't need to keep paying for it for another month)
                    if (entity != null && !string.IsNullOrEmpty(entity.YodleeEmailAddress))
                    {
                        ObjectFactory.GetInstance<IYodleeService>().UnRegister(RequestContext.Current, entity);
                    }

                    // Delete from MP database
                    Context.Current.Database.ExecuteSqlCommand("DeleteAccountByEmailAddress @usertodelete",
                                                               new SqlParameter("usertodelete", emailAddress));
                    scope.Complete();
                }
            }
            return true;
        }

        public bool SoftRemoveAccount(Account account)
        {
            account.AccountSettings.AccountDeleted = true;
            return UpdateAccount(account);
        }

        public IEnumerable<DeletedAccount> GetDeletedAccountsToProcessList()
        {
            return DB.DeletedAccounts.Where(x => !x.MCProcessed.HasValue || (x.MCProcessed.HasValue && !x.MCProcessed.Value));
        }
        public bool DeletedAccountsMarkAsMCProceessed(IEnumerable<DeletedAccount> deletedAccounts)
        {
            try
            {
                foreach (var deletedAccount in deletedAccounts)
                {
                    deletedAccount.MCProcessed = true;
                }

                DB.SaveChanges();
                return true;
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return false;
            }
        }

        public int GetTotalNumberOfAccounts()
        {
            return DB.Accounts.Count();
        }

        public bool AddToDeleteAccounts(string emailAddress, Cobrand cobrand)
        {
            using (Profiler.Step("AccountService.AddToDeleteAccounts"))
            {
                try
                {
                    if (cobrand == null)
                        cobrand = DB.Cobrands.FirstOrDefault(x => x.ID == 1);

                    var deleteAccount = new DeletedAccount() { EmailAddress = emailAddress, DeletedDate = DateTime.Now, Cobrand = cobrand };

                    //DB.DeletedAccounts.Add(deleteAccount);
                    DB.Entry(deleteAccount).State = System.Data.Entity.EntityState.Added;
                    DB.SaveChanges();
                    return true;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    return false;
                }
            }
        }

        public IEnumerable<Account> GetAccountsToDelete()
        {
            using (Profiler.Step("AccountService.GetAccountsToDelete"))
            {
                var appSettingValue = ConfigurationManager.AppSettings["deactivateAccountNumberOfDays"];
                var numberOfDays = (appSettingValue != null) ? Convert.ToInt32(appSettingValue) : 10;
                //numberOfDays = numberOfDays - 1;
                var d1 = DateTime.Now.Date.AddDays(-numberOfDays);
                var db = DB;

                var accounts =
                    db.Accounts.Where(
                        x =>
                        x.DeactivatedDate.HasValue &&
                        x.DeactivatedDate <= d1 &&
                        !(x.AccountSettings.AccountDeleted.HasValue && x.AccountSettings.AccountDeleted.Value) &&
                        x.EmailAddress.Contains("@"));

                return accounts.ToList();
            }
        }

        public bool ScheduleAccountForDeletion(Account account)
        {

            var ok = string.IsNullOrEmpty(account.StripeCustomerId) || DeleteStripeSubscriptionsForCustomer(account);
            if (!ok)
                return false;

            var entity = account.GetEntity();
            account.DeactivatedDate = DateTime.Now;

            var result = UpdateAccount(account);
            if (result)
                ApplicationNotification.SendDeleteAccountNotification(MessageType.Email, account.EmailAddress,
                    entity.DisplayName, DateTime.Now);

            return result;
        }

        public void CompleteFirstTimeWizard(Account account)
        {
            if (account == null)
                throw new ArgumentNullException("account");
            account = GetAccount(account.ID); // get account without going to cache

            account.FirstTimeWizardCompletedDate = DateTime.Now;
            UpdateAccount(account);
        }


        public IEnumerable<Account> GetExpiredAccounts()
        {
            return DB.Accounts.Where(x => x.Plan.ID != 1 && x.PlanExpiryDate <= DateTime.Now.Date);
        }

        public IEnumerable<Account> GetExpiredAccountsWithThreshold(DateTime thresholdStart, DateTime thresholdEnd)
        {
            //return DB.Accounts.Where(x => x.EmailAddress == "angb15091401@test.com");
            return
                DB.Accounts.Where(
                    x => x.Plan.ID != 1 && x.PlanExpiryDate >= thresholdStart && x.PlanExpiryDate <= thresholdEnd);
        }

        public string GenerateAndSavePasswordResetToken(string userName, double expiry, out DateTime expiryDate, out bool saveOk)
        {
            var token = MembershipService.GenerateToken();
            expiryDate = DateTime.Now.AddMinutes(expiry);
            saveOk = MembershipService.SavePasswordResetToken(userName, token);
            return token;
        }

        public IEnumerable<Account> GetAccountsToRemindAboutActivation()
        {
            try
            {
                var intervalList = ConfigHelper.TryGetOrDefault("ActivationEmailReminderProviderDayInterval", "2,8,14");
                var dayIntervals = intervalList.Split(',').Select(n => Convert.ToInt32(n)).ToList();
                var dateTimeList = dayIntervals.Select(dayInterval => DateTime.Now.AddDays(-dayInterval).Date).ToList();

                var directJoinAccounts =
                    DB.Accounts.Where(x => dateTimeList.Any() && x.EmailAddress.Contains("@") && !x.ActivateDate.HasValue && x.ActivationEmailSentDt.HasValue && dateTimeList.Contains(System.Data.Entity.DbFunctions.TruncateTime(x.ActivationEmailSentDt.Value).Value));

                //var clientAccounts =
                //  DB.ActivationRequests.Where(
                //      x => dateTimeList.Any() && dateTimeList.Contains(EntityFunctions.TruncateTime(x.Requested).Value)).Select(x => x.NewUserAccount).Where(x => !x.LastLoginDateTime.HasValue);


                //return directJoinAccounts.Union(clientAccounts);


                return directJoinAccounts;
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return new List<Account>();
            }
        }



        //public void GetAllTodoForAgentPortal(Account partnerAccount,
        //    bool includeTempGuestAccounts = false, bool includeSoftDeletedAccounts = false,
        //    bool includeHiddenAccounts = false,
        //    bool lazyLoadPlansMayCauseIssuesIfUsingPlans = false)
        //{
        //    if (partnerAccount == null)
        //        throw new ArgumentNullException("partnerAccount");

        //    // Not sure what the underlying DB query looks like for this - need to watch performance
        //    var groupMembers = DB.GroupMembers.Where(x => x.Account.ID == partnerAccount.ID &&
        //                                                  x.Entity.RelationshipInternal.HasValue).ToList();
        //    var accountantEntityIds = groupMembers.Where(x=>x.Entity != null).Select(x => x.Entity.ID).ToList();
        //    var portalViews = GetPortalClientsViewsAllRelationships(partnerAccount, includeTempGuestAccounts,
        //        includeSoftDeletedAccounts, includeHiddenAccounts, lazyLoadPlansMayCauseIssuesIfUsingPlans);
        //    var clientEntityIds = portalViews.Select(x => x.Entity_ID).ToList();
        //    var clientAccountIds = portalViews.Select(x => x.Account_ID).ToList();
        //    var allEntityIds = accountantEntityIds.Union(clientEntityIds);

        //    var todos = DB.PlanToDos.Where(x => x.Owners.Any(y => allEntityIds.Contains(y.Entity.ID)) ||
        //                                        (x.AssignedOriginalAccount != null &&
        //                                         clientAccountIds.Contains(x.AssignedOriginalAccount.ID)));
        //}


        public bool SendActivationEmailsToList(Account partnerAccount, Cobrand partnerCobrand,
            IList<int> clientIds, int resendLimitDays, bool bypassPartnerCheck = false)
        {
            if (partnerAccount == null)
                throw new ArgumentNullException("partnerAccount");

            // Check permissions. Fail if any fail check
            var linkAccounts = bypassPartnerCheck
                ? GetAccounts(clientIds).ToList()
                : GetClientAccountsCheckingFinPartnerAccess(partnerAccount, clientIds, true).ToList();

            linkAccounts = linkAccounts.Where(x => x.AccountCobrands.Any(y => y.CobrandID == partnerCobrand.ID)).ToList();

            var linkAccountsIds = linkAccounts.Select(x => x.ID);

            clientIds = clientIds.Where(x => linkAccountsIds.Contains(x)).ToList();

            if (!linkAccounts.AnyAndNotNull() || linkAccounts.Count != clientIds.Count)
                return false;

            // Send activation emails async
            return RegistrationService.SendActivationEmailToExistingClients(
                partnerAccount.ID, clientIds, partnerCobrand, resendLimitDays: resendLimitDays);
        }

        public bool SendActivationEmailsToAllClients(Account partnerAccount, Cobrand partnerCobrand, int resendLimitDays)
        {
            if (partnerAccount == null)
                throw new ArgumentNullException("partnerAccount");

            // Check permissions. Fail if any fail check
            var linkAccounts = GetClientAccountsCheckingFinPartnerAccess(partnerAccount).ToList();
            if (linkAccounts == null || linkAccounts.Count <= 0)
                return false;

            var clientIds = linkAccounts.Select(x => x.ID).ToList();

            // Send activation emails async
            return RegistrationService.SendActivationEmailToExistingClients(
                partnerAccount.ID, clientIds, partnerCobrand, resendLimitDays: resendLimitDays);
        }

        /// <summary>
        /// Send Tax return Email and Todo to clients 
        /// </summary>
        /// <param name="partnerAccount"></param>
        /// <param name="partnerCobrand"></param>
        /// <param name="clientIds">Client AccountIDs</param>
        /// <param name="resendLimitDays"></param>
        /// <param name="bypassPartnerCheck"></param>
        /// <returns></returns>
        public bool SendTaxReturnInviteEmailsToList(Account partnerAccount, Cobrand partnerCobrand,
        IList<int> clientIds, int resendLimitDays, bool bypassPartnerCheck = false)
        {
            if (partnerAccount == null)
                throw new ArgumentNullException("partnerAccount");

            // Check permissions. Fail if any fail check
            var linkAccounts = GetPartnerLinkedClientsAccounts(clientIds, partnerAccount, bypassPartnerCheck);
            //var linkAccounts = bypassPartnerCheck
            //    ? GetAccounts(clientIds).ToList()
            //    : GetClientAccountsCheckingFinPartnerAccess(partnerAccount, clientIds, true).ToList();

            linkAccounts = linkAccounts.Where(x => x.AccountCobrands.Any(y => y.CobrandID == partnerCobrand.ID)).ToList();

            var linkAccountsIds = linkAccounts.Select(x => x.ID);

            clientIds = clientIds.Where(x => linkAccountsIds.Contains(x)).ToList();

            if (!linkAccounts.AnyAndNotNull() || linkAccounts.Count != clientIds.Count)
                return false;

            // Send activation emails async
            return SendTaxReturnInviteEmailToClients(
                partnerAccount.ID, clientIds, resendLimitDays: resendLimitDays);
        }
        public IList<Account> GetPartnerLinkedClientsAccounts(IList<int> clientAccountIds, Account partnerAccount, bool bypassPartnerCheck = false)
        {
            // Check permissions. Fail if any fail check
            var linkAccounts = bypassPartnerCheck
                ? GetAccounts(clientAccountIds).ToList()
                : GetClientAccountsCheckingFinPartnerAccess(partnerAccount, clientAccountIds, true).ToList();
            //linkAccounts = linkAccounts.Where(x => x.AccountCobrands.Any(y => y.CobrandID == partnerCobrand.ID)).ToList();
            return linkAccounts;
        }

        private bool SendTaxReturnInviteEmailToClients(int accountantAccountID, IList<int> clientAccountIdList,
            int resendLimitDays)
        {
            return SendTaxReturnInviteEmailToClientAccounts_Core1(
                accountantAccountID, clientAccountIdList, resendLimitDays, true);
        }

        private bool SendTaxReturnInviteEmailToClientAccounts_Core1(int accountantAccountID,
            IList<int> clientAccountIdList, int resendLimitDays, bool isClient = true)
        {
            bool result = true;
            var accountantAccount = DB.Accounts.Single(t => t.ID == accountantAccountID);
            if (accountantAccount == null)
                throw new ArgumentNullException("accountantAccountID does not exist: " + accountantAccountID);

            if (!clientAccountIdList.AnyAndNotNull())
                return false;

            var clientAccounts = DB.Accounts.Where(x => clientAccountIdList.Contains(x.ID)).ToList();
            DateTime limit = DateTime.Now.AddDays(-resendLimitDays);

            //            clientAccounts = clientAccounts.Where(x => !x.LastLoginDateTime.HasValue &&
            //                                                               (!x.ActivationEmailSentDt.HasValue || x.ActivationEmailSentDt.Value < limit)).ToList();

            if (!clientAccounts.Any())
                return false;

            var sendViaNotificationQueueAsync = (!isClient || clientAccounts.Count > 1);    // send immediately if activating single client account
            var maxEligible = clientAccounts.Count;
            var clientInvitationMaxBatchSize = ConfigHelper.TryGetOrDefault("ClientInvitationMaxBatchSize", 100); // zero means unlimited
            if (clientInvitationMaxBatchSize > 0)
                clientAccounts = clientAccounts.Take(clientInvitationMaxBatchSize).ToList();

            var successfullySent = "Successfully sent: ";
            var unsuccessfullySent = string.Empty;
            var successfulCount = 0;
            var unsuccessfulCount = 0;

            foreach (var clientAccount in clientAccounts)
            {
                if (SendTaxReturnToClientAccount_Core2(accountantAccount, clientAccount,
                    resendLimitDays, sendViaNotificationQueueAsync))
                {
                    successfulCount++;
                    successfullySent = string.Format("{0}{1}, ", successfullySent, clientAccount.EmailAddress);
                }
                else
                {
                    result = false;
                    unsuccessfulCount++;
                    unsuccessfullySent = string.Format("{0}{1}, ", unsuccessfullySent, clientAccount.EmailAddress);
                }
            }

            var finalMsg = unsuccessfulCount > 0
                ? string.Format("Sent {0}/{1} emails. {2}. Unsuccessful: {3}",
                    successfulCount, clientAccounts.Count, successfullySent, unsuccessfullySent)
                : string.Format("Sent {0}/{1} emails. {2}.", successfulCount, clientAccounts.Count, successfullySent);
            if (clientInvitationMaxBatchSize > 0)
                finalMsg = string.Format("{0} ClientTaxReturnInviteMaxBatchSize: {1}", finalMsg, clientInvitationMaxBatchSize);

            finalMsg = string.Format("{0}. Remaining eligible Tax Assistant invites: {1}", finalMsg, (maxEligible - successfulCount));

            LogHelper.LogInfo(finalMsg, "SendTaxReturnInviteEmailToClientAccounts_Core1");

            return result;
        }

        private bool SendTaxReturnToClientAccount_Core2(Account accountantAccount,
            Account clientAccount, int resendLimitDays,
            bool sendViaNotificationQueueAsync = true)
        {
            if (accountantAccount == null)
                throw new ArgumentNullException("accountantAccount");

            if (clientAccount == null)
                throw new ArgumentNullException("clientAccount");

            return ApplicationNotification.SendTaxReturnInviteToClient(
                    accountantAccount, clientAccount, ApplicationNotification.GetClientTaxUrl(clientAccount, CobrandService), sendViaNotificationQueueAsync);

        }

        public Account ConvertTempGuestAccountIntoRealAccount(Account tempAccount, string realEmailAddress, Cobrand cobrand)
        {
            if (tempAccount == null)
                throw new ArgumentNullException("tempAccount");

            if (string.IsNullOrEmpty(realEmailAddress))
                throw new ArgumentNullException("realEmailAddress");


            var hasError = !CheckOrCreateMembershipForGuestAccount(tempAccount, out tempAccount);

            if (hasError)
                throw new Exception(string.Format("Error converting account for accountId{0}", tempAccount.ID));

            var tempEmailAddress = tempAccount.EmailAddress;

            var updatedAccount = ChangePrimaryEmailAddress(tempEmailAddress, realEmailAddress);

            if (updatedAccount == null)
                throw new Exception(string.Format("Problem trying to convert temp guest account for temp (original) email: {0}, real (new) email: {1}",
                        tempEmailAddress, realEmailAddress));

            updatedAccount.SetCobrand(cobrand);
            return updatedAccount;
        }

        public MpPartnerAccountConversionToken GetPartnerAccountConversionToken(int accountID)
        {
            return DB.MpPartnerAccountConversionTokens.FirstOrDefault(t => t.Account.ID == accountID);
        }

        public MpPartnerAccountConversionToken GetDefaultPartnerAccountConversionTokenByBranch(int branchID)
        {
            return DB.MpPartnerAccountConversionTokens.FirstOrDefault(t => t.Branch.ID == branchID && t.IsDefault && t.ExpiryDate >= DateTime.Now);
        }

        public bool PartnerAccountConversionTokenIsValid(int accountID, string token)
        {
            var decryptedToken = ObjectFactory.GetInstance<IAWSKMSSA>().Decrypt(token);
            return DB.MpPartnerAccountConversionTokens.Any(t => t.Account.ID == accountID && t.TokenGuid.ToString() == decryptedToken && t.ExpiryDate >= DateTime.Now);
        }

        public bool AddOrUpdatePartnerAccountConversionToken(int accountID, int branchID, bool isDefault, out string encryptedToken, int numOfDaysToExpire = 1)
        {
            using (Profiler.Step("AccountService.AddOrUpdateMpPartnerAccountConversionToken"))
            {
                try
                {
                    var db = DB;

                    var account = GetAccount(accountID);
                    var branch = DB.TaxPartnerBranches.FirstOrDefault(b => b.ID == branchID);
                    if (account == null || branch == null)
                    {
                        encryptedToken = string.Empty;
                        return false;
                    }

                    var tokenGuid = Guid.NewGuid();
                    var currentToken = GetPartnerAccountConversionToken(accountID);

                    if (currentToken != null)
                    {
                        currentToken.Branch = branch;
                        currentToken.RequestDate = DateTime.Now;
                        currentToken.ExpiryDate = currentToken.RequestDate.AddDays(numOfDaysToExpire);
                        currentToken.TokenGuid = tokenGuid;
                        currentToken.IsDefault = isDefault;
                        db.Entry(currentToken).State = System.Data.Entity.EntityState.Modified;
                    }
                    else
                    {
                        var token = new MpPartnerAccountConversionToken();
                        token.Account = account;
                        token.Branch = branch;
                        token.RequestDate = DateTime.Now;
                        token.ExpiryDate = token.RequestDate.AddDays(numOfDaysToExpire);
                        token.TokenGuid = tokenGuid;
                        token.IsDefault = isDefault;
                        db.Entry(token).State = System.Data.Entity.EntityState.Added;
                    }

                    db.SaveChanges();
                    encryptedToken = ObjectFactory.GetInstance<IAWSKMSSA>().Encrypt(tokenGuid.ToString());
                    return true;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    encryptedToken = string.Empty;
                    return false;
                }
            }
        }

        public bool ExpirePartnerAccountConversionToken(MpPartnerAccountConversionToken token)
        {
            using (Profiler.Step("AccountService.ExpirePartnerAccountConversionToken"))
            {
                try
                {
                    token.ExpiryDate = DateTime.Now;
                    DB.Entry(token).State = System.Data.Entity.EntityState.Modified;
                    DB.SaveChanges();
                    return true;

                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    return false;
                }
            }
        }

        private bool CheckOrCreateMembershipForGuestAccount(Account guestAccount, out Account account)
        {
            if (guestAccount == null)
                throw new ArgumentNullException("guestAccount");

            var userName = MembershipService.GetUserNameByEmail(guestAccount.EmailAddress);


            account = guestAccount;

            if (!string.IsNullOrEmpty(userName))
                return true;



            var password = ConfigHelper.TryGetOrDefault("GuestAccessTemppassword", "Mp@123");
            var status = MembershipService.CreateUser(guestAccount.LoginRef, password, guestAccount.EmailAddress, null, null);


            if (status != MembershipCreateStatus.Success)
            {
                var message = string.Format("Registration failed: {0} {1}", guestAccount.EmailAddress, status);
                ExceptionHelper.Log(new Exception(message));
                return false;
            }

            return UpdateAccount(guestAccount);
        }

        public void IncrementFailedMFAAttemptCount(Account account)
        {
            account.FailedMFAAttemptCount++;

            var userName = MembershipService.GetUserNameByEmail(account.EmailAddress);

            if (!VarifyMFAAttemptCount(account) &&
                !string.IsNullOrWhiteSpace(userName))
            {
                ResetFailedMFAAttemptCount(account);
                MembershipService.SetUserLock(userName, true);
            }
            else
            {
                UpdateAccount(account);
            }
        }

        public void ResetFailedMFAAttemptCount(Account account)
        {
            account.FailedMFAAttemptCount = 0;
            UpdateAccount(account);
        }

        private bool VarifyMFAAttemptCount(Account account)
        {
            return account.FailedMFAAttemptCount < ConfigHelper.TryGetOrDefault("MaxFailedMFAAttemptCount", 5);
        }

        public bool HideAccounts(Account partnerAccount, IEnumerable<int> clientAccountIds)
        {
            try
            {
                if (partnerAccount == null)
                    throw new NullReferenceException("-- Hide Accounts-- partner can not be null");

                var members = PortalClientViewService.GetPortalClientsViews(partnerAccount, TaxService.GetValidPersonRelationshipsListStatic(), includeHiddenAccounts: true, includeInactiveClients: true);
                var memberIds = members.Select(x => x.Account_ID);
                var validMemberIds = memberIds.Intersect(clientAccountIds).ToList();

                var accounts = GetAccounts(validMemberIds);
                foreach (var account in accounts)
                {
                    var accountCobrand = account.AccountCobrands.FirstOrDefault(ac => ac.CobrandID == partnerAccount.CobrandToUse.ID);
                    if (accountCobrand != null)
                    {
                        accountCobrand.IsCurrentClient = false;
                    }
                    DB.Entry(accountCobrand).State = System.Data.Entity.EntityState.Modified;
                }

                DB.SaveChanges();

                if (validMemberIds.Count != clientAccountIds.Count())
                {
                    var errorMessage = string.Format("--Hide Accounts-- in valid for ids: {0}",
                        string.Join(", ", clientAccountIds.Where(x => !memberIds.Contains(x))));
                    throw new ApplicationException(errorMessage);
                }
                return true;
            }
            catch (Exception exception)
            {
                ExceptionHelper.Log(exception);
                return false;
            }
        }

        //public bool GetAccountSetting(Account account)
        //{
        //    var t=DB.AccountSettings.Where(ast => ast.ID == account.AccountSettings.ID).FirstOrDefault();
        //    t.
        //}

        public IEnumerable<TaxPartnerAgent> GetAccountantsFromAccount(Account account)
        {
            using (Profiler.Step("AccountService.GetAccountantsFromAccount"))
            {
                try
                {
                    var taxService = ObjectFactory.GetInstance<ITaxService>();
                    var accountantIDs = taxService.GetRegisteredAccountants(account);

                    return accountantIDs;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    throw;
                }
            }
        }
    }
}

