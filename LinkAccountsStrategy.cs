using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Data;
using Data.Enumerations.Notifications;
using Data.Model.BatchTasks;
using Data.Model.Partners;
using Data.Model.UserPendingActions;
using MyProsperity.Business.Interfaces;
using MyProsperity.Framework;
using MyProsperity.Framework.Logging;
using MyProsperity.Resources;
using StructureMap;

namespace Business.BatchTasks
{
    public class LinkAccountsStrategy : ITaskStrategy
    {
        private readonly IYodleeService _yodleeService;
        private readonly IScoreService _scoreService;
        private readonly IBatchTasksNotifier _batchTasksNotifier;
        private readonly ITaskService _taskService;
        private readonly PendingActionService _pendingActionsService;
        private readonly IAccountService _accountService;

        public LinkAccountsStrategy()
        {
            _yodleeService = ObjectFactory.GetInstance<IYodleeService>();
            _accountService = ObjectFactory.GetInstance<IAccountService>();
            _pendingActionsService = ObjectFactory.GetInstance<PendingActionService>();
            _taskService = ObjectFactory.GetInstance<ITaskService>();
            _batchTasksNotifier = ObjectFactory.GetInstance<IBatchTasksNotifier>();
            _scoreService = ObjectFactory.GetInstance<IScoreService>();
        }

        public bool Execute(TaskAbstract taskAbstract)
        {
            var task = taskAbstract as LinkAccountsTask;
            var itemIds = task.ItemIds;
            var account = _accountService.Get(task.AccountID);
            var accountant = _accountService.GetAccountantsFromAccount(account);
            var isEmailNotificationRequired = account != null && account.CobrandToUse != null && account.CobrandToUse.CobrandSettings != null
                ? account.CobrandToUse.CobrandSettings.EnableEmailNotification
                : false;
            var context = task.Context as RequestContext;
            var taskID = task.ID;
            var wealthService = ObjectFactory.GetInstance<IWealthService>();
            var reportProgress = _taskService.GetTaskProgressReporter(taskID);
            var sendNotificationEmail = Convert.ToBoolean(ConfigurationManager.AppSettings["SendFinancialAccountAddedEmail"]);
            int? wealthItemReplacementId = null;
            if (task.WealthItemReplacementIds != null)
                wealthItemReplacementId = task.WealthItemReplacementIds.FirstOrDefault();
           
            var success = _yodleeService.AddYodleeAccounts(itemIds, account, context, reportProgress, sendNotificationEmail, wealthItemIdToReplace:wealthItemReplacementId);
            
            if (success)
            {
                if (wealthItemReplacementId.HasValue)
                {
                    var wealthItemToReplace = wealthService.GetWealthItem(wealthItemReplacementId.Value);
                    if (wealthItemToReplace == null)
                        throw new Exception(string.Format("WealthItemToReplaceId is null for linkaccounttask. WealthitemToReplaceid:{0} ", wealthItemReplacementId.Value));

                    var owner = wealthItemToReplace.Owners.Select(x => x.Entity).FirstOrDefault();
                    
                    wealthService.UpdateCategories(owner, task.WealthItemCategoryAssociations);
                    wealthService.DeleteWealthItem(wealthItemToReplace);
                }
                else
                {
                    wealthService.UpdateCategories(account.GetEntity(), task.WealthItemCategoryAssociations);
                }
                
                _pendingActionsService.AddAccountsLinked(account.ID, task.ID);
                _scoreService.CalculateScoreAsync(account);
                _taskService.UpdateTaskStatusText(task.ID, "Accounts successfully added.");
            }
            else
            {
                _pendingActionsService.AddGenericMessagePA(account.ID, "Financial Accounts Linking",
                                                           "We've encountered an error trying to link your financial accounts. Please try again later. We apologise for the inconvenience.",
                                                           new[] {FuncArea.WEALTH}, task.ID);

                var adminMessage = string.Format("LinkAccounts Task failed. TaskID: {0}. AccountID: {1} Email:{2}",
                                                 task.ID, account.ID, account.EmailAddress);
                _taskService.UpdateTaskStatusText(task.ID, MPRWealth.YodleeAccountRefreshFailure);
                _batchTasksNotifier.NotifyAdmins(adminMessage, string.Format("BatchTask ERROR. LinkAccountsTask{0} failed", task.ID));
                SendBankAccountFailureEmailNotification(account, accountant, isEmailNotificationRequired);
            }

            return success;
        }

        private static void SendBankAccountFailureEmailNotification(Account account, IEnumerable<TaxPartnerAgent> accountant, bool emailNotificationRequired)
        {
            var sendEmailToClient = ApplicationNotification.SendBankAccountFailNotificationToClient(MessageType.Email, account);

            if (emailNotificationRequired)
            {
                foreach (var acc in accountant)
                {
                    var sendEmailToAdviser =
                        ApplicationNotification.SendBankAccountFailNotificationToAdvisor(MessageType.Email, account, acc);
                }

            }

            if (!sendEmailToClient)
            {
                ExceptionHelper.HandleException(new Exception("Could not sent email"), false);
            }
        }
    }

}