using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data.Entity.Core.Objects;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Business.Extensions;
using Business.RealEstate;
using Calculators.Lib;
using Calculators.Lib.Model;
using CreativeFactory.MVC;
using Data;
using Data.Model;
using Data.Model.Partners;
using Data.Model.SA;
using MyProsperity.Business.Interfaces;
using MyProsperity.Framework.EfUtils;
using MyProsperity.Framework.Extensions;
using MyProsperity.Framework.Logging;
using Data.Model.Wealth;
using MyProsperity.Framework;
using MyProsperity.Framework.Caching;
using MyProsperity.ServiceAgents;
using PostSharp.Aspects;
using StructureMap;
using Yodlee.Api;
using BankAccount = Data.BankAccount;
using BankTransaction = Data.BankTransaction;
using CardAccount = Data.CardAccount;
using CardTransaction = Data.CardTransaction;
using Entity = Data.Entity;
using LoanAccount = Data.LoanAccount;
using LoanTransaction = Data.LoanTransaction;

namespace Business
{
    [PerformanceAspect(LimitSecs = 5)]
    public class WealthService : DBService, IWealthService
    {
        public IAsxService AsxService { get; set; }

        public TransferMatcher TransferMatcher { get; set; }
        public IDocumentService DocumentService { get; set; }

        public WealthService(DBContext context)
            : base(context)
        {
            if (TransferMatcher == null)
                TransferMatcher = new TransferMatcher();

            DocumentService = ObjectFactory.GetInstance<IDocumentService>();
        }

        public WealthItem GetWealthItem(int id)
        {
            var item = DB.WealthItems.FirstOrDefault(x => x.ID == id);
            return item;
        }

        public WealthItem GetWealthItem(int id, Entity ownerEntity)
        {
            var item = DB.WealthItems.FirstOrDefault(x => x.ID == id);
            if (item != null && item.Owners.AnyAndNotNull()
                   && item.Owners.Any(x => x.Entity != null && x.Entity.ID == ownerEntity.ID))
                return item;

            return null;
        }

        public WealthItem GetWealthItem(int id, Account account)
        {
            var entityIDs = from aa in account.Access
                            select aa.Entity.ID;

            var item = DB.WealthItems.FirstOrDefault(x => x.ID == id);
            if (item != null && item.Owners.AnyAndNotNull()
                   && item.Owners.Any(o => entityIDs.Contains(o.Entity.ID)))
                return item;

            return null;
        }

        public WealthItem GetWealthItem(int id, WealthItemClassification classification)
        {
            var item = GetWealthItem(id);
            return item != null && item.Classification == classification ? item : null;
        }

        public bool UpdateFinancialAccount(AccountBase item)
        {
            using (Profiler.Step("WealthService.UpdateFinancialAccount"))
            {
                var db = DB;

                try
                {
                    db.Entry(item).State = System.Data.Entity.EntityState.Modified;

                    db.SaveChanges();
                    return true;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    return false;
                }
            }
        }

        public IEnumerable<WealthItem> GetWealthItems(int accountID, bool includeDummyItems = false)
        {
            return GetWealthItems(DB.Accounts.Single(a => a.ID == accountID), WealthItemSortOrder.None, includeDummyItems: includeDummyItems);
        }

        //this will include hidden items
        public IEnumerable<WealthItem> GetWealthItems(Entity entity, bool includeDummyItems = false)
        {
            DB db = DB;

            var qry = (from i in db.WealthItems
                       let isOk = i.Owners.Any(o => o.Entity.ID == entity.ID)
                       where isOk
                       select i
                      )
                .Include(x => x.ToDos)
                .Include(x => x.CostHistory.Select(xx => xx.Owners.Select(xxx => xxx.Entity)))
                .Include(x => x.Owners.Select(xx => xx.Entity));

            var items = qry.ToList();

            if (!includeDummyItems)
                items = items.Where(x => !x.IsDummy).ToList();

            return items;
        }

        public IEnumerable<WealthItem> GetWealthItems<T>(Account account, bool includeDummyItems = false)
            where T : WealthItem
        {

            DB db = DB;
            var showHidden = account.GetAccountSettingCached().ShowHiddenWealthItem;

            var entityIDs = from aa in account.Access
                            select aa.Entity.ID;

            var qry = (from i in db.WealthItems
                       let isOk = i.Owners.Any(o => entityIDs.Contains(o.Entity.ID))
                       where isOk
                       orderby i.ListedShareASXCode
                       select i
                      )
                .Include(x => x.ToDos)
                .Include(x => x.CostHistory.Select(xx => xx.Owners.Select(xxx => xxx.Entity)))
                .Include(x => x.Owners.Select(xx => xx.Entity));

            var items = qry.OfType<T>().ToList();

            if (!includeDummyItems)
                items = items.Where(x => !x.IsDummy).ToList();

            return showHidden ? items : items.Where(x => !x.IsHidden);
        }

        public IEnumerable<WealthItem> GetWealthItems(int[] ids)
        {
            return DB.WealthItems.Where(x => ids.Contains(x.ID));
        }

        public NetWorthSummary GetNetWorthSummary(Account account, bool includeDummyItems = false, IList<WealthItem> wealthItems = null)
        {
            var netWorthSummary = new NetWorthSummary();

            var showHidden = account.GetAccountSettingCached().ShowHiddenWealthItem;
            var entityIDs = (from aa in account.Access.Where(x => !x.Entity.Relationship.HasValue || x.Entity.Relationship.Value != Relationship.NotListed)
                             select aa.Entity.ID).ToArray();

            if (wealthItems == null)
            {
                wealthItems = DB.WealthItems.Where(w => w.Owners.Any(r => entityIDs.Contains(r.Entity.ID))).ToList();
            }

            wealthItems = showHidden ? wealthItems : wealthItems.Where(x => !x.IsHidden).ToList();

            if (!includeDummyItems)
                wealthItems = wealthItems.Where(x => !x.IsDummy).ToList();

            netWorthSummary.HasWealthItem = wealthItems.Any();

            netWorthSummary.Own = wealthItems.Sum(t => t.ComputedValueWithSign >= 0 ? t.GetValueFor(t.ComputedValueWithSign, entityIDs) : 0);
            netWorthSummary.Owe = wealthItems.Sum(t => t.ComputedValueWithSign < 0 ? t.GetValueFor(t.ComputedValueWithSign, entityIDs) : 0);

            return netWorthSummary;
        }

        public IEnumerable<AccountBase> GetFinAccounts(Account account, bool includeDummyItems = false)
        {
            var finAccounts = new List<AccountBase>();
            var wealthItems = GetWealthItems(account, WealthItemSortOrder.None, includeDummyItems: includeDummyItems);
            finAccounts.AddRange(wealthItems.Select(t => t.BankAccount).Where(a => a != null));
            finAccounts.AddRange(wealthItems.Select(t => t.CardAccount).Where(a => a != null));
            finAccounts.AddRange(wealthItems.Select(t => t.LoanAccount).Where(a => a != null));
            finAccounts.AddRange(wealthItems.Select(t => t.InvestmentAccount).Where(a => a != null));
            return finAccounts;
        }

        public IEnumerable<WealthItem> GetFinAccountsWealthItems(Account account, bool includeDummyItems = false,
            bool includeInvestment = true)
        {
            return GetWealthItems(account, WealthItemSortOrder.None, includeDummyItems)
                .Where(x => x.BankAccount != null || x.CardAccount != null || x.LoanAccount != null ||
                            (includeInvestment && x.InvestmentAccount != null));
        }

        public IEnumerable<AccountBase> GetFinAccountsNonInvestment(Account account, bool includeDummyItems = false)
        {
            var finAccounts = new List<AccountBase>();
            var wealthItems = GetWealthItems(account, WealthItemSortOrder.None, includeDummyItems: includeDummyItems);
            finAccounts.AddRange(wealthItems.Select(t => t.BankAccount).Where(a => a != null));
            finAccounts.AddRange(wealthItems.Select(t => t.CardAccount).Where(a => a != null));
            finAccounts.AddRange(wealthItems.Select(t => t.LoanAccount).Where(a => a != null));
            return finAccounts;
        }

        public IEnumerable<AccountBase> GetFinAccountsCashflowOnly(Account account, bool includeDummyItems = false)
        {
            var finAccounts = new List<AccountBase>();
            var wealthItems = GetWealthItems(account, WealthItemSortOrder.None, includeDummyItems: includeDummyItems);
            finAccounts.AddRange(wealthItems.Select(t => t.BankAccount).Where(a => a != null));
            finAccounts.AddRange(wealthItems.Select(t => t.CardAccount).Where(a => a != null));
            return finAccounts;
        }

        public IEnumerable<InvestmentAccount> GetFinAccountsInvestmentOnly(Account account, bool includeDummyItems = false)
        {
            var finAccounts = new List<InvestmentAccount>();
            var wealthItems = GetWealthItems(account, WealthItemSortOrder.None, includeDummyItems: includeDummyItems);
            finAccounts.AddRange(wealthItems.Select(t => t.InvestmentAccount).Where(a => a != null));
            return finAccounts;
        }

        public IEnumerable<WealthItem> GetYodleeWealthItems(Account account, bool includeDummyItems = false)
        {
            return GetWealthItems(account, WealthItemSortOrder.None, includeDummyItems: includeDummyItems)
                .Where(w => w.HasYodleeData && w.UseDataFeed).ToList();
        }

        public IEnumerable<AccountBase> GetYodleeFinAccounts(Account account, bool includeDummyItems = false)
        {
            var finAccounts = new List<AccountBase>();
            var wealthItems = GetYodleeWealthItems(account, includeDummyItems);
            finAccounts.AddRange(wealthItems.Select(t => t.BankAccount).Where(a => a != null));
            finAccounts.AddRange(wealthItems.Select(t => t.CardAccount).Where(a => a != null));
            finAccounts.AddRange(wealthItems.Select(t => t.LoanAccount).Where(a => a != null));
            finAccounts.AddRange(wealthItems.Select(t => t.InvestmentAccount).Where(a => a != null));

            var yodleeFinAccounts = finAccounts.Where(t => t.YodleeItemAccountId.HasValue).ToList();
            return yodleeFinAccounts;
        }

        public AccountBase GetFinAccountBaseByTransactionAccountType(int id, TransactionAccountType accountType, Account account)
        {
            if (account == null)
                throw new ArgumentNullException("account");

            switch (accountType)
            {
                case TransactionAccountType.BANK:
                    var bankAccount = GetBankAccount(id);
                    if (bankAccount == null)
                        throw new Exception(string.Format("No {0} account found with id {1}", accountType, id));
                    if (!account.HasAccessTo(bankAccount))
                        throw new UnauthorizedAccessException(string.Format("User (AccountID {0}) doesn't have access to {1} account id {2}", account.ID, accountType, id));
                    return bankAccount;

                case TransactionAccountType.CARD:
                    var cardAccount = GetCardAccount(id);
                    if (cardAccount == null)
                        throw new Exception(string.Format("No {0} account found with id {1}", accountType, id));
                    if (!account.HasAccessTo(cardAccount))
                        throw new UnauthorizedAccessException(string.Format("User (AccountID {0}) doesn't have access to {1} account id {2}", account.ID, accountType, id));
                    return cardAccount;

                case TransactionAccountType.LOAN:
                    var loanAccount = GetLoanAccount(id);
                    if (loanAccount == null)
                        throw new Exception(string.Format("No {0} account found with id {1}", accountType, id));
                    if (!account.HasAccessTo(loanAccount))
                        throw new UnauthorizedAccessException(string.Format("User (AccountID {0}) doesn't have access to {1} account id {2}", account.ID, accountType, id));
                    return loanAccount;

                case TransactionAccountType.INVESTMENT:
                    var investmentAccount = GetInvestmentAccount(id);
                    if (investmentAccount == null)
                        throw new Exception(string.Format("No {0} account found with id {1}", accountType, id));
                    if (!account.HasAccessTo(investmentAccount))
                        throw new UnauthorizedAccessException(string.Format("User (AccountID {0}) doesn't have access to {1} account id {2}", account.ID, accountType, id));
                    return investmentAccount;
            }

            throw new Exception("Can't find fin account for TransactionAccountType " + accountType);
        }


