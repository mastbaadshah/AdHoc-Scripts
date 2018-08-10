using Data;
using Data.Model;
using MyProsperity.Business.Interfaces;
using MyProsperity.Framework;
using MyProsperity.Framework.Logging;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace Business
{

    public class SnapshotService : DBService, ISnapshotService
    {
        public IWealthService WealthService { get; set; }
        public IAccountService AccountService { get; set; }

        public SnapshotService(DBContext context)
            : base(context)
        {
        }

        public DateTime LastSnapshotPeriod()
        {
            var db = DB;

            var dt = db.Snapshots.Select(x => x.Period).DefaultIfEmpty().Max();

            return dt;

        }

        public DateTime LastSnapshotTaken()
        {
            var db = DB;

            var dt = db.Snapshots.Select(x => x.DateTaken).DefaultIfEmpty().Max();

            return dt;
        }

        public Snapshot LastSnapshot(Account account)
        {
            using (Profiler.Step("SnapshotService.LastSnapshot"))
            {
                IEnumerable<Snapshot> snaps = GetSnapshots(account);

                var qry = (from i in snaps
                           let firstDate = i.DateTaken.Day == 1
                           where firstDate
                           orderby i.Period descending
                           select i).Take(1).FirstOrDefault();

                return qry;
            }
        }

        public bool CreateSnapshot(Account account, DateTime period)
        {
            using (Profiler.Step("SnapshotService.CreateSnapshot" + account.ID))
            {
                if (account == null)
                    throw new ArgumentNullException("account");

                var retVal = false;
                try
                {
                    var entity = account.GetEntity();
                    if (entity == null)
                        throw new NullReferenceException(string.Format("Entity is null for Account ID: {0} with email: {1}", account.ID, account.EmailAddress));
                    var entitytoUse = DB.Entities.FirstOrDefault(x => x.ID == entity.ID);

                    var wealthItems = WealthService.GetWealthItems(account, excludeHiddenItem: true, includeDummyItems: false);

                    var frequency = Convert.ToInt32(ConfigHelper.TryGetOrDefault("WealthItemSnapshotFrequency", "0"));
                    var dateTimeToProcess = DateTime.Now.AddDays(-frequency).Date;

                    CreateNetWealthSnapshot(account, period, entitytoUse, wealthItems);
//
                    if (dateTimeToProcess > account.LastSnapshotTaken)
                    {
//                        CreateNetWealthSnapshotTest(period, wealthItems);
                        CreateWealthItemsSnapshot(period, wealthItems);
                    }

                    account.LastSnapshotTaken = DateTime.Now;
//                    AccountService.UpdateAccount(account);
                    DB.SaveChanges();
                    retVal = true;
                }
                catch (Exception ex)
                {
                    ExceptionHelper.HandleException(RequestContext.Current, ex, false, account.EmailAddress);
                }

                return retVal;
            }
        }

        public void CreateNetWealthSnapshot(Account account, DateTime period, Entity entity, IEnumerable<WealthItem> wealthItems)
        {
            using (Profiler.Step("SnapshotService.CreateNetWealthSnapshot" + account.ID))
            {
                var netWorth = WealthService.GetNetWorthSummary(account, false, wealthItems.ToList());

                var newSnapshot = new Snapshot
                {
                    NetWorth = netWorth.NetWorth,
                    DateTaken = DateTime.Now,
                    Period = period,
                    Owners = SnapshotOwner.Sole(entity)
                };

                DB.Snapshots.Add(newSnapshot);
            }
        }
        
//        private void CreateNetWealthSnapshotTest(DateTime period, List<WealthItem> wealthItems)
//        {
//            foreach (var wealthItem in wealthItems)
//            {
//                var pH = new WealthItemHistoryTest
//                {
//                    Date = DateTime.Now.Date,
//                    Value = wealthItem.Value,
//                    WealthItem = wealthItem,
//                    Period = period
//                };
//
//                DB.WealthItemHistoryTests.Add(pH);
//            }
//        }

        public void CreateWealthItemsSnapshot(DateTime period, IEnumerable<WealthItem> wealthItems)
        {
            foreach (var wealthItem in wealthItems)
            {
                var pH = new WealthItemHistory
                {
                    Date = DateTime.Now.Date,
                    Value = wealthItem.Value,
                    WealthItem = wealthItem,
//                    Period = period
                };
                DB.WealthItemHistories.Add(pH);
            }
        }

        public Snapshot GetSnapshotForSpecificMonth(Account account, int monthIndex)
        {
            var snapshots = GetMonthlySnapshots(account).Take(12).OrderByDescending(x => x.Period);
            return snapshots.FirstOrDefault(x => x.Period.Month == monthIndex);
        }

        public Decimal CalculatePercentageDifferenceBetweenTheLastSnapshots(Account account, int numberOfMonths)
        {
            var snapShots = GetMonthlySnapshots(account).OrderByDescending(x => x.Period).Take(numberOfMonths);
            var newSnapshot = snapShots.FirstOrDefault();
            var oldSnapshot = snapShots.LastOrDefault();
            var newNetworth = newSnapshot != null ? newSnapshot.NetWorth : 0M;
            var oldNetworth = oldSnapshot != null ? oldSnapshot.NetWorth : 0M;
            return newNetworth == 0 ? 0 : (newNetworth - oldNetworth) / newNetworth;
        }

        public IEnumerable<Snapshot> GetMonthlySnapshots(Account account)
        {
            IEnumerable<Snapshot> snaps = GetSnapshots(account);
             
            var startDate = account.ActivateDate ?? account.CreateDate;
            var dates = (from i in snaps
                         group i by i.DateTaken.ToString("yyyy-MM")
                             into g
                             select g.Min(s => s.DateTaken)).ToList();

            var qry = (from i in snaps
                       let isFirstOfMonth = dates.Contains(i.DateTaken)
                       let isLaterThanJoinDate = i.Period > startDate
                       where isFirstOfMonth && isLaterThanJoinDate
                       orderby i.Period
                       select i);

            return qry.ToList();
        }

        public IEnumerable<Snapshot> GetMonthlySnapshots_Old(Account account)
        {
            var db = DB;
            var entityIDs = from aa in account.Access
                            where aa.IsCreator
                            select aa.Entity.ID;

            var startDate = account.ActivateDate ?? account.CreateDate;

            var dates = (from i in db.Snapshots.Where(s => s.Owners.Any(o => entityIDs.Contains(o.Entity.ID))).ToList()
                         group i by i.DateTaken.ToString("yyyy-MM")
                             into g
                             select g.Min(s => s.DateTaken)).ToList();

            var qry = (from i in db.Snapshots
                let isOk = i.Owners.Any(o => entityIDs.Contains(o.Entity.ID))
                let isFirstOfMonth = dates.Contains(i.DateTaken)
                let isLaterThanJoinDate = i.Period > startDate
                where isOk && isFirstOfMonth && isLaterThanJoinDate
                orderby i.Period
                select i);

            return qry.ToList();
        }

        public IEnumerable<Snapshot> GetSnapshots(Account account)
        {
            using (Profiler.Step("SnapshotService.GetSnapshots(Account)"))
            {
                IEnumerable<Snapshot> snaps = new List<Snapshot>();
                try
                {
                    var access = account.Access.FirstOrDefault(a => a.IsCreator);
                    if (access != null)
                    {
                        var entityId = access.EntityID;
                        snaps = DB.Snapshots.Where(s => s.Owners.Any(o => o.Entity.ID == entityId)).ToList();
                    }
                }
                catch (Exception ex)
                {
                    ExceptionHelper.HandleException(ex);
                }

                return snaps;
            }
        }

        public IEnumerable<Snapshot> GetSnapshots(Entity entity)
        {
            using (Profiler.Step("SnapshotService.GetSnapshots(Entity)"))
            {
                return DBExtensions.Include(DB.Snapshots, s => s.Owners).Where(s => s.Owners.Any(o => o.Entity.ID == entity.ID)).ToList();
            }
        }

        public IEnumerable<Snapshot> GetSnapshots_Old(Account account)
        {
            var db = DB;
            var entityIDs = from aa in account.Access
                            where aa.IsCreator
                            select aa.Entity.ID;
            var qry = (from i in db.Snapshots
                       let isOk = i.Owners.Any(o => entityIDs.Contains(o.Entity.ID))
                       where isOk
                       select i);

            return qry.ToList();
        }   
    }
}