using System;
using System.Collections.Generic;
using Data;
using Data.Enumerations.Notifications;
using Data.Model.BatchTasks;
using Data.Model.Partners;
using Data.Services.Cyrpto;
using MyProsperity.Business.Interfaces;
using MyProsperity.Framework;
using MyProsperity.Framework.Logging;
using StructureMap;

namespace Business.BatchTasks
{
    public class ForceAccountRefreshStrategy : ITaskStrategy
    {
        private readonly IAccountService _accountService;
        private readonly ITaskService _taskService;
        private ISecureData _secureData { get; set; }

        public ForceAccountRefreshStrategy()
        {
            _accountService = ObjectFactory.GetInstance<IAccountService>();
            _taskService = ObjectFactory.GetInstance<ITaskService>();
            _secureData = ObjectFactory.GetInstance<ISecureData>();
        }

        public bool Execute(TaskAbstract taskAbstract)
        {
            var task = (ForceAccountRefreshTask) taskAbstract;
            var itemId = task.ItemID;
            var account = _accountService.Get(task.AccountID);
            var accountant = _accountService.GetAccountantsFromAccount(account);
            var isEmailNotificationRequired = account != null && account.CobrandToUse != null && account.CobrandToUse.CobrandSettings != null
                ? account.CobrandToUse.CobrandSettings.EnableEmailNotification
                : false;
            var forceRefreshRunner = ObjectFactory.GetInstance<ForceRefreshFacade>();
            var reportProgress = _taskService.GetTaskProgressReporter(task.ID);

            var context = RequestContext.Current;

            var numWealthItemsProcessed = 0;
            var result = forceRefreshRunner.ForceRefreshAccount(context, account, itemId, out numWealthItemsProcessed, reportProgress);

            if (result && numWealthItemsProcessed > 0 && task.ShouldSendUpdateNotification)
            {
                // Send email informing user that their financial accounts have updated
                ApplicationNotification.SendEmailForFinancialAccountsUpdated(MessageType.Email, account, numWealthItemsProcessed);
            }
            else
            {
                SendBankAccountFailureEmailNotification(account, accountant, isEmailNotificationRequired);
            }
            return false; 
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