        public bool HaveWealthItems(Account account, bool includeDummyItems = false)
        {
            var entityIDs = account.Access.Select(e => e.Entity).Select(e => e.ID).ToList();
            return DB.WealthItemOwners.Any(x => entityIDs.Contains(x.Entity.ID) && (includeDummyItems || !x.Item.IsDummy));
        }

        public bool HaveFinAccounts(Account account, bool onlyYodlee = false, bool includeDummyItems = false)
        {
            var entityIDs = account.Access.Select(e => e.Entity).Select(e => e.ID).ToList();

            return DB.WealthItemOwners.Any(w => entityIDs.Contains(w.Entity.ID) &&
                                                    ((w.Item.BankAccount_ID != null ||
                                                      w.Item.CardAccount_ID != null ||
                                                      w.Item.LoanAccount_ID != null ||
                                                      w.Item.InvestmentAccount_ID != null)
                                                      && (!onlyYodlee || w.Item.HasYodleeData)
                                                      && (includeDummyItems || !w.Item.IsDummy))
                    );



            //if (DB.BankAccountOwners.Any(w => entityIDs.Contains(w.Entity.ID) &&
            //    (!onlyYodlee || w.BankAccount.YodleeItemAccountId != null)))
            //    return true;

            //if (DB.CardAccountOwners.Any(w => entityIDs.Contains(w.Entity.ID) &&
            //    (!onlyYodlee || w.CardAccount.YodleeItemAccountId != null)))
            //    return true;

            //if (DB.LoanAccountOwners.Any(w => entityIDs.Contains(w.Entity.ID) &&
            //    (!onlyYodlee || w.LoanAccount.YodleeItemAccountId != null)))
            //    return true;

            //if (DB.InvestmentAccountOwners.Any(w => entityIDs.Contains(w.Entity.ID) &&
            //    (!onlyYodlee || w.InvestmentAccount.YodleeItemAccountId != null)))
            //    return true;

            //return false;
        }

        public Bank GetFinAccountBank(AccountBase financialAccount)
        {
            return DB.Banks.SingleOrDefault(b => b.ContentServiceID == financialAccount.ContentServiceId);
        }





        public IEnumerable<WealthItem> GetWealthItemsOnly(Account account, bool excludeHiddenItem = false, bool includeDummyItems = false)
        {
            try
            {
                DB db = DB;

                var entityIDs = from aa in account.Access
                                select aa.Entity.ID;


                var qry = db.WealthItems.Where(x => x.Owners.Any(o => entityIDs.Contains(o.Entity.ID)));

                var items = qry.ToList();

                var showHidden = account.GetAccountSettingCached().ShowHiddenWealthItem;
                items = showHidden ? items : items.Where(x => !x.IsHidden).ToList();

                if (excludeHiddenItem)
                {
                    items = items.Where(x => !x.IsHidden).ToList();
                }

                if (!includeDummyItems)
                    items = items.Where(x => !x.IsDummy).ToList();

                return items;

            }
            catch (Exception ex)
            {
                ExceptionHelper.HandleException(ex);
            }
            return null;
        }

        public IEnumerable<WealthItem> GetSuperWealthItems(bool includeDummyItems = false)
        {
            const int superCategory = (int)AssetWealthItemCategory.Super;
            var superWealthItems = DB.WealthItems.Where(x => x.CategoryInternal == superCategory && (includeDummyItems || !x.IsDummy)).OfType<AssetWealthItem>();
            return superWealthItems;
        }

        /// <summary>
        /// Get all of the assets of a specified type (optional) for a number of accounts, 
        /// broken up in a dictionary by accountID. Useful for reports
        /// </summary>
        /// <param name="accountIdList"></param>
        /// <param name="assetWealthItemCategory"></param>
        /// <param name="includeDummyItems"></param>
        /// <returns></returns>
        public IDictionary<int, List<WealthItem>> GetAssetWealthItemsDictionaryByAccountIDs(
            List<int> accountIdList, AssetWealthItemCategory? assetWealthItemCategory = null, bool includeDummyItems = false)
        {
            var wiCatlist = new List<AssetWealthItemCategory>();
            if (assetWealthItemCategory.HasValue)
                wiCatlist.Add(assetWealthItemCategory.Value);

            return GetAssetWealthItemsDictionaryByAccountIDs(accountIdList, wiCatlist, includeDummyItems);
        }

        /// <summary>
        /// Get all of the assets of a specified type (optional) for a number of accounts, 
        /// broken up in a dictionary by accountID. Useful for reports
        /// </summary>
        /// <param name="accountIdList"></param>
        /// <param name="assetWeatlhItemCategoryList"></param>
        /// <param name="includeDummyItems"></param>
        /// <returns></returns>
        public IDictionary<int, List<WealthItem>> GetAssetWealthItemsDictionaryByAccountIDs(
                List<int> accountIdList, List<AssetWealthItemCategory> assetWeatlhItemCategoryList, bool includeDummyItems = false)
        {
            if (accountIdList == null)
                accountIdList = new List<int>();
            if (assetWeatlhItemCategoryList == null)
                assetWeatlhItemCategoryList = new List<AssetWealthItemCategory>();

            // Get dictionary of EntityID -> AccountID
            var accounts = DB.Accounts.Where(x => accountIdList.Contains(x.ID))
                .Include(a => a.Access)
                .Include(a => a.Access.Select(y => y.Entity)).ToList(); // .Where(y => y != null)

            var entityIdBigList = new List<int>();
            var entityIdToAccountIdDict = new Dictionary<int, int>();
            foreach (var account in accounts)
            {
                foreach (var access in account.Access)
                {
                    entityIdToAccountIdDict[access.EntityID] = account.ID;
                    entityIdBigList.Add(access.EntityID);
                }
            }

            // Get all WealthItems for any of the accounts including WealthItemOwners
            IEnumerable<WealthItem> wealthItems;

            if (assetWeatlhItemCategoryList.Any())
            {
                // get only specified type of asset wealth items
                var assetWeathItemCategoryIntList = assetWeatlhItemCategoryList.Select(x => (int)x).ToList();

                wealthItems = DB.WealthItems
                            .Where(x => (assetWeathItemCategoryIntList.Contains(x.CategoryInternal))
                                && x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
                            .OfType<AssetWealthItem>()
                            .Include(w => w.Owners)
                            .Include(w => w.InvestmentAccount);
            }
            else // get all asset types
            {
                wealthItems = DB.WealthItems
                    .Where(x => x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
                    .OfType<AssetWealthItem>()
                    .Include(w => w.Owners);
            }

            if (!includeDummyItems)
                wealthItems = wealthItems.Where(x => !x.IsDummy).ToList();

            // combine results
            var resultDict = accountIdList.ToDictionary(x => x, y => new List<WealthItem>());
            int acctId;
            WealthItemOwner wio;
            foreach (var wi in wealthItems)
            {
                wio = wi.Owners.FirstOrDefault();  // doesn't matter if there are multiple owners, they will all map to the same accountId
                if (wio != null)
                {
                    acctId = entityIdToAccountIdDict[wio.Entity_ID];    // Get the account ID for this WealthItem
                    resultDict[acctId].Add(wi);
                }
            }

            return resultDict;
        }

        /// <summary>
        /// Get all of the liabilities of a specified type (optional) for a number of accounts, 
        /// broken up in a dictionary by accountID. Useful for reports
        /// </summary>
        /// <param name="accountIdList"></param>
        /// <param name="liabilityWealthItemCategory"></param>
        /// <param name="includeDummyItems"></param>
        /// <returns>Dictionary of AccountID to list of their LiabilityWealthItems</returns>
        public IDictionary<int, List<WealthItem>> GetLiabilityWealthItemsDictionaryByAccountIDs(
            List<int> accountIdList, LiabilityWealthItemCategory? liabilityWealthItemCategory = null, bool includeDummyItems = false)
        {
            var wiCatlist = new List<LiabilityWealthItemCategory>();
            if (liabilityWealthItemCategory.HasValue)
                wiCatlist.Add(liabilityWealthItemCategory.Value);

            return GetLiabilityWealthItemsDictionaryByAccountIDs(accountIdList, wiCatlist, includeDummyItems);
        }

        /// <summary>
        /// Get all of the liabilities of a specified type (optional) for a number of accounts, 
        /// broken up in a dictionary by accountID. Useful for reports
        /// </summary>
        /// <param name="accountIdList"></param>
        /// <param name="liabilityWealthItemCategoryList"></param>
        /// <param name="includeDummyItems"></param>
        /// <returns>Dictionary of AccountID to list of their LiabilityWealthItems</returns>
        public IDictionary<int, List<WealthItem>> GetLiabilityWealthItemsDictionaryByAccountIDs(
               List<int> accountIdList, List<LiabilityWealthItemCategory> liabilityWealthItemCategoryList, bool includeDummyItems = false)
        {
            if (accountIdList == null)
                accountIdList = new List<int>();
            if (liabilityWealthItemCategoryList == null)
                liabilityWealthItemCategoryList = new List<LiabilityWealthItemCategory>();

            // Get dictionary of EntityID -> AccountID
            var accounts = DB.Accounts.Where(x => accountIdList.Contains(x.ID))
                .Include(a => a.Access)
                .Include(a => a.Access.Select(y => y.Entity)).ToList(); // .Where(y => y != null)

            var entityIdBigList = new List<int>();
            var entityIdToAccountIdDict = new Dictionary<int, int>();
            foreach (var account in accounts)
            {
                foreach (var access in account.Access)
                {
                    entityIdToAccountIdDict[access.EntityID] = account.ID;
                    entityIdBigList.Add(access.EntityID);
                }
            }

            // Get all WealthItems for any of the accounts including WealthItemOwners
            IEnumerable<WealthItem> wealthItems;

            if (liabilityWealthItemCategoryList.Any())
            {
                // get only specified type of liability wealth items
                var liabilityWealthItemCategoryIntList = liabilityWealthItemCategoryList.Select(x => (int)x).ToList();

                wealthItems = DB.WealthItems
                            .Where(x => (liabilityWealthItemCategoryIntList.Contains(x.CategoryInternal))
                                && x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
                            .OfType<LiabilityWealthItem>()
                            .Include(w => w.Owners)
                            .Include(w => w.InvestmentAccount);
            }
            else // get all liability types
            {
                wealthItems = DB.WealthItems
                    .Where(x => x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
                    .OfType<LiabilityWealthItem>()
                    .Include(w => w.Owners);
            }

            if (!includeDummyItems)
                wealthItems = wealthItems.Where(x => !x.IsDummy).ToList();

            // combine results
            var resultDict = accountIdList.ToDictionary(x => x, y => new List<WealthItem>());
            int acctId;
            WealthItemOwner wio;
            foreach (var wi in wealthItems)
            {
                wio = wi.Owners.FirstOrDefault();  // doesn't matter if there are multiple owners, they will all map to the same accountId
                if (wio != null)
                {
                    acctId = entityIdToAccountIdDict[wio.Entity_ID];    // Get the account ID for this WealthItem
                    resultDict[acctId].Add(wi);
                }
            }

            return resultDict;
        }

        ///// <summary>
        ///// Get all of the liabilities of a specified type (optional) for a number of accounts, 
        ///// broken up in a dictionary by accountID. Useful for reports
        ///// </summary>
        ///// <param name="accountIdList"></param>
        ///// <param name="liabilityWealthItemCategory"></param>
        ///// <param name="liabilityWealthItemCategory2"></param>
        ///// <returns>Dictionary of AccountID to list of their LiabilityWealthItems</returns>
        //public IDictionary<int, List<WealthItem>> GetLiabilityWealthItemsDictionaryByAccountIDs(
        //        List<int> accountIdList, LiabilityWealthItemCategory? liabilityWealthItemCategory,
        //    LiabilityWealthItemCategory? liabilityWealthItemCategory2 = null)
        //{
        //    if (accountIdList == null)
        //        accountIdList = new List<int>();
        //    // Get dictionary of EntityID -> AccountID
        //    var accounts = DB.Accounts.Where(x => accountIdList.Contains(x.ID))
        //        .Include(a => a.Access)
        //        .Include(a => a.Access.Select(y => y.Entity)).ToList(); // .Where(y => y != null)

        //    var entityIdBigList = new List<int>();
        //    var entityIdToAccountIdDict = new Dictionary<int, int>();
        //    foreach (var account in accounts)
        //    {
        //        foreach (var access in account.Access)
        //        {
        //            entityIdToAccountIdDict[access.EntityID] = account.ID;
        //            entityIdBigList.Add(access.EntityID);
        //        }
        //    }

        //    // Get all WealthItems for any of the accounts including WealthItemOwners
        //    IEnumerable<WealthItem> wealthItems;
        //    if (liabilityWealthItemCategory.HasValue)    // get only specified type of liability wealth items
        //    {
        //        if (liabilityWealthItemCategory2.HasValue)
        //        {
        //            wealthItems = DB.WealthItems
        //                .Where(x => (x.CategoryInternal == (int)liabilityWealthItemCategory.Value || x.CategoryInternal == (int)liabilityWealthItemCategory2.Value)
        //                        && x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
        //                .OfType<LiabilityWealthItem>()
        //                .Include(w => w.Owners);
        //        }
        //        else
        //        {
        //            wealthItems = DB.WealthItems
        //                .Where(x => x.CategoryInternal == (int)liabilityWealthItemCategory.Value && x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
        //                .OfType<LiabilityWealthItem>()
        //                .Include(w => w.Owners);
        //        }

        //    }
        //    else
        //    {
        //        wealthItems = DB.WealthItems
        //            .Where(x => x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
        //            .OfType<LiabilityWealthItem>()
        //            .Include(w => w.Owners);
        //    }

        //    // combine results
        //    var resultDict = accountIdList.ToDictionary(x => x, y => new List<WealthItem>());
        //    int acctId;
        //    WealthItemOwner wio;
        //    foreach (var wi in wealthItems)
        //    {
        //        wio = wi.Owners.FirstOrDefault();  // doesn't matter if there are multiple owners, they will all map to the same accountId
        //        if (wio != null)
        //        {
        //            acctId = entityIdToAccountIdDict[wio.Entity_ID];    // Get the account ID for this WealthItem
        //            resultDict[acctId].Add(wi);
        //        }
        //    }

        //    return resultDict;
        //}

        public IDictionary<int, List<WealthItem>> GetLiabilityWealthItemsDictionaryByAccountIDs(
                List<int> accountIdList, List<string> liabilityWealthItemCategoryList, bool includeDummyItems = false)
        {
            if (accountIdList == null)
                accountIdList = new List<int>();
            // Get dictionary of EntityID -> AccountID
            var accounts = DB.Accounts.Where(x => accountIdList.Contains(x.ID))
                .Include(a => a.Access)
                .Include(a => a.Access.Select(y => y.Entity)).ToList(); // .Where(y => y != null)

            var entityIdBigList = new List<int>();
            var entityIdToAccountIdDict = new Dictionary<int, int>();
            foreach (var account in accounts)
            {
                foreach (var access in account.Access)
                {
                    entityIdToAccountIdDict[access.EntityID] = account.ID;
                    entityIdBigList.Add(access.EntityID);
                }
            }

            // Get all WealthItems for any of the accounts including WealthItemOwners
            IEnumerable<WealthItem> wealthItems;
            if (liabilityWealthItemCategoryList.AnyAndNotNull())    // get only specified type of liability wealth items
            {
                wealthItems =
                    DB.WealthItems.Where(x => x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
                        .OfType<LiabilityWealthItem>()
                        .Include(w => w.Owners)
                        .ToList()
                        .Where(x => liabilityWealthItemCategoryList.Contains(x.CategoryName));
            }
            else
            {
                wealthItems = DB.WealthItems
                .Where(x => x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
                .OfType<LiabilityWealthItem>()
                .Include(w => w.Owners);
            }

            if (!includeDummyItems)
                wealthItems = wealthItems.Where(x => !x.IsDummy).ToList();

            // combine results
            var resultDict = accountIdList.ToDictionary(x => x, y => new List<WealthItem>());
            int acctId;
            WealthItemOwner wio;
            foreach (var wi in wealthItems)
            {
                wio = wi.Owners.FirstOrDefault();  // doesn't matter if there are multiple owners, they will all map to the same accountId
                if (wio != null)
                {
                    acctId = entityIdToAccountIdDict[wio.Entity_ID];    // Get the account ID for this WealthItem
                    resultDict[acctId].Add(wi);
                }
            }

            return resultDict;
        }

        public IDictionary<int, List<WealthItem>> GetAssetWealthItemsDictionaryByAccountIDs(
             List<int> accountIdList, List<string> assetWealthItemCategoryList, bool includeDummyItems = false)
        {
            if (accountIdList == null)
                accountIdList = new List<int>();
            // Get dictionary of EntityID -> AccountID
            var accounts = DB.Accounts.Where(x => accountIdList.Contains(x.ID))
                .Include(a => a.Access)
                .Include(a => a.Access.Select(y => y.Entity)).ToList(); // .Where(y => y != null)

            var entityIdBigList = new List<int>();
            var entityIdToAccountIdDict = new Dictionary<int, int>();
            foreach (var account in accounts)
            {
                foreach (var access in account.Access)
                {
                    entityIdToAccountIdDict[access.EntityID] = account.ID;
                    entityIdBigList.Add(access.EntityID);
                }
            }

            // Get all WealthItems for any of the accounts including WealthItemOwners
            IEnumerable<WealthItem> wealthItems;
            if (assetWealthItemCategoryList.AnyAndNotNull())    // get only specified type of asset wealth items
            {
                wealthItems =
                    DB.WealthItems.Where(x => x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
                        .OfType<AssetWealthItem>()
                        .Include(w => w.Owners)
                        .Include(w => w.InvestmentAccount)
                        .ToList()
                        .Where(x => assetWealthItemCategoryList.Contains(x.CategoryName));
                ;
            }
            else
            {
                wealthItems = DB.WealthItems
                .Where(x => x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
                .OfType<AssetWealthItem>()
                .Include(w => w.Owners);
            }

            if (!includeDummyItems)
                wealthItems = wealthItems.Where(x => !x.IsDummy).ToList();

            // combine results
            var resultDict = accountIdList.ToDictionary(x => x, y => new List<WealthItem>());
            int acctId;
            WealthItemOwner wio;
            foreach (var wi in wealthItems)
            {
                wio = wi.Owners.FirstOrDefault();  // doesn't matter if there are multiple owners, they will all map to the same accountId
                if (wio != null)
                {
                    acctId = entityIdToAccountIdDict[wio.Entity_ID];    // Get the account ID for this WealthItem
                    resultDict[acctId].Add(wi);
                }
            }

            return resultDict;
        }

        //public IDictionary<int, List<AssetWealthItem>> GetSuperWealthItemsByAccountIDs(List<int> accountIdList)
        //{
        //    if(accountIdList == null)
        //        accountIdList = new List<int>();
        //    // Get dictionary of EntityID -> AccountID
        //    var accounts = DB.Accounts.Where(x => accountIdList.Contains(x.ID))
        //        .Where(a => a.Access != null)
        //        .Include(a => a.Access)
        //        .Include(a => a.Access.Select(y => y.Entity));

        //    var entityIdBigList = new List<int>();
        //    var entityIdToAccountIdDict = new Dictionary<int, int>();
        //    foreach (var account in accounts)
        //    {
        //        foreach (var access in account.Access)
        //        {
        //            entityIdToAccountIdDict[access.EntityID] = account.ID;
        //            entityIdBigList.Add(access.EntityID);
        //        }
        //    }

        //    // Get all super WealthItems for any of the accounts including WealthItemOwners
        //    const int superCategory = (int)AssetWealthItemCategory.Super;
        //    var superWealthItems = DB.WealthItems
        //        .Where(x => x.CategoryInternal == superCategory && x.Owners.Any(o => entityIdBigList.Contains(o.Entity.ID)))
        //        .OfType<AssetWealthItem>()
        //        .Include(w => w.Owners);

        //    // combine results
        //    var resultDict = accountIdList.ToDictionary(x => x, y => new List<AssetWealthItem>());
        //    int acctId;
        //    WealthItemOwner wio;
        //    foreach (var superWi in superWealthItems)
        //    {
        //        wio = superWi.Owners.FirstOrDefault();  // doesn't matter if there are multiple owners, they will all map to the same accountId
        //        if (wio != null)
        //        {
        //            acctId = entityIdToAccountIdDict[wio.Entity_ID];    // Get the account ID for this super WealthItem
        //            resultDict[acctId].Add(superWi);
        //        }
        //    }

        //    return resultDict;
        //}

        //new Dictionary<int, List<AssetWealthItem>>();
        //foreach (var accountId in accountIdList)
        //{
        //    resultDict[accountId] = new List<AssetWealthItem>();
        //}

        public IEnumerable<WealthItem> GetLiabilityWealthItems(Account account, bool includeDummyItems = false)
        {
            var items = GetItems(account, includeDummyItems);
            return items.OfType<LiabilityWealthItem>().ToList();
        }

        public List<string> GetDiscoverDebtReviewExistingCategories(Account account, bool includeDummyItems = false)
        {
            return GetLiabilityWealthItems(account, includeDummyItems).Distinct(x => x.CategoryName).Select(x => x.CategoryName).ToList();

        }

        public IEnumerable<WealthItem> GetSuperWealthItems(IEnumerable<int> ownerEntityIDs, bool includeDummyItems = false)
        {
            const int superCategory = (int)AssetWealthItemCategory.Super;
            var superWealthItems = (from ow in DB.WealthItemOwners
                                    where ownerEntityIDs.Contains(ow.Entity_ID)
                                    where ow.Item.CategoryInternal == superCategory
                                    select ow.Item).OfType<AssetWealthItem>().ToList();

            if (!includeDummyItems)
                superWealthItems = superWealthItems.Where(x => !x.IsDummy).ToList();

            return superWealthItems;
        }

        public IEnumerable<WealthItem> GetSuperAndPortfolioWealthItems(bool includeDummyItems = false)
        {
            const int superCategory = (int)AssetWealthItemCategory.Super;
            const int portfolioCategory = (int)AssetWealthItemCategory.Portfolio;
            var wealthItems = DB.WealthItems.Where(
                x => (x.CategoryInternal == superCategory || x.CategoryInternal == portfolioCategory) && (includeDummyItems || !x.IsDummy)).OfType<AssetWealthItem>();
            return wealthItems;
        }

        public IEnumerable<WealthItem> GetSuperAndPortfolioWealthItems(IEnumerable<int> ownerEntityIDs, bool includeDummyItems = false)
        {
            const int superCategory = (int)AssetWealthItemCategory.Super;
            const int portfolioCategory = (int)AssetWealthItemCategory.Portfolio;
            var wealthItems = DB.WealthItemOwners.Where(x =>
                ownerEntityIDs.Contains(x.Entity_ID) &&
                (x.Item.CategoryInternal == superCategory || x.Item.CategoryInternal == portfolioCategory))
                .Select(ow => ow.Item).OfType<AssetWealthItem>().ToList();

            if (!includeDummyItems)
                wealthItems = wealthItems.Where(x => !x.IsDummy).ToList();

            return wealthItems;
        }

        public IList<WealthItem> GetWealthItems(int partnerAgentId, IEnumerable<int> entityIds, bool bypassCache = false)
        {
            var cacheKey = CacheHelperKeys.GetCacheKeyForGetWealthItems_ByPartnerAgentId(partnerAgentId);
            var wealthItems = CacheHelper.GetItemFromCache(cacheKey) as IList<WealthItem>;

            if (wealthItems == null || bypassCache)
            {
                wealthItems = DB.WealthItems.Where(wi => wi.Owners.Any(o => entityIds.Contains(o.Entity.ID)) && !wi.IsDummy).ToList();

                CacheHelper.AddItemToCache(cacheKey, wealthItems, ConfigHelper.TryGetOrDefault("DefaultCacheTime", 3600));
            }
            return wealthItems;
        }

        public List<WealthItem> GetWealthItems(Account account, WealthItemSortOrder wealthItemSortOrder = WealthItemSortOrder.None, 
            bool excludeHiddenItem = false, bool includeDummyItems = false)
        {
            DB db = DB;

            var entityIDs = from aa in account.Access
                            select aa.Entity.ID;

            var items = db.WealthItems.Where(r => r.Owners.Any(o => entityIDs.Contains(o.Entity.ID))).ToList();
                    
            var showHidden = account.GetAccountSettingCached().ShowHiddenWealthItem;

            items = showHidden ? items : items.Where(x => !x.IsHidden).ToList();

            if (excludeHiddenItem)
                items = items.Where(x => !x.IsHidden).ToList();

            if (!includeDummyItems)
                items = items.Where(x => !x.IsDummy).ToList();
            
            // do ordering last after possibly removing items to improve performance
            switch (wealthItemSortOrder)
            {
                case WealthItemSortOrder.None:
                    break;
                case WealthItemSortOrder.DescendingComputedValue:
                    items = items.OrderByDescending(r => r.ComputedValue).ToList();
                    break;
                case WealthItemSortOrder.AscendingComputedValue:
                    items = items.OrderBy(r => r.ComputedValue).ToList();
                    break;
                case WealthItemSortOrder.DescendingComputedValueWithSign:
                    items = items.OrderByDescending(r => r.ComputedValueWithSign).ToList();
                    break;
                case WealthItemSortOrder.AscendingComputedValueWithSign:
                    items = items.OrderBy(r => r.ComputedValueWithSign).ToList();
                    break;
            }

            return items;
        }


        public WealthItem GetOrCreateDummyTaxFinAcc(Account account)
        {
            var taxDummyAccName = ConfigHelper.TryGetOrDefault("TaxDummyAccName", "Tax Assistant bank account");
            var wealthItem = GetDummyWealthItems(account).FirstOrDefault(x => x.Name.Contains(taxDummyAccName));
            var entity = account.GetEntity();
            if (wealthItem == null)
            {
                var bankWI = new AssetWealthItem
                {
                    Created = DateTime.Now,
                    LastReviewed = DateTime.Now,
                    LifestyleOrInvestment = LifestyleOrInvestment.Investment,
                    Name = taxDummyAccName,
                    ReportingCategory = WealthReportingCategory.Cash,
                    Owners = WealthItemOwner.Sole(entity),
                    ValuationDate = DateTime.Now,
                    ProjectedGrowthRate = 0f,
                    ProjectedIncomeRate = 0f,
                    UseDataFeed = false,
                    HasYodleeData = false,
                    ListedShareNumberOfShares = 10,
                    ListedShareAverageUnitCostPerShare = 120.32m,
                    Value = 0,
                    Category = AssetWealthItemCategory.BankAccount,
                    IsDummy = true
                };

                var bankAccount = new BankAccount
                {
                    AccountName = taxDummyAccName,
                    AccountClassification = AccountClassification.OTHER.ToString(),
                    AvailableBalance = 0,
                    CurrentBalance = 0,
                    //PaperlessStatementFlag = 0,
                    Transactions = new Collection<BankTransaction>(),
                    ContentServiceId = 0
                };

                bankAccount.SetOwner<BankAccount, BankAccountOwner>(entity);
                bankWI.BankAccount = bankAccount;

                //var cardWI = new LiabilityWealthItem
                //{
                //    Created = DateTime.Now,
                //    LastReviewed = DateTime.Now,
                //    LifestyleOrInvestment = LifestyleOrInvestment.Investment,
                //    Name = "Tax return credit card account",
                //    ReportingCategory = WealthReportingCategory.TaxDeductible,
                //    Owners = WealthItemOwner.Sole(entity),
                //    ValuationDate = DateTime.Now,
                //    ProjectedGrowthRate = 0f,
                //    ProjectedIncomeRate = 0f,
                //    UseDataFeed = false,
                //    HasYodleeData = false,
                //    ListedShareNumberOfShares = 10,
                //    ListedShareAverageUnitCostPerShare = 120.32m,
                //    Value = 0,
                //    Category = LiabilityWealthItemCategory.CreditCard,
                //};
                //var cardAccount = new CardAccount
                //{
                //    AccountName = string.Format("{0} {1}", taxDummyAccName, "Credit card account"),
                //    AmountDue = 0,
                //    Transactions = new Collection<CardTransaction>(),
                //    ContentServiceId = 0

                //};
                //cardAccount.SetOwner<CardAccount, CardAccountOwner>(entity);
                //cardWI.CardAccount = cardAccount;
                int? id;
                AddWealthItem(bankWI, out id);
                return bankWI;
            }
            return wealthItem;
        }

        public IEnumerable<WealthItem> GetDummyWealthItems(Account account)
        {
            DB db = DB;

            var entityIDs = from aa in account.Access
                            select aa.Entity.ID;

            var items = db.WealthItems.Where(r => r.Owners.Any(o => entityIDs.Contains(o.Entity.ID)) && r.IsDummy)
                .ToList();

            var showHidden = account.GetAccountSettingCached().ShowHiddenWealthItem;

            items = showHidden ? items : items.Where(x => !x.IsHidden).ToList();
            return items;
        }


        public List<WealthItem> GetWealthItems(Account account, string category, 
            WealthItemSortOrder wealthItemSortOrder = WealthItemSortOrder.None, bool includeDummyItems = false)
        {
            return
                GetWealthItems(account, wealthItemSortOrder, includeDummyItems: includeDummyItems)
                    .Where(w => w.WealthItemCategory.GetDescription()
                         .Equals(category, StringComparison.InvariantCultureIgnoreCase)).
                     ToList();
        }



        public decimal GetSuperAmount(Account account, bool includeDummyItems = false)
        {
            var items = GetItems(account, includeDummyItems);
            //RINO: use CategoryInternal instead of Category
            return
                items.OfType<AssetWealthItem>()
                     .Where(x => x.CategoryInternal == (int)AssetWeathItemType.Super)
                     .Sum(v => (decimal?)v.Value) ?? 0m;
        }

        //RINO 11May2012: Corrected value for Listed Shares
        public decimal GetWealthAmount<T>(Account account, bool includeDummyItems = false)
            where T : WealthItem
        {
            var items = GetItems(account, includeDummyItems);

            if (typeof(T) == typeof(LiabilityWealthItem))
            {
                return items.OfType<T>().Sum(x => (decimal?)x.Value) ?? 0m;
            }

            return items.Include(x => x.ListedCompany)
                        .OfType<T>()
                        .Sum(x => (x.CategoryInternal == (int)AssetWeathItemType.ListedShare && x.UseDataFeed)
                                      ? x.ListedCompany.LatestPrice * x.ListedShareNumberOfShares
                                      : x.Value) ?? 0m;
        }

        public bool AddManualWealthItem(WealthItem item, Entity entity, out int? id, string bankName, long contentServiceId = 0)
        {
            const string defaultBankName = "Manually added bank";
            if (String.IsNullOrEmpty(bankName)) bankName = defaultBankName;

            var assetWealthItem = item as AssetWealthItem;
            if (assetWealthItem != null)
            {
                switch (assetWealthItem.Category)
                {
                    case AssetWealthItemCategory.BankAccount:
                    case AssetWealthItemCategory.TermDeposits:
                        var bankAccount = new BankAccount
                        {
                            AccountName = bankName,
                            AccountClassification = AccountClassification.OTHER.ToString(),
                            AvailableBalance = (double)item.Value,
                            CurrentBalance = (double)item.Value,
                            //PaperlessStatementFlag = 0,
                            Transactions = new Collection<BankTransaction>(),
                            ContentServiceId = contentServiceId
                        };

                        bankAccount.SetOwner<BankAccount, BankAccountOwner>(entity);
                        item.BankAccount = bankAccount;
                        break;

                    case AssetWealthItemCategory.Super:
                    case AssetWealthItemCategory.Portfolio:
                        var investmentAccount = new InvestmentAccount
                        {
                            AccountName = bankName,
                            TotalBalance = (double)item.Value,
                            Transactions = new Collection<InvestmentTransaction>(),

                        };
                        investmentAccount.SetOwner<InvestmentAccount, InvestmentAccountOwner>(entity);
                        item.InvestmentAccount = investmentAccount;
                        break;
                }
            }

            var liabilityWealthItem = item as LiabilityWealthItem;
            if (liabilityWealthItem != null)
            {
                switch (liabilityWealthItem.Category)
                {
                    case LiabilityWealthItemCategory.CreditCard:
                        var cardAccount = new CardAccount
                        {
                            AccountName = bankName,
                            AmountDue = (double)item.Value,
                            Transactions = new Collection<CardTransaction>(),
                            ContentServiceId = contentServiceId

                        };
                        cardAccount.SetOwner<CardAccount, CardAccountOwner>(entity);
                        item.CardAccount = cardAccount;
                        break;
                    case LiabilityWealthItemCategory.HomeMortgage:
                    case LiabilityWealthItemCategory.InvestmentLoan:
                    case LiabilityWealthItemCategory.CarLoan:
                    case LiabilityWealthItemCategory.PersonalLoan:
                        var loanAccount = new LoanAccount
                        {
                            TypeLoan = "OTHER",
                            AccountName = bankName,
                            AmountDue = (double)item.Value,
                        };
                        loanAccount.SetOwner<LoanAccount, LoanAccountOwner>(entity);
                        item.LoanAccount = loanAccount;
                        break;
                }
            }

            return AddWealthItems(new[] { item }, out id);
        }

        public bool AddWealthItem(WealthItem item, out int? id)
        {
            return AddWealthItems(new[] { item }, out id);
        }


        //DEPRECATED
        //private void AddTagForProperty(WealthItem wealthItem)
        //{
        //    var accountOwner = wealthItem.Owners.Select(o => o.Entity);
        //    var entities = (from own in accountOwner
        //                    group own by own.ID
        //                        into grp
        //                        select grp.First());

        //    foreach (var entity in entities)
        //    {
        //        var account =
        //        DB.Accounts.SingleOrDefault(
        //            a => a.Access.Any(t => t.IsCreator && t.Entity.ID == entity.ID));
        //        var tagExist = DB.Flags.Any(x => x.Account_ID == account.ID && x.Description == wealthItem.AddressStreet1);
        //        if (!tagExist && account != null)
        //        {
        //            var newFlag = new Flag() { Account_ID = account.ID, Description = wealthItem.AddressStreet1 };
        //            DB.Flags.Add(newFlag);

        //        }
        //    }
        //}

        [PerformanceAspect(LimitSecs = 5)]
        public bool AddWealthItems(WealthItem[] items, out int? id)
        {
            if (items == null || !items.Any())
            {
                id = null;
                return false;
            }
            try
            {
                var itemHasYodleeData = false;

                foreach (var wealthItem in items)
                {
                    DB.WealthItems.Add(wealthItem);

                    //DEPRECATED
                    //if (wealthItem.ReportingCategory == WealthReportingCategory.Property)
                    //{
                    //    AddTagForProperty(wealthItem);
                    //}


                    SetTransactionTag<BankAccount, BankTransaction>(wealthItem.BankAccount, DB);
                    SetTransactionTag<CardAccount, CardTransaction>(wealthItem.CardAccount, DB);
                    SetTransactionTag<InvestmentAccount, InvestmentTransaction>(wealthItem.InvestmentAccount, DB);
                    SetTransactionTag<LoanAccount, LoanTransaction>(wealthItem.LoanAccount, DB);

                    itemHasYodleeData = itemHasYodleeData || wealthItem.HasYodleeData;
                }

                DB.SaveChanges();


                id = items.Last().ID;

                // Match transfer transactions
                if (itemHasYodleeData)
                {

                    var owners = new List<Entity>();

                    foreach (var wealthItem in items.
                        Where(
                            wi =>
                            wi.BankAccount != null || wi.CardAccount != null || wi.LoanAccount != null ||
                            wi.InvestmentAccount != null).ToList())
                    {
                        owners.AddRange(wealthItem.Owners.Select(o => o.Entity));
                    }

                    owners = (from own in owners
                              group own by own.ID
                                  into grp
                              select grp.First()
                             ).ToList();

                    // ReSharper disable LoopCanBeConvertedToQuery
                    foreach (var entity in owners)
                    // ReSharper restore LoopCanBeConvertedToQuery
                    {
                        var account =
                            DB.Accounts.SingleOrDefault(
                                a => a.Access.Any(t => t.IsCreator && t.Entity.ID == entity.ID));       // TODO PAX

                        if (account == null)
                            continue;

                        var transferMatchSuccess = TransferMatcher.CategorizeTransfers(account, this);

                        if (!transferMatchSuccess)
                            return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                ExceptionHelper.Log(e);

                id = null;
                return false;
            }
        }

        [PerformanceAspect(LimitSecs = 1)]
        private void SetTransactionTag<TAccount, TTransaction>(TAccount account, DB db)
            where TTransaction : TransactionBase
            where TAccount : IHasTransactions<TTransaction>
        {
            using (db.TempDisableChangeTracking(saveChanges: false))
            {
                // ReSharper disable CompareNonConstrainedGenericWithNull
                if (account == null || account.Transactions == null) return;
                // ReSharper restore CompareNonConstrainedGenericWithNull
                var trans = account.Transactions;

                foreach (var tran in trans)
                {
                    foreach (var tag in tran.GetTagsToAdd())
                    {
                        //flag could come from different context. make sure it is attached
                        db.Flags.Attach(tag.Flag);
                        tran.ItemTags.Add(tag);

                    }
                    foreach (var tag in tran.GetTagsToRemove())
                    {
                        db.Flags.Attach(tag.Flag);
                        db.Entry(tag).State = System.Data.Entity.EntityState.Deleted;
                    }

                    var merchant = tran.MerchantAuto;
                    if (merchant != null)
                        db.Entry(merchant).State = System.Data.Entity.EntityState.Unchanged;
                }
            }
        }

        public bool UpdateInvestmentAccount(WealthItem item, IList<InvestmentHolding> holdingsToDelete, IList<InvestmentTransactionOwner> transactionOwnersToDelete = null)
        {
            if (holdingsToDelete == null)
                holdingsToDelete = new List<InvestmentHolding>();
            if (transactionOwnersToDelete == null)
                transactionOwnersToDelete = new List<InvestmentTransactionOwner>();

            try
            {
                foreach (var holding in holdingsToDelete)
                    DB.Entry(holding).State = System.Data.Entity.EntityState.Deleted;

                foreach (var owner in transactionOwnersToDelete)
                    DB.Entry(owner).State = System.Data.Entity.EntityState.Deleted;

                return UpdateWealthItem(item);
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return false;
            }
        }

        public bool UpdateWealthItem(WealthItem item)
        {
            item.LastReviewed = DateTime.Now;
            DB.Entry(item).State = System.Data.Entity.EntityState.Modified;

            DB.SaveChanges();
            return true;
        }

        public void UpdatePropertyValues(int fromWealthItemID, int toWealthItemID, int ignoreLastHours = 0)
        {
            try
            {
                DateTime dt = DateTime.Now.AddHours(-1 * ignoreLastHours); // ignore values that have been updated in the last n hours)
                int batchNum = 1;
                int batchSize = 10;

                int numProps = DB.WealthItems.Count(i => i is AssetWealthItem
                                                         && i.ID >= fromWealthItemID && i.ID <= toWealthItemID
                                                         && (i.CategoryInternal == (int)AssetWealthItemCategory.Property ||
                                                                i.CategoryInternal == (int)AssetWealthItemCategory.MyHome ||
                                                                i.CategoryInternal == (int)AssetWealthItemCategory.LifestyleProperty)
                                                         && (!i.AVMUpdateDate.HasValue || i.AVMUpdateDate.Value < dt)
                                                         && i.UseDataFeed);
                LogHelper.LogInfo("Start PropertyValueUpdater. Approx " + numProps + " properties to update (" + numProps / batchSize + " batches of size " + batchSize + ")");

                List<WealthItem> items = DB.WealthItems.OrderBy(x => x.AVMUpdateDate).Where(i => i is AssetWealthItem
                    && i.ID >= fromWealthItemID && i.ID <= toWealthItemID
                    && (i.CategoryInternal == (int)AssetWealthItemCategory.Property ||
                            i.CategoryInternal == (int)AssetWealthItemCategory.MyHome ||
                            i.CategoryInternal == (int)AssetWealthItemCategory.LifestyleProperty)
                    && (!i.AVMUpdateDate.HasValue || i.AVMUpdateDate.Value < dt)
                    && i.UseDataFeed).Take(batchSize).ToList();

                while (items.Count > 0)
                {
                    foreach (var wealthItem in items)
                    {
                        EstimateCurrentPropertyValue(wealthItem);
                        UpdateHomeModelInfo(wealthItem);
                        //wealthItem.AVMUpdateDate = DateTime.Now;
                    }
                    DB.SaveChanges();
                    items = DB.WealthItems.OrderBy(x => x.AVMUpdateDate).Where(i => i is AssetWealthItem
                        && i.ID >= fromWealthItemID && i.ID <= toWealthItemID
                        && (i.CategoryInternal == (int)AssetWealthItemCategory.Property ||
                                i.CategoryInternal == (int)AssetWealthItemCategory.MyHome ||
                                i.CategoryInternal == (int)AssetWealthItemCategory.LifestyleProperty)
                        && (!i.AVMUpdateDate.HasValue || i.AVMUpdateDate.Value < dt)
                        && i.UseDataFeed).Take(batchSize).ToList();

                    if (batchNum % 10 == 0) // Write to log every 10th batch
                        LogHelper.LogInfo("PropertyValueUpdater processing batch " + batchNum);
                    batchNum++;
                }
            }

            catch (Exception ex)
            {
                ExceptionHelper.HandleException(ex, false);
            }
            finally
            {
                LogHelper.LogInfo("End PropertyValueUpdater");
            }
        }

        private void UpdateHomeModelInfo(WealthItem wealthItem)
        {
            try
            {
                var sa = new RPDataSA();
                var response = sa.PropertyMatch(wealthItem.AddressStreet2);
                if (response.ResponseType == PropertyMatchResponseTypeEnum.ExactMatchFound)
                {
                    response.PropertyGrowthInfo.Clear();
                    response.AreaSaleSummary.Clear();
                    response.PropertyGrowthInfo.Clear();
                    response.YearlyMedianPrices.Clear();
                    response.MonthlyMedianPrices.Clear();
                    wealthItem.HomeDetails = MyProsperity.Framework.Xml.SerializationHelper.Serialize(response);
                }
            }
            catch (Exception ex)
            {
                ExceptionHelper.HandleException(ex, false);
            }
            return;
        }

        public bool EstimateCurrentPropertyValue(WealthItem propertyItem)
        {
            if (propertyItem == null)
                throw new ArgumentNullException();

            bool result = true;

            try
            {
                decimal newRPDataValue = PropertyHelper.GetCalculatedPropertyValueFromRPData(propertyItem.AddressStreet2);

                // If there is an RPData value 
                if (newRPDataValue > 0)
                {
                    EstimateCurrentValue(propertyItem, newRPDataValue);
                }
                else
                {
                    result = false; // didn't get an RPData value so couldn't update
                    propertyItem.RefreshInfo =
                        string.Format(
                            "Datetime: {0}. Failed to update because newRPDataValue is less than or equal to 0.", DateTime.Now);
                }

            }
            catch (Exception ex)
            {
                result = false;
                propertyItem.RefreshInfo =
                        string.Format(
                            "Datetime: {0}. Failed to update because of exception. Please refer to the logs.", DateTime.Now);
                ExceptionHelper.HandleException(ex, false);
            }
            finally
            {
                propertyItem.AVMUpdateDate = DateTime.Now;
            }

            return result;
        }

        public void UpdateVehicleValues(int fromWealthItemID, int toWealthItemID, int ignoreLastHours = 0)
        {
            try
            {
                DateTime dt = DateTime.Now.AddHours(-1 * ignoreLastHours); // ignore values that have been updated in the last n hours)
                int batchNum = 1;
                int batchSize = 10;

                int numCars = DB.WealthItems.Where(i => i is AssetWealthItem
                    && i.ID >= fromWealthItemID && i.ID <= toWealthItemID
                    && i.CategoryInternal == (int)AssetWealthItemCategory.MotorVehicle
                    && (!i.AVMUpdateDate.HasValue || i.AVMUpdateDate.Value < dt)
                    && i.UseDataFeed).Count();
                LogHelper.LogInfo("Start VehicleValueUpdater. Approx " + numCars + " vehicles to update (" + numCars / batchSize + " batches of size " + batchSize + ")");

                List<WealthItem> items = DB.WealthItems.OrderBy(x => x.AVMUpdateDate).Where(i => i is AssetWealthItem
                    && i.ID >= fromWealthItemID && i.ID <= toWealthItemID
                    && i.CategoryInternal == (int)AssetWealthItemCategory.MotorVehicle
                    && (!i.AVMUpdateDate.HasValue || i.AVMUpdateDate.Value < dt)
                    && i.UseDataFeed).Take(batchSize).ToList();

                while (items.Count > 0)
                {
                    foreach (var wealthItem in items)
                    {
                        EstimateCurrentVehicleValue(wealthItem);
                    }
                    DB.SaveChanges();
                    items = DB.WealthItems.OrderBy(x => x.AVMUpdateDate).Where(i => i is AssetWealthItem
                        && i.ID >= fromWealthItemID && i.ID <= toWealthItemID
                        && i.CategoryInternal == (int)AssetWealthItemCategory.MotorVehicle
                        && (!i.AVMUpdateDate.HasValue || i.AVMUpdateDate.Value < dt)
                        && i.UseDataFeed).Take(batchSize).ToList();

                    if (batchNum % 10 == 0) // Write to log every 10th batch
                        LogHelper.LogInfo("VehicleValueUpdater processing batch " + batchNum);
                    batchNum++;
                }

            }
            catch (Exception ex)
            {
                ExceptionHelper.HandleException(ex, false);
            }
            finally
            {
                LogHelper.LogInfo("End VehicleValueUpdater");
            }
        }

        public bool EstimateCurrentVehicleValue(WealthItem wealthItem)
        {
            bool result = true;
            try
            {
                RedBookSA sa = new RedBookSA();

                var latestRedbookVehicle = sa.GetVehicleDetail(RequestContext.Current, wealthItem.MotorVehicleId);

                // If there is data from Redbook
                if (latestRedbookVehicle != null && latestRedbookVehicle.ComputedValue > 0)
                {
                    decimal newRBValue = latestRedbookVehicle.ComputedValue;

                    EstimateCurrentValue(wealthItem, newRBValue);
                }
                else // else Didn't get any data from Redbook so no prices are updated. Still need to set AVMUpdateDate in finally block because we tried!
                {
                    result = false;
                }

            }
            catch (Exception ex)
            {
                result = false;
                ExceptionHelper.HandleException(ex, false, wealthItem.ID);
            }
            finally
            {
                wealthItem.AVMUpdateDate = DateTime.Now;
            }

            return result;
        }

        private void EstimateCurrentValue(WealthItem item, decimal newFeedValue)
        {
            decimal oldValuationPrice = item.ValuationPrice ?? 0;
            decimal oldFeedValue = 0;

            if (item.CategoryInternal == (int)AssetWealthItemCategory.MotorVehicle)
                oldFeedValue = item.RedBookValue ?? 0;
            else if (item.IsHomeOrProperty)
                oldFeedValue = item.RPDataValue ?? 0;

            // IF Scenario 1 - this is the first time we have Redbook data, use RB value even if someone has set a custom valuation
            // OR Scenario 2 (primary case) - Using Redbook valuations, need to update
            if (oldFeedValue == 0 ||
                PropertyHelper.ValuesAreSameOrAlmostTheSame(oldFeedValue, oldValuationPrice, 10))
            {
                var strValueChange = string.Format("from {0} to {1}", item.Value, newFeedValue);
                item.ValuationPrice = newFeedValue;
                item.ValuationDate = DateTime.Now;
                item.Value = newFeedValue;
                item.RefreshInfo =
                        string.Format(
                            "Datetime: {0}. Item's value has been UPDATED {1} using RedBookValue/RPDataValue.", DateTime.Now, strValueChange);
            }
            else // Scenario 3 - User has provided a custom valuation.
            {
                // The current value is updated based on the % change in RPData/RedBook prices
                if (ConfigHelper.TryGetOrDefault("ScaleAppraisalWithFeedChanges", true))
                {
                    var newValue = Math.Round(item.Value * (newFeedValue / oldFeedValue));
                    var strValueChange = string.Format("from {0} to {1}", item.Value, newValue);
                    item.Value = newValue;
                    item.RefreshInfo =
                        string.Format(
                            "Datetime: {0}. Item's value has been SCALED {1} using RedBookValue/RPDataValue.", DateTime.Now, strValueChange);
                }
            }

            if (item.CategoryInternal == (int)AssetWealthItemCategory.MotorVehicle)
                item.RedBookValue = newFeedValue;
            else if (item.IsHomeOrProperty)
                item.RPDataValue = newFeedValue;

            item.AVMLastSucceedDate = DateTime.Now;
            //            item.AVMUpdateDate = DateTime.Now; //should be set in a finally block in the calling function, but just in case...
        }

        public WealthItemHistoryItem GetHistoryItem(int id)
        {
            using (Profiler.Step("WealthService.GetHistoryItem"))
            {
                var db = DB;

                var re = db.CostHistoryItems
                           .Include(r => r.Owners)
                           .FirstOrDefault(r => r.ID == id);
                return re;
            }
        }

        public WealthItemHistoryItem GetHistoryItem(Account account, int id)
        {
            using (Profiler.Step("WealthService.GetHistoryItem"))
            {
                var entityIDs = from aa in account.Access
                                select aa.Entity.ID;

                var db = DB;
                var re = db.CostHistoryItems
                    .Include(r => r.Owners)
                    .FirstOrDefault(r => r.ID == id && r.Owners.AnyAndNotNull() && r.Owners.Any(ow => entityIDs.Contains(ow.Entity.ID)));
                return re;
            }
        }

        public WealthItemHistoryItem GetHistoryItem(int id, out int parentId)
        {
            using (Profiler.Step("WealthService.GetHistoryItem"))
            {
                var db = DB;

                var qry = from i in db.WealthItems
                          from r in i.CostHistory
                          where r.ID == id
                          select new { r, parentId = i.ID };

                foreach (var result in qry.Take(1))
                {
                    parentId = result.parentId;
                    return result.r;
                }

                parentId = 0;
                return null;
            }
        }

        public bool UpdateHistoryItem(WealthItemHistoryItem receipt)
        {
            using (Profiler.Step("WealthService.UpdateHistoryItem"))
            {
                var db = DB;

                try
                {
                    db.Entry(receipt).State = System.Data.Entity.EntityState.Modified;

                    db.SaveChanges();
                    return true;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    return false;
                }
            }
        }

        public bool AddHistoryItem(int parentId, WealthItemHistoryItem item,
                                   Entity entity, out int? id)
        {
            using (Profiler.Step("WealthService.AddHistoryItem"))
            {
                var db = DB;

                try
                {
                    db.Entry(entity).State = System.Data.Entity.EntityState.Unchanged;

                    item.Owners = WealthItemHistoryItemOwner.Sole(entity);

                    var wealthItem = db.WealthItems.First(x => x.ID == parentId);

                    wealthItem.CostHistory.Add(item);

                    db.SaveChanges();

                    id = item.ID;

                    return true;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    id = null;
                    return false;
                }

            }
        }

        public bool DeleteHistoryItem(WealthItemHistoryItem item)
        {
            using (Profiler.Step("WealthService.DeleteHistoryItem"))
            {
                var db = DB;

                try
                {
                    // NOTE: documents have to be manually deleted because they don't have ON CASCADE DELETE
                    item.Documents
                        .ToList()
                        .ForEach(d => DB.Entry(d).State = System.Data.Entity.EntityState.Deleted);

                    DB.CostHistoryItems.Remove(item);

                    db.SaveChanges();

                    return true;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);

                    return false;
                }
            }
        }

        private IQueryable<WealthItem> GetItems(IEnumerable<Account> accounts)
        {
            var db = DB;

            var accountIds = accounts.Select(a => a.ID);

            var entityIDs = db.AccountAccesses.Where(aa => accountIds.Contains(aa.Account.ID)).Select(aa => aa.EntityID);

            return db.WealthItemOwners.Where(o => entityIDs.Contains(o.Entity_ID)).Select(o => o.Item);
        }

        private IQueryable<WealthItem> GetItems(Account account, bool includeDummyItems = false)
        {
            var db = DB;

            var entityIDs = from aa in account.Access
                            select aa.Entity.ID;

            var r = (from i in db.WealthItems
                     where i.Owners.Any(o => entityIDs.Contains(o.Entity.ID))
                     select i
                    );

            var showHidden = account.GetAccountSettingCached().ShowHiddenWealthItem;

            if (!showHidden)
                r = r.Where(x => !x.IsHidden);

            if (!includeDummyItems)
                r = r.Where(x => !x.IsDummy);

            return r;
        }


        public int CountItems(Account account, bool excludeClassSuperAccounts = false, bool includeDummyItems = false)
        {
            using (Profiler.Step("WealthService.CountItems"))
            {
                try
                {
                    var items = GetItems(account, includeDummyItems);
                    return excludeClassSuperAccounts
                        ? items.Count(t => t.InvestmentAccount != null &&
                                           t.InvestmentAccount.DataSource ==
                                           InvestmentAccountDataSource.ClassSuper.ToString())
                        : items.Count();
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    return 0;
                }
            }
        }

        public int CountItems<T>(Account account, bool includeDummyItems = false)
            where T : WealthItem
        {
            using (Profiler.Step("WealthService.CountItems<T>"))
            {

                try
                {
                    return GetItems(account, includeDummyItems).OfType<T>().Count();

                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                    return 0;
                }
            }
        }

        private void RemoveMatchingTransactions<TAccount, TTransaction>(TAccount account)
            where TTransaction : TransactionBase
            where TAccount : IHasTransactions<TTransaction>
        {
            foreach (var transaction in account.Transactions)
            {
                if (transaction.HasMatchingTransaction)
                {
                    transaction.ClearMatchingTransactions();
                }
            }
        }

        [PerformanceAspect(LimitSecs = 5)]
        [LogExceptionAspect(FlowBehavior.Continue, false)]
        public bool DeleteWealthItem(WealthItem item)
        {
            var db = DB;

            using (db.TempDisableChangeTracking())
            {
                if (item.BankAccount != null)
                    RemoveMatchingTransactions<BankAccount, BankTransaction>(item.BankAccount);
                if (item.CardAccount != null)
                    RemoveMatchingTransactions<CardAccount, CardTransaction>(item.CardAccount);
                if (item.LoanAccount != null)
                    RemoveMatchingTransactions<LoanAccount, LoanTransaction>(item.LoanAccount);
                if (item.InvestmentAccount != null)
                    RemoveMatchingTransactions<InvestmentAccount, InvestmentTransaction>(item.InvestmentAccount);

                // NOTE: documents have to be manually deleted because they don't have ON CASCADE DELETE
                Data.Util.DocumentFinder.FindRecursive(item)
                    .ToList()
                    .ForEach(d => db.Entry(d).State = System.Data.Entity.EntityState.Deleted);

                if (item.BankAccount != null)
                {
                    foreach (var transaction in item.BankAccount.Transactions.ToList())
                    {
                        //Business.DocumentService.DeleteDocumentEntities(transaction);

                        Data.Util.DocumentFinder.FindRecursive(transaction)
                            .Where(d => d.PropertyManagementReport == null)
                            .ToList()
                            .ForEach(d => db.Entry(d).State = System.Data.Entity.EntityState.Deleted);

                        //db.CashflowItems.Remove(transaction.CashflowItem);
                        db.BankTransactions.Remove(transaction);
                    }

                    db.Entry(item.BankAccount).State = System.Data.Entity.EntityState.Deleted;
                }
                if (item.CardAccount != null)
                {
                    foreach (var transaction in item.CardAccount.Transactions.ToList())
                    {
                        Data.Util.DocumentFinder.FindRecursive(transaction)
                            .Where(d => d.PropertyManagementReport == null)
                            .ToList()
                            .ForEach(d => db.Entry(d).State = System.Data.Entity.EntityState.Deleted);
                        //db.CashflowItems.Remove(transaction.CashflowItem);
                        db.CardTransactions.Remove(transaction);
                    }

                    db.Entry(item.CardAccount).State = System.Data.Entity.EntityState.Deleted;
                }

                if (item.InvestmentAccount != null)
                {
                    foreach (var transaction in item.InvestmentAccount.Transactions.ToList())
                    {
                        Data.Util.DocumentFinder.FindRecursive(transaction)
                            .Where(d => d.PropertyManagementReport == null)
                            .ToList()
                            .ForEach(d => db.Entry(d).State = System.Data.Entity.EntityState.Deleted);
                        //db.CashflowItems.Remove(transaction.CashflowItem);
                        db.InvestmentTransactions.Remove(transaction);
                    }

                    db.Entry(item.InvestmentAccount).State = System.Data.Entity.EntityState.Deleted;
                }
                if (item.LoanAccount != null)
                {
                    foreach (var transaction in item.LoanAccount.Transactions.ToList())
                    {
                        Data.Util.DocumentFinder.FindRecursive(transaction)
                            .Where(d => d.PropertyManagementReport == null)
                            .ToList()
                            .ForEach(d => db.Entry(d).State = System.Data.Entity.EntityState.Deleted);
                        // db.CashflowItems.Remove(transaction.CashflowItem);
                        db.LoanTransactions.Remove(transaction);
                    }

                    db.Entry(item.LoanAccount).State = System.Data.Entity.EntityState.Deleted;
                }

                var propertyManagementService = ObjectFactory.GetInstance<PropertyManagementService>();
                var reports = propertyManagementService.GetManagementReports(item.ID);
                if (reports != null)
                {
                    reports.ToList()
                           .ForEach(x => propertyManagementService.DeleteReport(x.ID, true));
                }

                db.WealthItems.Remove(item);
            }

            return true;
        }

        public WealthItem GetLastItemAdded(bool includeDummyItems = false)
        {
            using (Profiler.Step("WealthService.GetItemAdded"))
            {
                var db = DB;

                var item = db.WealthItems
                    //.Include(x => x.Owners.Select(xx => xx.Entity))
                    //.Include(x => x.CostHistory.Select(xx => xx.Owners.Select(xxx => xxx.Entity)))
                    //.Include(x => x.CostHistory.Select(xx => xx.Documents))
                    //.Include(x => x.ToDos)
                    .Where(x => includeDummyItems || !x.IsDummy)
                             .OrderByDescending(x => x.ID)
                             .Take(1)
                             .FirstOrDefault();

                return item;
            }
        }

        public IEnumerable<WealthItem> GetSuperannuationItems(Entity entity, bool includeDummyItems = false)
        {
            using (Profiler.Step("WealthService.GetSuperannuationItems"))
            {
                DB db = DB;

                var qry = (from i in db.WealthItems.OfType<AssetWealthItem>()
                           let isOk = i.Owners.Any(o => o.Entity.ID == entity.ID)
                           where isOk && i.CategoryInternal == (int)AssetWealthItemCategory.Super
                           select i
                          );

                var items = qry.ToList();

                if (!includeDummyItems)
                    items = items.Where(x => !x.IsDummy).ToList();

                return qry;

            }
        }

        //rino 24-Apr-2012: Eager loading of CostHistory items
        //WARNING!!! This function seems SLOWER than lazy loading history items.       
        public IEnumerable<WealthItemHistoryItem> GetHistoryItems(int parentId)
        {
            using (Profiler.Step("WealthService.GetHistoryItems"))
            {
                var db = DB;

                // ReSharper disable ReplaceWithSingleCallToFirstOrDefault
                var item = db.WealthItems
                             .Include(x => x.CostHistory.Select(xx => xx.Owners.Select(xxx => xxx.Entity)))
                             .Include(x => x.CostHistory.Select(xx => xx.Documents))
                             .Where(x => x.ID == parentId)
                             .FirstOrDefault();
                // ReSharper restore ReplaceWithSingleCallToFirstOrDefault

                if (item != null) return item.CostHistory;

                return new List<WealthItemHistoryItem>();
            }
        }

        //This will include hidden items.
        public IEnumerable<WealthItem> GetWealthItemsWithAccounts(Entity entity, bool includeDummyItems = false)
        {
            using (Profiler.Step("WealthService.GetWealthItemsWithAccounts(Entity)"))
            {
                DB db = DB;


                //RINO 17Oct2012: Eager load the accounts
                var qry = (from i in db.WealthItems
                           let isOk = i.Owners.Any(o => o.Entity.ID == entity.ID)
                           where isOk
                           select i
                          )
                    .Include(x => x.Owners.Select(xx => xx.Entity))
                    .Include(a => a.BankAccount)
                    .Include(a => a.CardAccount)
                    .Include(a => a.InvestmentAccount)
                    .Include(a => a.LoanAccount);

                var items = qry.ToList();

                if (!includeDummyItems)
                    items = items.Where(x => !x.IsDummy).ToList();

                return items;
            }
        }

        public IEnumerable<InvestmentHolding> GetHoldingsByWealthItem(WealthItem wealthItem, bool includeCashAsHolding = true)
        {
            if (wealthItem == null)
                return new List<InvestmentHolding>();

            var investmentAccount = wealthItem.InvestmentAccount ?? new InvestmentAccount();

            return investmentAccount.GetInvestmentHoldings(includeCashAsHolding);
        }

        public IEnumerable<InvestmentFund> GetInvestmentFunds(TaxPartnerBranch branch)
        {
            if (branch == null)
                throw new ArgumentNullException("branch");

            return DB.InvestmentFunds.Where(i => i.InvestmentBrand != null
                                                 && i.InvestmentBrand.TaxPartnerBranch != null
                                                 && i.InvestmentBrand.TaxPartnerBranch.ID == branch.ID);
        }

        public IEnumerable<InvestmentFund> GetInvestmentFunds(TaxPartnerBranch branch, InvestmentBrand brand)
        {
            if (brand == null)
                throw new ArgumentNullException("brand");

            return GetInvestmentFunds(branch).Where(i => i.InvestmentBrand.ID == brand.ID);
        }

        public IEnumerable<InvestmentFund> GetInvestmentFunds(TaxPartnerBranch branch, List<InvestmentBrand> brands)
        {
            if (brands == null)
                throw new ArgumentNullException("brands");

            return GetInvestmentFunds(branch).Where(i => brands.Select(b => b.ID).Contains(i.InvestmentBrand.ID));
        }

        public BankAccount GetBankAccount(int id)
        {
            return DB.BankAccounts.FirstOrDefault(i => i.ID == id);
        }

        public CardAccount GetCardAccount(int id)
        {
            return DB.CardAccounts.FirstOrDefault(i => i.ID == id);
        }

        public LoanAccount GetLoanAccount(int id)
        {
            return DB.LoanAccounts.FirstOrDefault(i => i.ID == id);
        }

        public InvestmentAccount GetInvestmentAccount(int id)
        {
            return DB.InvestmentAccounts.FirstOrDefault(i => i.ID == id);
        }

        public WealthItem GetWealthItemByBankAccount(int id, bool includeDummyItems = false)
        {
            return DB.WealthItems.SingleOrDefault(w => w.BankAccount != null && w.BankAccount.ID == id && (includeDummyItems || !w.IsDummy));
        }

        public WealthItem GetWealthItemByCardAccount(int id, bool includeDummyItems = false)
        {
            return DB.WealthItems.SingleOrDefault(w => w.CardAccount != null && w.CardAccount.ID == id && (includeDummyItems || !w.IsDummy));
        }

        public WealthItem GetWealthItemByLoanAccount(int id, bool includeDummyItems = false)
        {
            return DB.WealthItems.SingleOrDefault(w => w.LoanAccount != null && w.LoanAccount.ID == id && (includeDummyItems || !w.IsDummy));
        }

        public WealthItem GetWealthItemByInvestmentAccount(int id, bool includeDummyItems = false)
        {
            return DB.WealthItems.SingleOrDefault(w => w.InvestmentAccount != null && w.InvestmentAccount.ID == id && (includeDummyItems || !w.IsDummy));
        }

        public WealthItem GetWealthItemByAccount<TAccount>(TAccount account, bool includeDummyItems = false) where TAccount : AccountBase
        {
            if (account is BankAccount)
                return GetWealthItemByBankAccount(account.ID, includeDummyItems);
            if (account is CardAccount)
                return GetWealthItemByCardAccount(account.ID, includeDummyItems);
            if (account is LoanAccount)
                return GetWealthItemByLoanAccount(account.ID, includeDummyItems);
            if (account is InvestmentAccount)
                return GetWealthItemByInvestmentAccount(account.ID, includeDummyItems);

            throw new Exception("Invalid account type" + typeof(TAccount).FullName);
        }

        /// <summary>
        /// Get all of the wealth items for a given set of fin accounts of any type. This method aims for high performance
        /// </summary>
        /// <param name="accountList"></param>
        /// <param name="includeDummyItems"></param>
        /// <returns></returns>
        public IEnumerable<WealthItem> GetWealthItemsByAccounts<TAccount>(
            List<TAccount> accountList, bool includeDummyItems = false) where TAccount : AccountBase
        {
            var bankAccountIDList = new List<int>();
            var cardAccountIDList = new List<int>();
            var loanAccountIDList = new List<int>();
            var investmentAccountIDList = new List<int>();

            // minimising reflection lookups (which are slow) as some users have many fin accounts
            // Don't even do reflection call for other fin account types unless necessary #SeriousOptimisation
            var bankAccountTypeStr = typeof(BankAccount).ToString();
            var cardAccountTypeStr = string.Empty;
            var loanAccountTypeStr = string.Empty;
            var investmentAccountTypeStr = string.Empty;

            foreach (var account in accountList)
            {
                var typeStr = ObjectContext.GetObjectType(account.GetType()).ToString(); // reflection in a loop, not ideal

                if (typeStr == bankAccountTypeStr)
                {
                    bankAccountIDList.Add(account.ID);
                    continue;
                }
                    
                if (cardAccountTypeStr == string.Empty || typeStr == cardAccountTypeStr)
                {
                    if (cardAccountTypeStr == string.Empty)
                    {
                        cardAccountTypeStr = typeof(CardAccount).ToString();
                        if (typeStr == cardAccountTypeStr)
                        {
                            cardAccountIDList.Add(account.ID);
                            continue;
                        }
                            
                    }
                    else
                    {
                        cardAccountIDList.Add(account.ID);
                        continue;
                    }                 
                }

                if (loanAccountTypeStr == string.Empty || typeStr == loanAccountTypeStr)
                {
                    if (loanAccountTypeStr == string.Empty)
                    {
                        loanAccountTypeStr = typeof(LoanAccount).ToString();
                        if (typeStr == loanAccountTypeStr)
                        {
                            loanAccountIDList.Add(account.ID);
                            continue;
                        }                          
                    }
                    else
                    {
                        loanAccountIDList.Add(account.ID);
                        continue;
                    }
                }

                if (investmentAccountTypeStr == string.Empty || typeStr == investmentAccountTypeStr)
                {
                    if (investmentAccountTypeStr == string.Empty)
                    {
                        investmentAccountTypeStr = typeof(InvestmentAccount).ToString();
                        if (typeStr == investmentAccountTypeStr)
                        {
                            investmentAccountIDList.Add(account.ID);
                            continue;
                        }               
                    }
                    else
                    {
                        investmentAccountIDList.Add(account.ID);
                        continue;
                    }
                }
            }

            var results = DB.WealthItems.Where(w => ((w.BankAccount != null && bankAccountIDList.Contains(w.BankAccount.ID)) ||
                                                     (w.CardAccount != null && cardAccountIDList.Contains(w.CardAccount.ID)) ||
                                                     (w.LoanAccount != null && loanAccountIDList.Contains(w.LoanAccount.ID)) ||
                                                     (w.InvestmentAccount != null && investmentAccountIDList.Contains(w.InvestmentAccount.ID))
                                                    ) && (includeDummyItems || !w.IsDummy)).ToList();

            if (ConfigHelper.TryGetOrDefault("DebugUpdatingYodleeAccounts", true))
            {
                var bids = string.Join(",", bankAccountIDList);
                var cids = string.Join(",", cardAccountIDList);
                var lids = string.Join(",", loanAccountIDList);
                var iids = string.Join(",", investmentAccountIDList);
                var wids = string.Join(",", results.Select(x => x.ID).ToList());

                LogHelper.LogInfo(string.Format("GetWealthItemsByAccounts: bankAccountIds: {0} cardAccountIDs {1} loanAccountIDs {2} investmentAccountIDs {3} wealthItemIDs {4}",
                    bids, cids, lids, iids, wids));
            }

            return results;
        }


        public string GetWealthItemNameByAccount(int accountID, TransactionAccountType transactionAccountType, bool includeDummyItems = false)
        {
            WealthItem wealthItem = null;

            if (transactionAccountType == TransactionAccountType.BANK)
            {
                wealthItem = GetWealthItemByBankAccount(accountID, includeDummyItems);
            }
            else if (transactionAccountType == TransactionAccountType.CARD)
            {
                wealthItem = GetWealthItemByCardAccount(accountID, includeDummyItems);
            }
            else if (transactionAccountType == TransactionAccountType.LOAN)
            {
                wealthItem = GetWealthItemByLoanAccount(accountID, includeDummyItems);
            }
            else if (transactionAccountType == TransactionAccountType.INVESTMENT)
            {
                wealthItem = GetWealthItemByInvestmentAccount(accountID, includeDummyItems);
            }
            else
            {
                throw new Exception("Invalid account type" + transactionAccountType.GetDescription());
            }

            return wealthItem != null ? wealthItem.Name : null;
        }


        public int? AddCashFlowHistory(CashFlowHistory history)
        {
            int? result = null;
            try
            {
                DB.CashFlowHistory.Add(history);
                DB.SaveChanges();
                result = history.ID;
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
            }

            return result;
        }

        public IEnumerable<CashFlowHistory> GetCashFlowHistory(Account account, DateTime dateTime)
        {
            return DB.CashFlowHistory.Where(s => s.Account.ID == account.ID && s.DateFrom >= dateTime);
        }

        public void AddPropertyValuationPdf(string path, Account account, int itemId, UploadEnvConfiguration uploadEnvConfiguration)
        {
            var fi = new FileInfo(path);

            using (var fileStream = File.Open(path, FileMode.Open))
            {
                var uploadedDocument = new UploadedDocument(itemId, "PropertyValuation.pdf", fi.Length, fileStream,
                                                            "Valuation Report As On " +
                                                            DateTime.Now.ToString("d/MM/yyyy"), account.ID);
                string errMsg;

                int? docid;
                var success = DocumentService.AddDocument<WealthItem>(uploadedDocument, uploadEnvConfiguration,
                                                                      out docid, out errMsg);
                if (success)
                {
                    Document document = DocumentService.GetDocumentByID(docid.GetValueOrDefault());
                    if (document != null)
                    {
                        document.File.MimeType = "application/pdf";
                        DB.SaveChanges();
                    }
                }
                if (!success) throw new Exception(errMsg);
            }

            //var fileInfo = new FileInfo(path);

            //var fileRef = new FileReference
            //                  {
            //                      FileName = "PropertyValuation.pdf",
            //                      FileRef = fileInfo.Name,
            //                      Bytes = (int)fileInfo.Length,
            //                      MimeType = Utility.MimeType(fileInfo.Extension)
            //                  };

            //var doc = new Document
            //              {
            //                  Account_ID = account.ID,
            //                  Description = "Valuation Report As On " + DateTime.Now.ToString("d/MM/yyyy"),
            //                  File = fileRef,
            //                  Created = DateTime.Now
            //              };

            //try
            //{
            //    Context<DB>.BeginThreadContext(DBBase.New());
            //}
            //catch (Exception ex)
            //{
            //    ExceptionHelper.HandleException(ex);
            //}

            //DocumentService.AddDocument<WealthItem>(itemId, doc, out docid);

            //try
            //{
            //    Context<DB>.EndThreadContext();
            //}
            //catch (Exception ex)
            //{
            //    ExceptionHelper.HandleException(ex);
            //}

            //new AmazonS3SA().Upload(RequestContext.Current, path);
        }

        public byte[] GetDefaultImage(WealthItem item)
        {
            if (item.HasYodleeData)
            {
                var finAccount = item.BaseAccount;
                if (finAccount == null)
                    return null;

                var bank = DB.Banks.SingleOrDefault(b => b.ContentServiceID == finAccount.ContentServiceId);

                if (bank == null)
                    return null;

                return bank.Image;
            }
            return null;
        }

        public bool UpdateCategories(Entity entity, List<Tuple<long, int>> wealthItemCategoryAssociations)
        {

            try
            {
                if (wealthItemCategoryAssociations.IsNullOrEmpty())
                    return true;

                var itemIDs = wealthItemCategoryAssociations.Select(t => t.Item1).ToList();
                var wealthItemsToUpdate = GetWealthItems(entity).ToList().
                                                                 Where(
                                                                     t =>
                                                                     t.BaseAccount != null &&
                                                                     itemIDs.Contains(t.BaseAccount.YodleeItemId))
                                                                .ToList();

                foreach (var wealthItem in wealthItemsToUpdate)
                {
                    var yodleeItemID = wealthItem.BaseAccount.YodleeItemId;
                    var newCategory = wealthItemCategoryAssociations.First(t => t.Item1 == yodleeItemID).Item2;

                    wealthItem.CategoryInternal = newCategory;
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

        public List<WealthItem> GetPropertyWealthItems(Account account, bool includeDummyItems = false)
        {
            var items = new List<WealthItem>();
            try
            {
                if (account == null)
                    throw new ArgumentNullException("account");

                items = GetItems(account, includeDummyItems).Where(x =>
                                x.HomeTypeInternal == (int)HomeType.Investment ||
                                x.HomeTypeInternal == (int)HomeType.LifeStyle)
                        .ToList();
            }
            catch (Exception ex)
            {
                ExceptionHelper.HandleException(RequestContext.Current, ex, false);
            }
            return items;
        }


        public List<WealthItem> GetWealthItemsByType(AssetWealthItemCategory? asset, LiabilityWealthItemCategory? liability, bool includeDummyItems = false)
        {
            var items = new List<WealthItem>();
            try
            {
                if (asset != null)
                {
                    items.AddRange(
                        DB.WealthItems.OfType<AssetWealthItem>().Where(x => x.CategoryInternal == (int)asset && (includeDummyItems || !x.IsDummy)).ToList());
                }
                if (liability != null)
                {
                    items.AddRange(
                        DB.WealthItems.OfType<LiabilityWealthItem>()
                            .Where(x => x.CategoryInternal == (int)liability && (includeDummyItems || !x.IsDummy))
                            .ToList());
                }
            }
            catch (Exception ex)
            {
                ExceptionHelper.HandleException(RequestContext.Current, ex, false);
            }
            return items;
        }

        public List<WealthItem> GetWealthItemsByAssetWealthItemCategory(Account account, AssetWealthItemCategory assetCategory, bool includeDummyItems = false)
        {
            var items = new List<WealthItem>();
            try
            {
                if (account == null)
                    throw new ArgumentNullException("account");

                items = GetItems(account, includeDummyItems).Where(x => x.CategoryInternal == (int)assetCategory).ToList();
            }
            catch (Exception ex)
            {
                ExceptionHelper.HandleException(RequestContext.Current, ex, false);
            }
            return items;
        }

        public List<WealthItem> GetWealthItemsByCategory(Account account, IList<AssetWealthItemCategory> assetCategories, IList<LiabilityWealthItemCategory> liabilityCategories, bool includeDummyItems = false)
        {
            var allItems = new List<WealthItem>();
            var items = new List<WealthItem>();
            try
            {
                if (account == null)
                    throw new ArgumentNullException("account");

                allItems = GetItems(account, includeDummyItems).ToList();

                if (assetCategories.AnyAndNotNull())
                {
                    foreach (AssetWealthItemCategory assetCat in assetCategories)
                    {
                        items.AddRange(allItems.OfType<AssetWealthItem>().Where(x => x.CategoryInternal == (int)assetCat).ToList());
                    }

                }
                if (liabilityCategories.AnyAndNotNull())
                {
                    foreach (LiabilityWealthItemCategory liabilityCat in liabilityCategories)
                    {
                        items.AddRange(allItems.OfType<LiabilityWealthItem>().Where(x => x.CategoryInternal == (int)liabilityCat).ToList());
                    }

                }
            }
            catch (Exception ex)
            {
                ExceptionHelper.HandleException(RequestContext.Current, ex, false);
            }
            return items;
        }

        public IList<AssetWealthItem> GetPropertyWealthItemsByHomeType(Account account, HomeType homeType, bool includeDummyItems = false)
        {
            return GetPropertyWealthItemsByHomeType(account, new List<HomeType> { homeType }, includeDummyItems);
        }

        public IList<AssetWealthItem> GetPropertyWealthItemsByHomeType(Account account, IList<HomeType> homeTypes, bool includeDummyItems = false)
        {
            var items = new List<AssetWealthItem>();
            try
            {
                if (account == null)
                    throw new ArgumentNullException("account");

                var allItems = GetItems(account, includeDummyItems).OfType<AssetWealthItem>().Where(x => x.AddressStreet1 != null).ToList();

                if (allItems.AnyAndNotNull())
                {
                    if (homeTypes.AnyAndNotNull())
                        foreach (var ht in homeTypes)
                        {
                            items.AddRange(allItems.Where(x => x.HomeType == (int)ht).ToList());
                        }
                    else
                    {
                        items = allItems;
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionHelper.HandleException(RequestContext.Current, ex, false);
            }
            return items;
        }

        public List<AssetWealthItem> GetPropertyWealthItemsForAccounts(IList<Account> accounts)
        {
            if (accounts.NullOrNone()) return null;

            return GetItems(accounts).OfType<AssetWealthItem>().Where(x => x.AddressStreet1 != null).ToList();
        }

        public PlanTodoProgress GenerateGoalProgress(CalculatorType calculatorType, string calculatorXml, bool isGoalProgress = false)
        {
            PlanTodoProgress progress = new PlanTodoProgress();
            var todoProgressDto = new TodoProgressDto();
            string errorMessage = null;

            switch (calculatorType)
            {
                case CalculatorType.Savings:
                    var savingsModel = MyProsperity.Framework.Xml.SerializationHelper.Deserialize<SavingPlanInputModel>(calculatorXml);
                    if (savingsModel != null)
                    {
                        var savingsPlanOutputModel = SavingPlansManager.Calculate(savingsModel, out errorMessage);
                        if (savingsModel.BankAccount != null)
                        {
                            var savingsAcct = GetWealthItem(savingsModel.BankAccount.ID);
                            if (savingsAcct != null)
                            {
                                progress.GoalProgress = new GoalProgress
                                {
                                    Date = DateTime.Now,
                                    GoalAchievedValue = savingsAcct.Value
                                };
                                if (isGoalProgress)
                                {
                                    savingsModel.BankAccount.Balance = savingsAcct.Value;
                                    todoProgressDto.IsOnTrack = savingsPlanOutputModel.IsOnTrack(savingsAcct);
                                }
                            }
                            else
                            {
                                savingsModel.BankAccount = null;
                            }
                        }
                        if (isGoalProgress)
                        {
                            todoProgressDto.BankAccountModel = savingsPlanOutputModel.BankAccount;
                            todoProgressDto.GoalTarget = (decimal)savingsModel.TargetAmount;
                        }
                    }
                    break;

                case CalculatorType.Superannuation:
                    var superModel = MyProsperity.Framework.Xml.SerializationHelper.Deserialize<AnnuityInputModel>(calculatorXml);
                    if (superModel.BankAccount != null)
                    {
                        var wealthItem = GetWealthItem(superModel.BankAccount.ID);
                        if (wealthItem != null)
                        {
                            superModel.BankAccount.Balance = wealthItem.ComputedValue;
                        }
                        else
                        {
                            superModel.BankAccount = null;
                        }
                    }
                    if (isGoalProgress)
                    {
                        var annuityOutputModel = AnnuityManager.Calculate(superModel, out errorMessage);
                        todoProgressDto.BankAccountModel = annuityOutputModel.BankAccount;
                        todoProgressDto.GoalTarget = annuityOutputModel.Principal;
                    }
                    break;

                case CalculatorType.DebtReduction:
                    var homeLoanModel = MyProsperity.Framework.Xml.SerializationHelper.Deserialize<DebtReductionInputModel>(calculatorXml);
                    if (homeLoanModel.BankAccount != null)
                    {
                        var mortgageAcct = GetWealthItem(homeLoanModel.BankAccount.ID);
                        if (mortgageAcct != null)
                        {
                            progress.GoalProgress = new GoalProgress
                            {
                                Date = DateTime.Now,
                                GoalAchievedValue = mortgageAcct.Value
                            };
                            if (isGoalProgress)
                            {
                                var homeLoanOutputModel = DebtReductionManager.Calculate(homeLoanModel, out errorMessage);
                                homeLoanModel.BankAccount.Balance = mortgageAcct.Value;
                                todoProgressDto.IsOnTrack = homeLoanOutputModel.IsOnTrack(mortgageAcct);
                                todoProgressDto.BankAccountModel = homeLoanModel.BankAccount;
                                todoProgressDto.GoalTarget = homeLoanOutputModel.Principal;
                            }
                        }
                    }
                    break;
                case CalculatorType.RetirementGap:
                    // TODO RetirementGap - calculate progress
                    break;

            }
            if (isGoalProgress && todoProgressDto.BankAccountModel != null)
            {
                progress.TodoProgress = (int)(todoProgressDto.BankAccountModel.Balance > todoProgressDto.GoalTarget
                    ? 0
                    : Math.Round((1 - (todoProgressDto.BankAccountModel.Balance / todoProgressDto.GoalTarget)) * 100, 0));
                progress.IsOnTrack = todoProgressDto.IsOnTrack;
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                ExceptionHelper.Log(new ApplicationException(errorMessage));
            }

            return progress;
        }

        protected static DateTime TotalAssetsLastUpdatedTime = DateTime.MinValue;

        protected bool IsRetrievingTotalAssetValue = false;

        protected static Object Lock = new Object();

        public long GetTotalAssetValue()
        {
            var retVal = Convert.ToInt64(MyProsperity.Framework.Caching.CacheHelper.GetItemFromCache("HomePageTotalAsset"));
            var defaultValue = Convert.ToInt64(ConfigurationManager.AppSettings["HomePageTotalAssetDefaultValue"]);
            var totalHours = ConfigHelper.TryGetOrDefault("HomePageNumberOfHoursToUpdate", 2);
            var invalidate = TotalAssetsLastUpdatedTime.AddHours(totalHours) < DateTime.Now;

            if (!invalidate && retVal != 0)
            {
                LogHelper.LogInfo("homepage don't need to retreive data");
                return retVal;
            }

            if (!Monitor.TryEnter(Lock))
            {
                LogHelper.LogInfo("homepage object is locked");
                return retVal == 0
                           ? defaultValue
                           : retVal;
            }

            lock (Lock)
            {
                LogHelper.LogInfo("homepage object is not locked");
                try
                {
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    var total = Context.Current.Database.SqlQuery<decimal>("CalculateTotalAssets").ToList<decimal>().FirstOrDefault();
                    sw.Stop();
                    LogHelper.LogInfo("homepage time lapse for retreving total assets: " + sw.ElapsedMilliseconds + "ms");
                    retVal = Convert.ToInt64(total);
                    TotalAssetsLastUpdatedTime = DateTime.Now;
                    MyProsperity.Framework.Caching.CacheHelper.AddItemToCache("HomePageTotalAsset", retVal);
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(ex);
                }
            }

            return retVal == 0
                      ? defaultValue
                      : retVal;

        }

        /// <summary>
        /// Includes hidden wealth items
        /// </summary>
        /// <param name="account"></param>
        /// <param name="includeDummyItems"></param>
        /// <returns></returns>
        public int WealthItemCount(Account account, bool includeDummyItems = false)
        {
            DB db = DB;

            var entityIDs = from aa in account.Access
                            select aa.Entity.ID;
            return db.WealthItems.Count(r => r.Owners.Any(o => entityIDs.Contains(o.Entity.ID) && (includeDummyItems || !r.IsDummy)));
        }

        public IEnumerable<WealthItemWithOwnerAccount> GetPropertiesMatchedByName(string searchtext, bool includeDummyItems = false)
        {
            var loweredSeachtext = searchtext.ToLower();

            var wealthItems = (from wi in DB.WealthItems
                               join o in DB.WealthItemOwners on wi.ID equals o.WealthItem_ID
                               join aa in DB.AccountAccesses on o.Entity_ID equals aa.EntityID
                               where wi.AddressStreet1 != null && wi.Name.ToLower().Contains(loweredSeachtext) && (includeDummyItems || !wi.IsDummy)
                               select new WealthItemWithOwnerAccount
                               {
                                   AccountEmail = aa.Account.EmailAddress,
                                   AccountId = aa.Account.ID,
                                   WealthItemId = wi.ID,
                                   WealthItemName = wi.Name
                               }).Distinct(x => x.WealthItemId);

            return wealthItems;
        }

        private class TodoProgressDto
        {
            public decimal GoalTarget { get; set; }
            public BankAccountModel BankAccountModel { get; set; }
            public bool? IsOnTrack { get; set; }
        }
    }
}
