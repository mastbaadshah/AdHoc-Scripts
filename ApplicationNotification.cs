using Business.Notifications;
using CreativeFactory.Images.Configuration;
using CreativeFactory.MVC;
using Data;
using Data.Enumerations.DarkWorld;
using Data.Enumerations.Notifications;
using Data.Model.AdviserBilling;
using Data.Model.Cobrand;
using Data.Model.DocumentSigning;
using Data.Model.FuelCard;
using Data.Model.Leads;
using Data.Model.Partners;
using Data.Model.RealEstate;
using Data.Model.Score;
using MyProsperity.Business.Interfaces;
using MyProsperity.Framework;
using MyProsperity.Framework.Logging;
using MyProsperity.Notification;
using MyProsperity.Notification.Messages;
using StructureMap;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Web;
using Data.Model;
using MyProsperity.Framework.Extensions;
using MyProsperity.Framework.Helpers;
using MyProsperity.Framework.Model.Enums;
using MessageType = Data.Enumerations.Notifications.MessageType;

namespace Business
{
    public class ApplicationNotification
    {
        public const string RAW_HTML_EMAIL_MARKUP = "RawHTML_";

        private static readonly Dictionary<MessageType, Func<ApplicationNotificationBase>> strategies =
            new Dictionary<MessageType, Func<ApplicationNotificationBase>>();

        private static readonly Dictionary<TemplatePlaceholderStrategy, Func<NotificationTemplateStrategy>> placeholderStrategies =
            new Dictionary<TemplatePlaceholderStrategy, Func<NotificationTemplateStrategy>>();

        static ApplicationNotification()
        {
            strategies.Add(MessageType.Email, () => IocHelper.Get<EmailApplicationNotification>());
            strategies.Add(MessageType.SMS, () => IocHelper.Get<SmsApplicationNotification>());

            placeholderStrategies.Add(TemplatePlaceholderStrategy.None, () => IocHelper.Get<NotificationTemplateStrategy>());
            placeholderStrategies.Add(TemplatePlaceholderStrategy.Basic, () => IocHelper.Get<BasicPlaceholders>());
        }

        public static ApplicationNotificationBase GetStrategy(MessageType messageType)
        {
            var strategy = strategies.FirstOrDefault(a => a.Key == messageType).Value;

            if (strategy == null)
                throw new ArgumentException("messageType is not valid");

            return strategy.Invoke();
        }

        public static NotificationTemplateStrategy GetTemplatePlaceholderStrategy(TemplatePlaceholderStrategy templatePlaceholderStrategy)
        {
            var placeholderStrategy = placeholderStrategies.FirstOrDefault(a => a.Key == templatePlaceholderStrategy).Value;

            if (placeholderStrategy == null)
                throw new ArgumentException("templatePlaceholderStrategy is not valid");

            return placeholderStrategy.Invoke();
        }

        public static string GetGifRequest(string emailTemplateName)
        {
            var googleAnalyticsId = ConfigurationManager.AppSettings["googleAnalyticsId"];
            var random = new Random();
            var randomNumber1 = random.Next(0, 1000000000).ToString("D10");
            var randomNumber2 = random.Next(0, 1000000000).ToString("D10");
            var randomNumber3 = random.Next(0, 1000000000).ToString("D10");
            var sb = new StringBuilder();

            bool useGaEmailEventTracking = ConfigHelper.TryGetOrDefault("UseGaEmailEventTracking", true);

            if (useGaEmailEventTracking)
            {
                // example: https://www.google-analytics.com/collect?v=1&tid=UA-34481516-1&cid=4444&t=event&ec=email&ea=open&el=55555&cs=HealthyHabitsLogin&cm=s5email&cn=LogBackInFree1
                string baseURL = "https://www.google-analytics.com/collect";
                string GaVersion = "1";
                string el = emailTemplateName;  // event label
                string ea = "open";             // event action
                string cs = "MPAD-OPEN";        // campaign source
                string cm = "email";            // campaign medium
                string cn = emailTemplateName;  // campaign name

                sb.Append(baseURL);
                sb.Append("?v=" + GaVersion);
                sb.Append("&tid=" + googleAnalyticsId);
                sb.Append("&cid=" + randomNumber1 + "." + randomNumber2);
                sb.Append("&t=event&ec=email&ea=" + ea);
                sb.Append("&el=" + el);
                sb.Append("&cs=" + cs);
                sb.Append("&cm=" + cm);
                sb.Append("&cn=" + cn);
            }
            else // use old GIF tracking
            {
                var hostname = "myprosperity.com.au";
                var utmp = "/EmailNotification";
                var utmac = googleAnalyticsId;
                var currentTimeStamp = (DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;
                var domainHash = "73520398.";
                var firstlastvisit = ".1367987126.1367987126.";
                var sessioncount = ".1";
                var utma = domainHash + randomNumber3 + firstlastvisit + currentTimeStamp + sessioncount;
                var utmz = domainHash + currentTimeStamp + sessioncount + ".1.";
                var utmcsr = emailTemplateName;
                var utmccn = "MPAD-OPEN";
                var utmcmd = "Email";
                var cookieRaw = "__utma=" + utma + ";+__utmz=" + utmz + "utmcsr=" + utmcsr + "|utmccn=" + utmccn +
                               "|utmcmd=" +
                               utmcmd + ";";
                var utmcc = HttpUtility.HtmlEncode(cookieRaw);

                sb.Append("https://ssl.google-analytics.com/__utm.gif?");
                sb.Append("utmwv=4.3&");
                sb.Append("utmn=" + randomNumber1 + "&");
                sb.Append("utmhid=" + randomNumber2 + "&");
                sb.Append("utmhn=" + hostname + "&");
                sb.Append("utmr=0&");
                sb.Append("utmp=" + utmp + "&");
                sb.Append("utmac=" + utmac + "&");
                sb.Append("utmcc=" + utmcc);
            }

            return sb.ToString();
        }

        // aka GetLogInUrl
        public static string GetSignInUrl(Account account, ICobrandService cobrandService)
        {
            if (account == null)
                throw new ArgumentNullException("account");
            if (cobrandService == null)
                throw new ArgumentNullException("cobrandService");

            var cobrand = cobrandService.GetCobrandByAccount(account);
            string baseDesktopUrl = cobrandService.GetBaseUrlDesktop(cobrand);

            return GetSignInUrl(baseDesktopUrl, cobrand);
        }

        public static string GetSignInUrl(string baseDesktopUrl, Cobrand cobrand)
        {
            return cobrand != null
                ? string.Format("{0}/Accounts/SignIn/SignIn?cobrand={1}", baseDesktopUrl, cobrand.Token)
                : baseDesktopUrl + "/Accounts/SignIn/SignIn";
        }

        public static string GetClientTaxUrl(Account account, ICobrandService cobrandService)
        {
            if (account == null)
                throw new ArgumentNullException("account");
            if (cobrandService == null)
                throw new ArgumentNullException("cobrandService");

            var cobrand = cobrandService.GetCobrandByAccount(account);
            string baseDesktopUrl = cobrandService.GetBaseUrlDesktop(cobrand);

            return GetClientTaxUrl(baseDesktopUrl, cobrand);
        }

        public static string GetClientTaxUrl(string baseDesktopUrl, Cobrand cobrand)
        {
            return cobrand != null
                ? string.Format("{0}/TaxReturn?cobrand={1}", baseDesktopUrl, cobrand.Token)
                : baseDesktopUrl + "/TaxReturn";
        }

        private static string EnsureUrlHasCobrandToken(string url, string accountEmail)
        {
            if (string.IsNullOrEmpty(url)) return url;

            var urlWithCobrandToken = url;
            if (!urlWithCobrandToken.Contains("cobrand="))
            {
                var cobrandService = ObjectFactory.GetInstance<ICobrandService>();
                var cobrand = cobrandService.GetCobrandByEmailAddress(accountEmail);
                if (cobrand != null)
                {
                    var tokenParam = "cobrand=" + cobrand.Token;
                    var separator = urlWithCobrandToken.Contains("?") ? "&" : "?";

                    urlWithCobrandToken = urlWithCobrandToken + separator + tokenParam;
                }
            }
            return urlWithCobrandToken;
        }

        public static string EnsureEmailUrlHasGaTrackingTags(string url, string utm_source, string utm_campaign = "MPAD")
        {
            if (string.IsNullOrEmpty(url)) return url;

            // example tracking tags: &utm_campaign=MPAD&utm_medium=email&utm_source=ActivateAccount
            string urlWithTrackingTags = url;

            if (!url.Contains("utm_campaign") && !url.Contains("utm_medium") && !url.Contains("utm_source"))
            {
                string separator = url.Contains("?") ? "&" : "?";

                var sb = new StringBuilder();
                sb.Append(url);
                sb.Append(separator + "utm_campaign=" + utm_campaign);
                sb.Append("&utm_medium=email&utm_source=");
                sb.Append(utm_source);
                urlWithTrackingTags = sb.ToString();
            }

            return urlWithTrackingTags;
        }

        public static string GetFromEmailAddressFromConfig()
        {
            return ConfigHelper.TryGetOrDefault("FromEmailAddress", "no-reply@myprosperity.com.au");
        }

        public static string GetFromNameFromConfig()
        {
            return ConfigHelper.TryGetOrDefault("FromName", "myprosperity");
        }

        /// <summary>
        /// Core helper method to create the message
        /// </summary>
        /// <param name="toAddress"></param>
        /// <param name="variables"></param>
        /// <param name="strategy"></param>
        /// <param name="subject"></param>
        /// <param name="templateName"></param>
        /// <param name="username"></param>
        /// <returns></returns>
        public static IMessage ConstructMessage(string toAddress, Dictionary<string, string> variables,
            ApplicationNotificationBase strategy, string subject, string templateName, string username)
        {
            string fromEmailAddress = string.Empty;
            string fromName = string.Empty;
            var parameters = new ApplicationNotificationParameters(variables, toAddress, subject, username, templateName, out fromEmailAddress, out fromName);
            var msg = strategy.NewMessage(subject, toAddress, templateName, parameters.ParameterDictionary, fromEmailAddress, fromName);

            return msg;
        }

        public static IMessage ConstructMessage(string toAddress, Dictionary<string, string> variables,
            MessageType messageType, string subject, string templateName, string username)
        {
            var strategy = GetStrategy(messageType);
            return ConstructMessage(toAddress, variables, strategy, subject, templateName, username);
        }

        /// <summary>
        /// Core helper method to create and send the message
        /// </summary>
        /// <param name="toAddress"></param>
        /// <param name="variables"></param>
        /// <param name="strategy"></param>
        /// <param name="subject"></param>
        /// <param name="templateName"></param>
        /// <param name="username"></param>
        /// <param name="sendViaNotificationQueueAsync"></param>
        /// <param name="documentGroup"></param>
        /// <param name="synchronousAttachmentFilePaths"></param>
        /// <returns></returns>
        public static bool ConstructAndSendMessage(string toAddress, Dictionary<string, string> variables,
            ApplicationNotificationBase strategy, string subject, string templateName, string username,
            bool sendViaNotificationQueueAsync = false, DocumentGroup documentGroup = null, List<string> synchronousAttachmentFilePaths = null)
        {
            var result = false;
            var msg = ConstructMessage(toAddress, variables, strategy, subject, templateName, username);

            if (sendViaNotificationQueueAsync)
            {
                var documentGroupId = documentGroup != null && documentGroup.ID > 0 ? documentGroup.ID : (int?)null;
                result = (ObjectFactory.GetInstance<INotificationService>()).SendMessageViaNotificationQueue(
                    toAddress, templateName, DateTime.Now, msg, documentGroupId);
            }
            else
            {
                if (synchronousAttachmentFilePaths.AnyAndNotNull() && strategy is EmailApplicationNotification)
                {
                    foreach (string filePath in synchronousAttachmentFilePaths)
                    {
                        Attachment data = new Attachment(filePath, MediaTypeNames.Application.Octet);
                        msg.Attachments.Add(data);
                    }
                }

                var primaryDocumentOnly = ConfigHelper.TryGetOrDefault("AttachPrimaryDocumentGroupDocOnly", true);
                AttachFilesInDocumentGroupToMessage(documentGroup, msg, primaryDocumentOnly);

                result = strategy.Send(msg);
            }

            return result;
        }

        public static void AttachFilesInDocumentGroupToMessage(DocumentGroup documentGroup, IMessage msg, bool primaryDocumentOnly)
        {
            var docFilesDownloaded = DocumentService.DownloadFilesToLocalServerByDocumentGroup(documentGroup, primaryDocumentOnly);
            var docFilePaths = new List<string>();
            if (docFilesDownloaded.AnyAndNotNull())
                docFilePaths = docFilesDownloaded.Select(x => x.PhysicalPath).ToList();
            else if (documentGroup != null)
                throw new Exception("Problem trying to attach files in document group to message, no documents downloaded to local server for DocumentGroupID: " + documentGroup.ID);

            foreach (var filePath in docFilePaths)
            {
                try
                {
                    Attachment data = new Attachment(filePath, MediaTypeNames.Application.Octet);
                    msg.Attachments.Add(data);
                }
                catch (Exception ex)
                {
                    ExceptionHelper.Log(new Exception(string.Format("{0} docs to upload in DocumentGroupID {1}. Problem trying to attach document with filepath: {2}",
                        docFilesDownloaded.Count, documentGroup != null ? documentGroup.ID.ToString() : "doc group is null (which is a problem!)", filePath)));
                    throw ex;
                }

            }
        }
        public static bool ConstructAndSendMessage(string toAddress, Dictionary<string, string> variables,
            MessageType messageType, string subject, string templateName, string username, bool sendViaNotificationQueueAsync = false,
            DocumentGroup documentGroup = null, List<string> synchronousAttachmentFilePaths = null)
        {
            var strategy = GetStrategy(messageType);
            return ConstructAndSendMessage(toAddress, variables, strategy, subject, templateName, username,
                sendViaNotificationQueueAsync, documentGroup, synchronousAttachmentFilePaths);
        }


        public static string GetUserDisplayName(Account account)
        {
            string displayName = string.Empty;

            if (account != null)
            {
                Entity entity = account.GetEntity();
                displayName = (entity != null) ? entity.DisplayName.ToTitleCase() : account.DisplayName.ToTitleCase();
            }

            return displayName;
        }
        public static string GetUserFirstNameOrDisplayName(Account account)
        {
            string FirstName = string.Empty;

            if (account != null)
            {
                Entity entity = account.GetEntity();
                FirstName = (entity != null && !string.IsNullOrEmpty(entity.FirstName) )?entity.FirstName : entity != null ? entity.DisplayName : account.DisplayName.ToTitleCase();
            }

            return FirstName;
        }

        // Notification Methods

        public static bool ScheduleActivateAccount(MessageType messageType, string to, string userName,
                                                   DateTime expiryDate, string activateUrl, string ipAddress)
        {
            activateUrl = EnsureUrlHasCobrandToken(activateUrl, to);
            var variables = new Dictionary<string, string>
                                {
                                    {"type", "PasswordReset"},
                                    {"activateUrl", activateUrl},
                                    {"ipAddress", ipAddress},
                                    {"ExpiryDate", expiryDate.ToString("d MMM yyyy h:mm tt")},
                                };

            return ConstructAndSendMessage(to, variables, messageType, "ACTION REQUIRED: Activate your account", "ActivateAccount", userName);
        }

        public static bool SendChangeEmailPhase1(MessageType messageType, string to, string userName, string verificationUrl,
                                       string ipAddress)
        {
            verificationUrl = EnsureEmailUrlHasTags(verificationUrl, to, "ChangeEmailPhase1");
            var variables = new Dictionary<string, string>
                                {
                                    {"type", "PasswordReset"},
                                    {"verificationUrl", verificationUrl},
                                    {"ipAddress", ipAddress},
                                };

            return ConstructAndSendMessage(to, variables, messageType, "ACTION REQUIRED: Verify your current email address", "ChangeEmailPhase1", userName);
        }

        public static bool SendChangeEmailPhase2(MessageType messageType, string to, string userName, string verificationUrl,
                               string oldEmailAddress, string ipAddress)
        {
            verificationUrl = EnsureEmailUrlHasTags(verificationUrl, oldEmailAddress, "ChangeEmailPhase2");
            var variables = new Dictionary<string, string>
                                {
                                    {"type", "PasswordReset"},
                                    {"verificationUrl", verificationUrl},
                                    {"ipAddress", ipAddress},
                                };

            return ConstructAndSendMessage(to, variables, messageType, "ACTION REQUIRED: Verify your new email address", "ChangeEmailPhase2", userName);
        }

        // IMPORTANT! This method is to be called only from within registration service method, otherwise might cause sending multiple activation emails
        public static bool SendActivateAccount(MessageType messageType, string to, string userName, string activateUrl,
                                               string ipAddress)
        {
            activateUrl = EnsureEmailUrlHasTags(activateUrl, to, "ActivateAccount");
            var variables = new Dictionary<string, string>
                                {
                                    {"type", "PasswordReset"},
                                    {"activateUrl", activateUrl},
                                    {"ipAddress", ipAddress},
                                };

            return ConstructAndSendMessage(to, variables, messageType, "ACTION REQUIRED: Activate your account", "ActivateAccount", userName);
        }

        public static bool SendAccountConversionEmail(MessageType messageType, string to, string userName, string confirmUrl)
        {
            confirmUrl = EnsureEmailUrlHasTags(confirmUrl, to, "AccountConversion");

            var variables = new Dictionary<string, string>
            {
                {"userName", userName},
                {"confirmUrl", confirmUrl}
            };

            return ConstructAndSendMessage(to, variables, messageType,
                "Setting you up on your firm’s portal", "AccountConversion", userName);
        }

        public static bool SendAgentActivateAccount(MessageType messageType, string to, string userName, string activateUrl,
                                               string ipAddress)
        {
            activateUrl = EnsureEmailUrlHasTags(activateUrl, to, "AgentActivateAccount");
            var variables = new Dictionary<string, string>
                                {
                                    {"type", "PasswordReset"},
                                    {"activateUrl", activateUrl},
                                    {"ipAddress", ipAddress},
                                };

            return ConstructAndSendMessage(to, variables, messageType, "ACTION REQUIRED: Activate your account", "ActivateAgentAccount", userName);
        }

        public static string EnsureEmailUrlHasTags(string url, string accountEmail, string utm_source, string utm_campaign = "MPAD", bool addCobrandToken = true)
        {
            var newurl = url;
            if (addCobrandToken)
                newurl = EnsureUrlHasCobrandToken(newurl, accountEmail);

            newurl = EnsureEmailUrlHasGaTrackingTags(newurl, utm_source, utm_campaign);

            return newurl;
        }

        public static bool SendPasswordReset(MessageType messageType, string to, string userName, string resetUrl,
                                             DateTime expiry, string ipAddress)
        {
            resetUrl = EnsureEmailUrlHasTags(resetUrl, to, "ResetPassword");
            var variables = new Dictionary<string, string>
            {
                {"type", "PasswordReset"},
                {"resetUrl", resetUrl},
                {"ipAddress", ipAddress},
                {"ExpiryDate", expiry.ToString("d MMM yyyy h:mm tt")},
            };

            return ConstructAndSendMessage(to, variables, messageType, "Reset Your Password", "ResetPassword", userName);
        }

        public static bool SendActivationInvitationToPartnerAgent(Account account, string pwResetToken, Cobrand cobrand)
        {
            return SendActivationInvitationToPartnerAgentOrClient(account, String.Empty, pwResetToken, cobrand);
        }

        public static bool SendActivationInvitationToClient(Account account, string pwResetToken, Cobrand cobrand,
            bool sendViaNotificationQueueAsync, string accountantName = "")
        {
            var useAccountantName = !string.IsNullOrEmpty(accountantName);
            return SendActivationInvitationToPartnerAgentOrClient(
                account, accountantName, pwResetToken, cobrand, useAccountantName, sendViaNotificationQueueAsync);
        }

        public static bool SendThirdPartyIntegrationResult(MessageType messageType, string to, string userName,
            DarkWorldType darkWorldType, bool result, int? accountId)
        {

            var variables = new Dictionary<string, string>
            {
                {"integrationtype", darkWorldType.GetDescription()},
                {"accountID", accountId.HasValue ? accountId.Value.ToString() : string.Empty}
            };

            return ConstructAndSendMessage(to, variables, messageType,
                string.Format(result ? "{0} integration complete" : "{0} integration failed",
                    darkWorldType.GetDescription()),
                result ? "ThirdPartyIntegrationSuccess" : "ThirdPartyIntegrationFail", userName);
        }

        private static bool SendActivationInvitationToPartnerAgentOrClient(Account account, string accountantName, string pwResetToken,
            Cobrand cobrand, bool useAccountantName = false, bool sendViaNotificationQueueAsync = true)
        {
            var resetUrl = RegistrationService.GetShortCircuitActivationUrl_Static(account, pwResetToken, cobrand);

            var entity = account.GetEntity();

            useAccountantName = (useAccountantName && !accountantName.IsNullOrEmpty());

            var friendlyMsg = useAccountantName
                ? accountantName + " has invited you to join their online real-time wealth portal. " +
                  "This will enable you to manage your finances by storing and accessing all your financial information anywhere, anytime on any device."
                : "You are invited to access our online real-time wealth portal.";

            var subject = useAccountantName
                ? "Invitation from " + accountantName
                : "Invitation " + ((cobrand != null && !cobrand.DisplayName.IsNullOrEmpty()) ? "from " + cobrand.DisplayName : "to join wealth portal");

            //SendActivationInvitationToClient(MessageType.Email,
            //    account.EmailAddress, entity.DisplayName, resetUrl, DateTime.Now.AddDays(1), friendlyMsg, subject);

            resetUrl = EnsureEmailUrlHasTags(resetUrl, account.EmailAddress, "NewUserOnboard");
            var variables = new Dictionary<string, string>
            {
                {"type", "PasswordReset"},
                {"resetUrl", resetUrl},
                {"msg", friendlyMsg},
                {"ExpiryDate", DateTime.Now.AddDays(1).ToString("d MMM yyyy h:mm tt")},
            };

            var accountService = ObjectFactory.GetInstance<IAccountService>();
            account.ActivationUrl = resetUrl;
            account.ActivationEmailSentDt = DateTime.Now;

            var success = accountService.UpdateAccount(account);

            if (!success)
            {
                SendEnquiryToAdmin("Activation Url not saved",
                    string.Format("Activation url not saved for accountId:{0}", account.ID));
                return false;
            }

            success = ConstructAndSendMessage(account.EmailAddress, variables, MessageType.Email,
                subject, "NewUserOnboard", entity.DisplayName, sendViaNotificationQueueAsync);

            if (!success)
                ExceptionHelper.Log(new Exception("Problem trying to send activation/invitation email for accountId: " + account.ID));

            return success;
        }

        //public static bool SendActivationInvitationToClient(MessageType messageType, string to, string userName, string resetUrl,
        //                                     DateTime expiry, string friendlyMsg, string subject)
        //{
        //    resetUrl = EnsureEmailUrlHasTags(resetUrl, to, "NewUserOnboard");
        //    var variables = new Dictionary<string, string>
        //    {
        //        {"type", "PasswordReset"},
        //        {"resetUrl", resetUrl},
        //        {"msg", friendlyMsg},
        //        {"ExpiryDate", expiry.ToString("d MMM yyyy h:mm tt")},
        //    };

        //    return ConstructAndSendMessage(to, variables, messageType, subject, "NewUserOnboard", userName, true);
        //}

        public static bool SendEnquiryFromUserToAdmin(MessageType messageType, string to, string enquiryEmail, string name,
                                             string subject, string company,
                                             string phone, string message, string title = "", string category = "", bool isBatchTasks = false)
        {
            subject = string.IsNullOrEmpty(subject)
                        ? "Customer contact: "
                        : "Customer contact: " + subject;

            var variables = new Dictionary<string, string>
                                {
                                    {"Title", title},
                                    {"Name", name},
                                    {"Company", company},
                                    {"Email", enquiryEmail},
                                    {"Phone", phone},
                                    {"Category", category},
                                    {"Message", message},
                                };

            return ConstructAndSendMessage(to, variables, messageType, subject, "EnquiryFromUserToAdmin", name);
        }

        public static bool SendEnquiryToAdmin(string subject, string message, string emailAddress, IEnumerable<HttpPostedFileBase> files)
        {
            subject = string.IsNullOrEmpty(subject) ? "Application admin notification" : subject;
            var strategy = GetStrategy(MessageType.Email);
            var variables = new Dictionary<string, string>
            {
                {"Message", message}
            };

            var msg =
                ConstructMessage(ConfigHelper.TryGetOrDefault("notificationAdminEmail", "info@myprosperity.com.au"),
                    variables, strategy, subject, "EnquirySentToAdmin", emailAddress);
            var success = true;
            foreach (var file in files.Where(file => file != null && file.ContentLength > 0))
            {
                try
                {
                    string fileName = Path.GetFileName(file.FileName);
                    var attachment = new Attachment(file.InputStream, fileName);
                    msg.Attachments.Add(attachment);
                }
                catch (Exception)
                {
                    success = false;
                }
            }

            return success && strategy.Send(msg);
        }

        public static bool SendEnquiryToAdmin(string subject, string message, Account account, IEnumerable<HttpPostedFileBase> files)
        {
            return SendEnquiryToAdmin(subject, message, account.EmailAddress, files);
        }

        public static bool SendEnquiryToAdmin(string subject, string message, string emailAddress, IEnumerable<string> filePaths)
        {
            subject = string.IsNullOrEmpty(subject) ? "Application admin notification" : subject;
            var strategy = GetStrategy(MessageType.Email);
            var variables = new Dictionary<string, string>
            {
                {"Message", message}
            };

            var msg =
                ConstructMessage(ConfigHelper.TryGetOrDefault("notificationAdminEmail", "info@myprosperity.com.au"),
                    variables, strategy, subject, "EnquirySentToAdmin", emailAddress);
            var success = true;
            foreach (var filePath in filePaths)
            {
                try
                {
                    if (System.IO.File.Exists(filePath))
                    {
                        Attachment data = new Attachment(filePath, MediaTypeNames.Application.Octet);
                        msg.Attachments.Add(data);
                    }
                }
                catch (Exception)
                {
                    success = false;
                }
            }

            return success && strategy.Send(msg);
        }

        public static bool SendEnquiryToAdmin(string subject, string message, Account account, IEnumerable<string> filePaths)
        {
            return SendEnquiryToAdmin(subject, message, account.EmailAddress, filePaths);
        }

        public static bool SendEnquiryToAdmin(string subject, string message)
        {
            return SendEnquiryToAdmin(ConfigHelper.TryGetOrDefault("notificationAdminEmail", "info@myprosperity.com.au"), // always send to mp
                subject, message);

        }
        public static bool SendEnquiryToAdmin(string to, string subject, string message)
        {
            return SendEnquiryToAdmin(MessageType.Email, to, subject, message);
        }

        public static bool SendEnquiryToAdmin(MessageType messageType, string to,
                                              string subject, string message)
        {
            subject = string.IsNullOrEmpty(subject) ? "Application admin notification" : subject;

            var variables = new Dictionary<string, string>
                                {
                                    {"Message", message}
                                };

            return ConstructAndSendMessage(to, variables, messageType, subject, "EnquirySentToAdmin", string.Empty);
        }

        public static bool SendEmailToBizinkAdmin(string subject, string cobrandName, string planAddonName, string accountEmail)
        {
            var to = ConfigHelper.TryGetOrDefault("BizinkAdminEmail", "developers@myprosperity.com.au");
            var variables = new Dictionary<string, string> {
                { "CobrandName", cobrandName },
                { "PlanAddonName", planAddonName },
                { "AccountEmail", accountEmail }
            };

            return ConstructAndSendMessage(to, variables, MessageType.Email, subject, "EmailToBizinkAdmin", string.Empty);
        }

        public static bool SendSuperReviewRequestToAccountant(MessageType messageType, string accountantName, string accountantEmail, string subject, string total, Entity entity)
        {
            subject = string.IsNullOrEmpty(subject) ? "Super review request" : subject;

            var variables = new Dictionary<string, string>
                                {
                                    {"Name", entity.Name },
                                    {"GrandTotal", total },
                                    {"PartnerName", accountantName }
                                };
            if (!entity.ContactPhone.IsNullOrEmpty())
                variables.Add("Phone", string.Format("Phone: {0}", entity.ContactPhone));
            if (!entity.PreferredEmailAddress.IsNullOrEmpty())
                variables.Add("Email", string.Format("Email: {0}", entity.PreferredEmailAddress));

            return ConstructAndSendMessage(accountantEmail, variables, messageType, subject, "SuperReviewRequestSentToAccountant", string.Empty);
        }

        public static bool SendDebtReviewRequestToAccountant(MessageType messageType, string accountantName, string accountantEmail, string subject, string total, Entity entity)
        {
            subject = string.IsNullOrEmpty(subject) ? "Debt review request" : subject;

            var variables = new Dictionary<string, string>
                                {
                                    {"Name", entity.Name },
                                    {"GrandTotal", total },
                                    {"PartnerName", accountantName }
                                };
            if (!entity.ContactPhone.IsNullOrEmpty())
                variables.Add("Phone", string.Format("Phone: {0}", entity.ContactPhone));
            if (!entity.PreferredEmailAddress.IsNullOrEmpty())
                variables.Add("Email", string.Format("Email: {0}", entity.PreferredEmailAddress));

            return ConstructAndSendMessage(accountantEmail, variables, messageType, subject, "DebtReviewRequestSentToAccountant", string.Empty);
        }

        public static bool SendContactUsFromAccountant(MessageType messageType, string name, string email, string phone, string cobrandName)
        {
            const string to = "janea@myprosperity.com.au";
            var contactInfo = string.Format("{0} with the cobrand {1}", name, cobrandName);
            var variables = new Dictionary<string, string>
                                {
                                    {"Name", contactInfo},
                                    {"Email", email},
                                    {"Phone",phone},
                                };

            return ConstructAndSendMessage(to, variables, messageType, "An accountant has contacted us", "AccountantContactUs", name);
        }

        public static bool SendContactUsFromREAgent(MessageType messageType, string name, string email, string phone, string cobrandName)
        {
            const string to = "janea@myprosperity.com.au";
            var contactInfo = string.Format("{0} with the cobrand {1}", name, cobrandName);
            var variables = new Dictionary<string, string>
                                {
                                    {"Name", contactInfo},
                                    {"Email", email},
                                    {"Phone",phone},
                                };

            return ConstructAndSendMessage(to, variables, messageType, "An RE Agent has contacted us", "AccountantContactUs", name);
        }

        public static bool SendPaymentFailDowngradeNotice(MessageType messageType, Account account, string cobrandToken)
        {

            var variables = new Dictionary<string, string>
                                {
                                    {"Name", account.DisplayName},
                                    {"Email", account.EmailAddress},
                                    {"cobrand", cobrandToken},

                                };

            return ConstructAndSendMessage(account.EmailAddress, variables, messageType, " Pro subscription has expired. Your account has been downgraded to Lite!", "PaymentFailureDowngraded", account.DisplayName);
        }

        public static bool SendThankYouForYourPayment(MessageType messageType, Account account, string cobrandToken)
        {

            var variables = new Dictionary<string, string>
                                {
                                    {"Name", account.DisplayName},
                                    {"Email", account.EmailAddress},
                                    {"cobrand", cobrandToken},

                                };

            return ConstructAndSendMessage(account.EmailAddress, variables, messageType, "You have upgraded to Pro!", "ThanksForPayment", account.DisplayName);
        }

        public static bool SendPaymentFailureRetryNotice(MessageType messageType, Account account, string cobrandToken)
        {
            var name = account.DisplayName;
            if (String.IsNullOrEmpty(name))
            {
                var cobrand = account.CobrandToUse;
                if (cobrand != null)
                {
                    name = cobrand.DisplayName;
                }
            }
            var variables = new Dictionary<string, string>
                                {
                                    {"Name", name},
                                    {"Email", account.EmailAddress},
                                    {"cobrand", cobrandToken},
                                };

            return ConstructAndSendMessage(account.EmailAddress, variables, messageType, "Notification for failed payment", "PaymentFailure", account.DisplayName);
        }

        public static bool SendPaymentFailureRetryNoticeToPartner(MessageType messageType, AdviserBilling adviserBilling, string cobrandToken)
        {
            if (adviserBilling == null)
                return false;
            var taxService = ObjectFactory.GetInstance<ITaxService>();
            var cobrand = adviserBilling.Cobrands.FirstOrDefault();
            if (cobrand == null)
                return false;

            var accountToMail = taxService.GetDefaultAgentAccountOrNextBestAgentAccountByCobrand(cobrand.ID);
            if (accountToMail == null)
                return false;

            var name = accountToMail.DisplayName;
            if (String.IsNullOrEmpty(name))
            {
                name = cobrand.DisplayName;
            }

            var variables = new Dictionary<string, string>
                                {
                                    {"Name", name},
                                    {"Email", accountToMail.EmailAddress},
                                    {"CobrandToken", cobrandToken}
                                };

            return ConstructAndSendMessage(accountToMail.EmailAddress, variables, messageType, "Notification for failed payment", "PaymentFailureRetryPartner", name);
        }


        public static bool SendPaymentFailureFinalNoticeToPartner(MessageType messageType,
            AdviserBilling adviserBilling, string cobrandToken)
        {
            if (adviserBilling == null)
                return false;
            var taxService = ObjectFactory.GetInstance<ITaxService>();
            var cobrand = adviserBilling.Cobrands.FirstOrDefault();
            if (cobrand == null)
                return false;

            var accountToMail = taxService.GetDefaultAgentAccountOrNextBestAgentAccountByCobrand(cobrand.ID);
            if (accountToMail == null)
                return false;

            var name = accountToMail.DisplayName;
            if (String.IsNullOrEmpty(name))
            {
                name = cobrand.DisplayName;
            }

            var variables = new Dictionary<string, string>
            {
                {"Name", name},
                {"Email", accountToMail.EmailAddress},
                {"CobrandToken", cobrandToken}
            };

            //Support should be copied in on PaymentFailureFinalPartner emails. so a copy will be sent to admin and redirect to support notifying them.
            if (ConstructAndSendMessage(accountToMail.EmailAddress, variables, messageType, "Final notification for failed payment", "PaymentFailureFinalPartner", name))
            {
                string message = String.Format("Unsuccessful debit from credit card for Account name : {0} , with email address {1} ", accountToMail.DisplayName, accountToMail.EmailAddress);
                return SendEnquiryToAdmin("Final notification for failed payment", message);
            }
            return false;
        }

        public static bool SendFormFillCreatedNotificationToClient(Account partnerAccount, Account clientAccount, MessageType messageType)
        {
            var variables = new Dictionary<string, string>
            {
                {"clientName", clientAccount.DisplayName},

            };

            return ConstructAndSendMessage(clientAccount.EmailAddress, variables, messageType, "Please update your financial details", "FormFillCreated", clientAccount.DisplayName);
        }

        public static bool SendFormFillClientCompletedNotificationToPartner(Account partnerAccount, Account clientAccount, MessageType messageType)
        {
            var variables = new Dictionary<string, string>
            {
                {"clientName", clientAccount.DisplayName},

            };
            var subject = string.Format("Action Required: Please Review Fact Find Submission - {0}",
                clientAccount.DisplayName);

            return ConstructAndSendMessage(partnerAccount.EmailAddress, variables, messageType, subject, "FormFillClientCompleted", partnerAccount.DisplayName);
        }

        public static bool SendUpgradeRequestToAccountant(MessageType messageType, Account account, string emailAddress, Cobrand cobrand = null)
        {
            var param = cobrand != null ? string.Format("?cobrand={0}", cobrand.Token) : string.Empty;
            var variables = new Dictionary<string, string>
                                {
                                    {"Name", account.DisplayName},
                                    {"Email", account.EmailAddress},
                                    {"Cobrand", param},
                                };

            return ConstructAndSendMessage(emailAddress, variables, messageType, "Upgrade request", "UpgradeRequestAccountant", account.DisplayName);
        }

        public static bool SendUpgradedAccEmailToAccountant(MessageType messageType, Account account, string emailAddress, Cobrand cobrand = null)
        {
            var subject = string.Format("Confirm that {0} has paid for upgrade", account.DisplayName);
            var param = cobrand != null ? string.Format("?cobrand={0}", cobrand.Token) : string.Empty;
            var variables = new Dictionary<string, string>
                                {
                                    {"Name", account.DisplayName},
                                    {"Email", account.EmailAddress},
                                    {"Cobrand", param},
                                };

            return ConstructAndSendMessage(emailAddress, variables, messageType, subject, "UpgradedAccEmailToAccountant", account.DisplayName);
        }

        public static bool SendUpgradedAccEmailToAccountantWithoutPaymentUrl(MessageType messageType, Account account, string emailAddress, Cobrand cobrand = null)
        {
            var subject = string.Format("Your client {0} has upgraded to pro", account.DisplayName);
            var param = cobrand != null ? string.Format("?cobrand={0}", cobrand.Token) : string.Empty;
            var variables = new Dictionary<string, string>
            {
                {"Name", account.DisplayName},
                {"Email", account.EmailAddress},
                {"Cobrand", param},
            };

            return ConstructAndSendMessage(emailAddress, variables, messageType, subject, "UpgradeWithOutPaymentURLAccEmailToAccountant", account.DisplayName);
        }

        public static bool SendUpgradeRequestToUnlinkedAccountant(MessageType messageType, string accountName, string accountEmailAdd, TaxPartnerAgent accountant, Cobrand cobrand)
        {
            var param = cobrand != null ? string.Format("?cobrand={0}", cobrand.Token) : string.Empty;
            var variables = new Dictionary<string, string>
                                {
                                    {"AccountantName", accountant.Account.DisplayName},
                                    {"Name", accountName},
                                    {"Email", accountEmailAdd},
                                    {"Cobrand", param},
                                };

            return ConstructAndSendMessage(accountant.Account.EmailAddress, variables, messageType, "Upgrade request", "UpgradeRequestToUnlinkedAccountant", accountName);
        }

        public static bool SendUpgradeRequestToNotRegisteredAccountant(MessageType messageType, string accountName, string accountEmailAdd, string accEmailAddress, string accBusinessName, string accName)
        {
            var variables = new Dictionary<string, string>
                                {
                                    {"AccountantName", accName},
                                    {"BusinessName", accBusinessName},
                                    {"Name", accountName},
                                    {"Email", accountEmailAdd},
                                };

            return ConstructAndSendMessage(accEmailAddress, variables, messageType, "Upgrade request", "UpgradeRequestToNotRegisteredAccountant", accountName);
        }

        public static bool SendEnquiryToUser(MessageType messageType, string to, string name, string subject, string company,
                                             string phone, string message, string title = "", string category = "")
        {
            subject = string.IsNullOrEmpty(subject)
                        ? "Enquiry confirmation"
                        : "Enquiry confirmation: " + subject;

            var variables = new Dictionary<string, string>
                                {
                                    {"Title", title},
                                    {"Name", name},
                                    {"Company",company},
                                    {"Email", to},
                                    {"Phone",phone},
                                    {"Category",category},
                                    {"Message",message},
                                };

            return ConstructAndSendMessage(to, variables, messageType, subject, "EnquirySentToUser", name);
        }

        public static bool SendGroupMemberNewAccount(MessageType messageType, string to, string url, string parentDisplayName, string parentFirstName, string guestFirstName, string salutation)
        {
            url = EnsureEmailUrlHasTags(url, to, "GroupMemberNewAccount");
            var variables = new Dictionary<string, string>
                                {
                                    {"GuestFirstName", guestFirstName},
                                    {"OwnerFullName", parentDisplayName},
                                    {"OwnerFirstName", parentFirstName},
                                    {"url", url}
                                };

            return ConstructAndSendMessage(to, variables, messageType, "Please join " + parentDisplayName + "’s wealth portal", "GroupMemberNewAccount", salutation);
        }

        public static bool SendGroupMemberExistingAccount(MessageType messageType, string to, string url, string parentDisplayName, string parentFirstName, string guestFirstName, string salutation)
        {
            url = EnsureEmailUrlHasTags(url, to, "GroupMemberExistingAccount");
            var variables = new Dictionary<string, string>
                                {
                                    {"GuestFirstName", salutation},
                                    {"OwnerFullName", parentDisplayName},
                                    {"OwnerFirstName", parentDisplayName},
                                    {"url", url}
                                };

            return ConstructAndSendMessage(to, variables, messageType, "Please join " + parentDisplayName + "’s wealth portal", "GroupMemberExistingAccount", salutation);
        }

        public static bool SendDeleteAccountNotification(MessageType messageType, string to, string userName, DateTime deactivatedDate)
        {
            var appSettingValue = ConfigurationManager.AppSettings["deactivateAccountNumberOfDays"];
            var numberOfDays = (appSettingValue != null) ? Convert.ToInt32(appSettingValue) : 10;
            var variables = new Dictionary<string, string>
                                {
                                    {"type", "DeactivatedAccount"},
                                    {"numberOfDays", numberOfDays.ToString()},
                                    {"ExpiryDate", deactivatedDate.AddDays(numberOfDays).ToString("d MMM yyyy")},
                                };

            return ConstructAndSendMessage(to, variables, messageType, "Deactivated Account", "DeactivatedAccount", userName);
        }

        public static bool SendIssueToJira(JiraIssue jiraIssue)
        {
            var variables = new Dictionary<string, string>
                                {
                                    {"description", jiraIssue.Description},
                                    {"subject", FormatHelper.Ellipsify(jiraIssue.Summary,33)}
                                };

            var jiraEmailAddress = string.IsNullOrEmpty(ConfigurationManager.AppSettings["JiraIssueCreateEmail"])
                                       ? "jira@myprosperity.atlassian.net"
                                       : ConfigurationManager.AppSettings["JiraIssueCreateEmail"];

            //var parameters = new ApplicationNotificationParameters(variables, jiraEmailAddress);
            //var msg = strategy.NewMessage(jiraIssue.Summary, jiraEmailAddress, "jiraTaskFromEmail", parameters.ParameterDictionary);
            //return strategy.Send(msg);
            return ConstructAndSendMessage(jiraEmailAddress, variables, MessageType.Email, jiraIssue.Summary, "jiraTaskFromEmail", string.Empty);
        }

        public static bool SendKidsAppNotification(MessageType messageType, Account parentAccount, Account childAccount, Entity childGroupMemberEntity,
        string parentSignInurl, string setPasswordUrl, string iOSAppLink, string androidAppLink)
        {
            var parentSignInUrl = EnsureEmailUrlHasTags(parentSignInurl, parentAccount.EmailAddress, "KidsAppNotification");
            var activateUrl = EnsureEmailUrlHasTags(setPasswordUrl, childAccount.EmailAddress, "ActivateAccount");
            var parentFirstName = parentAccount.GetEntity().FirstName;
            var childFirstName = childGroupMemberEntity.FirstName.IsNullOrEmpty() ? childAccount.DisplayName : childGroupMemberEntity.FirstName;

            var variables = new Dictionary<string, string>
            {
                {"type", "EnableKidsAppForChild"},
                {"childFirstName", childFirstName },
                {"parentFirstName",  parentFirstName.IsNullOrEmpty() ? parentAccount.DisplayName : parentFirstName},
                {"appstore-kidsapplink",iOSAppLink},
                {"googleplay-kidsapplink", androidAppLink},
                {"activationUrl", activateUrl},
                {"cobrandName", parentAccount.CobrandToUse.DisplayName},
                {"cobrandtoken", parentAccount.CobrandToUse.Token}
            };
            var result = ConstructAndSendMessage(childAccount.EmailAddress, variables, messageType,
                "Set up your Kwidz account", "EnableKidsAppForChild", parentAccount.EmailAddress);

            if (result)
            {
                variables = new Dictionary<string, string>
                {
                    {"type", "EnableKidsAppForParent"},
                    {"childFirstName",childFirstName},
                    {"parentFirstName", parentFirstName.IsNullOrEmpty() ? parentAccount.DisplayName : parentFirstName},
                    {"appstore-kidsapplink",iOSAppLink},
                    {"googleplay-kidsapplink", androidAppLink},
                    {"signInUrl", parentSignInUrl},
                    {"cobrandName", parentAccount.CobrandToUse.DisplayName}
                };
            }

            result = ConstructAndSendMessage(parentAccount.EmailAddress, variables, messageType,
                "Kwidz has been enabled!", "EnableKidsAppForParent", parentAccount.EmailAddress);

            return result;
        }

        public static bool SendToDoNotification(MessageType messageType, string to, string userName, string url, string creatorName, string planToDoName, string planToDoDesc, string planToDoStatus, DateTime planToDoDue, string actionToView)
        {
            if (DateTime.Now > Convert.ToDateTime(planToDoDue))
                planToDoStatus = "Overdue";
            var signInUrl = EnsureEmailUrlHasTags(url, to, "TodoNotification");

            var variables = new Dictionary<string, string>
                                {
                                    {"type", "SendToDoNotification"},
                                    {"creatorName", creatorName},
                                    {"planToDoName", planToDoName},
                                    {"planToDoDesc", planToDoDesc},
                                    {"planToDoStatus", planToDoStatus},
                                    {"planToDoDue", planToDoDue.ToShortDateString()},
                                    {"signInUrl", signInUrl},
                                    {"actionToView", actionToView}
                                };

            return ConstructAndSendMessage(to, variables, messageType, "To-do", "SendToDoNotification", userName);
        }

        public static bool SendToDoNotificationBulk(MessageType messageType, Account ownerAcc, Account creatorAcc, List<string> toDoNames, string url)
        {

            var signInUrl = EnsureEmailUrlHasTags(url, ownerAcc.EmailAddress, "TodoNotificationBulk");
            var message = ownerAcc.ID == creatorAcc.ID
                ? string.Format("You have the following {0}", toDoNames.Count < 2 ? "to-do" : "to-dos")
                : string.Format("{0} has assigned {1} to you", creatorAcc.DisplayName,
                    toDoNames.Count < 2 ? "a to-do" : "to-dos");
            var todos = toDoNames.Aggregate(string.Empty,
                (current, toDoName) => current + string.Format("To-do name:{0}<br />",
                                           WebUtility.HtmlEncode(toDoName)));

            var variables = new Dictionary<string, string>
            {
                {"RawHTML_planToDo", todos},
                {"ToDoText", message},
                {"signInUrl", signInUrl},

            };

            return ConstructAndSendMessage(ownerAcc.EmailAddress, variables, messageType, toDoNames.Count < 2 ? "To-do" : "To-dos", "ToDoReminderBulk", ownerAcc.DisplayName);
        }


        public static bool FollowUpEmailDay1(MessageType messageType, string to, string userName)
        {
            var variables = new Dictionary<string, string>
                                {
                                    {"type", "FollowUpEmailDay1"},
                                };

            return ConstructAndSendMessage(to, variables, messageType, "Welcome", "FollowUpEmailDay1", userName);
        }

        public static bool SendScoreEmail(Account account, UserAction action, string templateName)
        {
            return ConstructAndSendMessage(account.EmailAddress, new Dictionary<string, string>(),
                MessageType.Email, action.Action.EmailSubject, templateName, GetUserDisplayName(account));
        }

        //        public static bool SendTaxReturnInvitation(MessageType messageType, Account accountantAccount, Account clientAccount, string clientTaxUrl)
        //        {
        //            var accountantName = GetUserDisplayName(accountantAccount);
        //            var clientName = GetUserDisplayName(clientAccount);
        //
        //            var variables = new Dictionary<string, string>
        //            {
        //                {"accountantName", accountantName},
        //                {"clientTaxUrl", clientTaxUrl},
        //                {"clientName", clientName},
        //                {"cobrandToken", accountantAccount.CobrandToUse.Token},
        //            };
        //
        //            var subject = ConfigHelper.TryGetOrDefault("SendReminderTaxReturnReviewRequestSubject", accountantAccount.DisplayName + " has invited you to start a tax return.");
        //
        //
        //            return ConstructAndSendMessage(clientAccount.EmailAddress, variables, messageType, subject, "SendReminderTaxReturnReviewRequest", accountantName);
        //        }

        public static bool SendTaxReturnInviteToClient(Account accountantAccount, Account clientAccount, string clientTaxUrl,
            bool sendViaNotificationQueueAsync = true)
        {
            var entity = clientAccount.GetEntity();

            var accountantName = GetUserDisplayName(accountantAccount);
            var clientName = GetUserDisplayName(clientAccount);
            var clientEntity = clientAccount.GetEntity();
            var clientFirstName = clientEntity != null && !string.IsNullOrEmpty(clientEntity.FirstName) ? clientEntity.FirstName : clientName;

            var subject = ConfigHelper.TryGetOrDefault("SendTaxReturnInviteSubject", "Please update your financial details so we can submit your tax return");

            var variables = new Dictionary<string, string>
            {
                {"accountantName", accountantName},
                {"clientTaxUrl", clientTaxUrl},
                {"clientName", clientName},
                {"clientFirstName", clientFirstName},
                {"cobrandToken", accountantAccount.CobrandToUse.Token},
                {"cobrandName", accountantAccount.CobrandToUse.DisplayName},
            };

            var success = ConstructAndSendMessage(clientAccount.EmailAddress, variables, MessageType.Email,
                subject, "SendTaxReturnInvite", entity.DisplayName, sendViaNotificationQueueAsync);

            if (!success)
                ExceptionHelper.Log(new Exception("Problem trying to send Tax Assistant invitation email for accountId: " + clientAccount.ID));

            return success;
        }

        public static bool SendTaxReturnReviewRequest(MessageType messageType, Account accountantAccount, Account clientAccount, string agentTaxUrl)
        {
            var accountantFirstName = GetUserFirstNameOrDisplayName(accountantAccount);
            var accountantName = GetUserDisplayName(accountantAccount);
            var clientName = GetUserDisplayName(clientAccount);

            var variables = new Dictionary<string, string>
            {
                {"accountantFirstName", accountantFirstName},
                {"accountantName", accountantName},
                {"agentTaxUrl", agentTaxUrl},
                {"clientName", clientName},
                {"cobrandToken", accountantAccount.CobrandToUse.Token},
            };

            var subject = ConfigHelper.TryGetOrDefault("SendTaxReturnReviewRequestSubject", string.Format("Tax information submitted by {0}", clientName));

            return ConstructAndSendMessage(accountantAccount.EmailAddress, variables, messageType, subject, "SendTaxReturnReviewRequest", accountantName);
        }

        //Todo(Sara) : Email's text should be updated ASAP (Alice will provide the copy)
        public static bool SendReminderTaxReturnReviewRequest(MessageType messageType, Account accountantAccount, Account clientAccount, string agentTaxUrl)
        {
            var accountantName = GetUserDisplayName(accountantAccount);
            var accountantFirstName = accountantAccount.GetEntity().FirstName;
            
            var clientName = GetUserDisplayName(clientAccount);

            var variables = new Dictionary<string, string>
            {
                {"accountantName", accountantName},
                {"accountantFirstName", !accountantFirstName.IsNullOrEmpty() ? accountantFirstName : accountantName},
                {"agentTaxUrl", agentTaxUrl},
                {"clientName", clientName},
                {"cobrandToken", accountantAccount.CobrandToUse.Token},
            };

            var subject = ConfigHelper.TryGetOrDefault("SendReminderTaxReturnReviewRequestSubject", "A client has added more details to their Tax Assistant");

            return ConstructAndSendMessage(accountantAccount.EmailAddress, variables, messageType, subject, "SendReminderTaxReturnReviewRequest", accountantName);
        }

        public static bool SendDocumentSigningRequest(MessageType messageType, Account signerAccount, string partnerName, string signatureUrl,
            string docName, string signername)
        {
            signername = signername.IsNullOrEmpty()
                ? signername = GetUserDisplayName(signerAccount)
                : signername.ToTitleCase();

            var variables = new Dictionary<string, string>
                                {
                                    {"partnerName", partnerName},
                                    {RAW_HTML_EMAIL_MARKUP + "signatureUrl", signatureUrl},
                                    {"docName", docName},
                                };

            var subject = ConfigHelper.TryGetOrDefault("DocumentSigningRequestSubject", "Please sign document");

            return ConstructAndSendMessage(signerAccount.EmailAddress, variables, messageType, subject, "DocumentSigningRequest", signername);
        }

        public static bool SendReminderDocumentSigningRequest(MessageType messageType, Account signerAccount, string partnerName, string signatureUrl,
            string docName, string signername)
        {
            signername = signername.IsNullOrEmpty()
                ? signername = GetUserDisplayName(signerAccount)
                : signername.ToTitleCase();

            var variables = new Dictionary<string, string>
                                {
                                    {"partnerName", partnerName},
                                    {RAW_HTML_EMAIL_MARKUP + "signatureUrl", signatureUrl},
                                    {"docName", docName},
                                };

            var subject = ConfigHelper.TryGetOrDefault("DocumentSigningRequestReminderSubject", "Please sign document");

            return ConstructAndSendMessage(signerAccount.EmailAddress, variables, messageType, subject, "ReminderDocumentSigningRequest", signername);
        }

        public static bool WaysToSaveCompletion(MessageType messageType, string to, string userName, ICollection<LeadProvider> applicationDescription)
        {
            var strategy = GetStrategy(messageType);
            const string subject = "Finding the best options for you!";
            var variables = new Dictionary<string, string>
                                {
                                    {"type", "WaysToSaveCompletion"},
                                    {"saveTypes", "saveTypesOverwrite"},
                                };
            string content = string.Empty;
            if (applicationDescription.Count != 0)
                content = applicationDescription.Aggregate(content, (current, ad) => current + ("&bull; " + EnumHelpers.GetDescription(ad) + " <br />"));

            var msg = ConstructMessage(to, variables, strategy, subject, "WaysToSaveCompletion", userName);

            msg.HtmlMessage = msg.HtmlMessage.Replace("saveTypesOverwrite", content);
            return strategy.Send(msg);
        }

        public static bool FuelCardReferral(MessageType messageType, Account account, Referral referral)
        {
            var userName = GetUserDisplayName(account);
            var subject = String.Format("{0} wants to share a United petrol card with you", userName);

            var variables = new Dictionary<string, string>
                {
                    {"type", "FuelCardReferral"},
                    {"recipientName", referral.RecipientName},
                    {"saveTypes", "saveTypesOverwrite"},
                    {"senderEmail", account.EmailAddress},
                };

            return ConstructAndSendMessage(referral.RecipientEmail, variables, messageType, subject, "FuelCardReferral", userName);
        }

        public static bool ManagementRequestNotifyAgent(string customerName, string customerEmail,
                                                        string propertyAddress, string agentEmail, string agentName)
        {
            var variables = new Dictionary<string, string>
                {
                    {"agentName", agentName},
                    {"customerName", customerName},
                    {"propertyAddress", propertyAddress},
                };
            //var parameters = new ApplicationNotificationParameters(variables, agentEmail);
            //var msg = strategy.NewMessage(
            //        string.Format("{0} would like their statements in their account", customerName),
            //        agentEmail,
            //        "PropertyManagementRequest", parameters.ParameterDictionary);
            //return strategy.Send(msg);
            return ConstructAndSendMessage(agentEmail, variables, MessageType.Email,
                string.Format("{0} would like their statements in their account", customerName), "PropertyManagementRequest", customerName);
        }

        public static bool AgentNotification(ValuationRequest valuationRequest, Agent agent, string encryptedToken, string formattedAddress)
        {
            string templateName = string.Empty;
            var property = valuationRequest.WealthItem;
            var owner = valuationRequest.Requester;
            var agentLink = "realestate/authenticate?token=" + encryptedToken;
            string subject = "Property valuation request";

            var variables = new Dictionary<string, string>();

            if (valuationRequest.ValuationRequestType == ValuationRequestType.Detailed)
            {
                if (valuationRequest.Intention == "Sale")
                {
                    templateName = "AgentNotificationSale";
                    subject = "Request to sell property";
                }
                else
                {
                    templateName = "AgentNotificationDetailed";
                    subject = "Personal property appraisal request";
                }
            }
            else
            {
                templateName = "AgentNotificationQuick";
                subject = "Online property appraisal request";
            }

            agentLink = EnsureEmailUrlHasTags(agentLink, agent.Email, templateName);

            if (!string.IsNullOrEmpty(formattedAddress))
                subject = subject + ": " + formattedAddress;

            variables.Add("AgentName", agent.Name);
            variables.Add("ValuationType", valuationRequest.ValuationRequestType.ToString().ToLower());
            variables.Add("OwnerName", GetUserDisplayName(owner));
            variables.Add("Suburb", property.AddressSuburb);
            variables.Add("AgentLink", agentLink);

            variables.Add("ValuationName", valuationRequest.ContactName);
            variables.Add("ValuationPhone", valuationRequest.ContactPhone);
            variables.Add("ValuationEmail", valuationRequest.ContactEmail);
            variables.Add("ValuationAddress", formattedAddress);

            return ConstructAndSendMessage(agent.Email, variables, MessageType.Email, subject, templateName, agent.Name);
        }


        public static bool SendEmailForBankLoginFailure(MessageType messageType, Account account, string bankName)
        {
            var subject = "Bank account balances have not updated";
            var variables = new Dictionary<string, string>
                {
                                    {"type", "FinancialAccountAdded"},
                                    {"bankName", bankName},
                };

            return ConstructAndSendMessage(account.EmailAddress, variables, messageType, subject, "BankLoginFailure", GetUserDisplayName(account));
        }

        public static bool SendEmailForFinancialAccountAdded(MessageType messageType, Account account, IEnumerable<AccountBase> accountAdded, string bankName)
        {
            var acctAdded = accountAdded.ToList();
            var strategy = GetStrategy(messageType);
            var message = "Your " + bankName + " " + (acctAdded.Count > 1 ? "accounts have" : "account has") + " been successfully linked.";
            var subject = (acctAdded.Count > 1 ? "Accounts" : "Account") + " successfully added";
            var variables = new Dictionary<string, string>
                {{"type", "FinancialAccountAdded"},
                                    {"bankName", bankName},
                                    {"message",message},
                                    {"details","###details###"},
                };

            var msg = ConstructMessage(account.EmailAddress, variables, strategy, subject, "FinancialAccountAdded", GetUserDisplayName(account));

            var accountList = string.Empty;
            foreach (var accountBase in accountAdded)
            {
                accountList += "Account name: " + accountBase.AccountName + "</br>";
            }
            msg.HtmlMessage = msg.HtmlMessage.Replace("###details###", accountList);

            return strategy.Send(msg);
        }

        public static bool SendEmailForFinancialAccountsUpdated(MessageType messageType, Account account, int numberFinAcctUpdated)
        {
            var message = numberFinAcctUpdated + " financial account " + (numberFinAcctUpdated > 1 ? "balances have" : "balance has") + " been successfully updated.";
            var subject = "Account " + (numberFinAcctUpdated > 1 ? "balances" : "balance") + " successfully updated";
            var variables = new Dictionary<string, string>
                {
                                    {"type", "FinancialAccountUpdated"},
                                    {"message",message},
                };

            return ConstructAndSendMessage(account.EmailAddress, variables, messageType, subject, "FinancialAccountUpdated", GetUserDisplayName(account));
        }

        internal static void NotifyPropManRentStatementReceived(
            string recipientEmail,
            string recipientName,
            string propertyManagerName,
            string agencyName,
            string agencyLogoUrl,
            string propertyUrl,
            string streetAddress,
            string suburbStateAndPostCode,
            string siteUrl,
            decimal amount,
            DateTime date,
            bool isTransactionMatched, EnumCulture? enumCulture
            )
        {
            var templateName = isTransactionMatched ? "RentReceivedMatched" : "RentReceivedNotMatched";
            const string SUBJECT = "Your statement is ready";

            var propertyViewUrl = string.Format("{0}Accounts/SignIn/SignIn?emailSetting={1}", siteUrl, EmailParam.mywealth);
            propertyViewUrl = EnsureUrlHasCobrandToken(propertyViewUrl, recipientEmail);

            var currencySymbolPattern = CultureHelper.GetResxString(enumCulture, "SymbolCurrency") + "#,##0.00";

            var variables = new Dictionary<string, string>
                {
                    {"recipientEmail", recipientEmail},
                    {"recipientName", recipientName},
                    {"matched", isTransactionMatched.ToString()},
                    {"propertyManagerName", propertyManagerName},
                    {"amount", amount.ToString(currencySymbolPattern)},
                    {"date", date.ToString("dd/MM/yyyy")},
                    {"agencyName", agencyName},
                    {"agencyLogoUrl", agencyLogoUrl},
                    {"propertyUrl", propertyUrl},
                    {"streetAddress", streetAddress},
                    {"suburbStateAndPostCode", suburbStateAndPostCode},
                    {"propertyViewUrl", propertyViewUrl},
                    {"siteUrl", siteUrl}
                };
            //var parameters = new ApplicationNotificationParameters(variables, recipientEmail);
            //var msg = strategy.NewMessage(SUBJECT, recipientEmail, templateName, parameters.ParameterDictionary);
            //strategy.Send(msg);
            ConstructAndSendMessage(recipientEmail, variables, MessageType.Email, SUBJECT, templateName, recipientName);
        }


        internal static void PropManAcceptedNotification(string recipientEmail, string recipientName,
            string propertyManagerName, string propertyManagerImageUrl, string agencyName, string agencyLogoUrl,
            string propertyUrl, string streetAddress, string suburbStateAndPostCode, string siteUrl)
        {
            const string TEMPLATE_NAME = "PropManAccepted";
            const string SUBJECT = "Property manager added";

            var variables = new Dictionary<string, string>
                {
                    {"recipientEmail", recipientEmail},
                    {"recipientName", recipientName},
                    {"propertyManagerName", propertyManagerName},
                    {"propertyManagerImageUrl", propertyManagerImageUrl},
                    {"agencyName", agencyName},
                    {"agencyLogoUrl", agencyLogoUrl},
                    {"propertyUrl", propertyUrl},
                    {"streetAddress", streetAddress},
                    {"suburbStateAndPostCode", suburbStateAndPostCode},
                    {"propertyViewUrl", siteUrl},
                    {"siteUrl", siteUrl}
                };

            ConstructAndSendMessage(recipientEmail, variables, MessageType.Email, SUBJECT, TEMPLATE_NAME, recipientName);
        }

        public static bool SendReport(Account account, Stream report, string templateName, string fileName, string subject)
        {
            var strategy = GetStrategy(MessageType.Email);

            var msg = ConstructMessage(account.EmailAddress, new Dictionary<string, string>(), strategy, subject, templateName, GetUserDisplayName(account));

            if (report != null)
            {
                var raw = new RawAttachment()
                {
                    Stream = report,
                    //MediaType = MediaTypeNames.Application.Pdf,
                    Name = fileName
                };
                msg.RawAttachments.Add(raw);
            }
            return strategy.Send(msg);
        }

        public static void ValuationCompleteNotification(ValuationRequest valuationRequest, string file, string baseUrl,
            bool sendtoAgent = false, Stream appraisalReport = null, List<HttpPostedFileBase> documents = null)
        {
            var emailAddress = sendtoAgent ? valuationRequest.Requester.EmailAddress : "support@myprosperity.com.au";
            bool isStarter = false;
            string templateName = string.Empty;
            var strategy = GetStrategy(MessageType.Email);
            var property = valuationRequest.WealthItem;
            var owner = valuationRequest.Requester;

            isStarter = (owner.GetActivePlanID().ToString(CultureInfo.InvariantCulture) == ConfigurationManager.AppSettings["StarterPlanId"]);

            string subject = "Here's your property appraisal for " + property.Name;

            string UpdateValuationLink = string.Format("{0}?confirmagentvaluation={1}",
                                                       baseUrl, valuationRequest.Id);
            string agentImage = ImageSection.CurrentProvider.GetImageURL(valuationRequest.AgentAssignee.AgencyBranch.Agency.PrimaryImageToken, "163x121");
            string homeImage = ImageSection.CurrentProvider.GetImageURL(property.ImageToken, "163x121");

            agentImage = string.Format("{0}/Image/Image?Id={1}", baseUrl, valuationRequest.AgentAssignee.AgencyBranch.Agency.PrimaryImageToken);
            homeImage = string.Format("{0}/Image/Image?Id={1}", baseUrl, string.IsNullOrEmpty(valuationRequest.ImageToken)
                ? property.ImageToken : valuationRequest.ImageToken);

            templateName = isStarter ? "ValuationCompleteNotificationStarter" : "ValuationCompleteNotification";

            UpdateValuationLink = EnsureEmailUrlHasTags(UpdateValuationLink, emailAddress, templateName);

            var variables = new Dictionary<string, string>
                {
                    {"ValuationType", valuationRequest.ValuationRequestType.ToString().ToLower()},
                    {"Suburb", property.AddressSuburb},
                    {"UpdateValuationLink", UpdateValuationLink},
                    {"AgentName",valuationRequest.AgentAssignee.Name},
                    {"Agency", valuationRequest.AgentAssignee.AgencyBranch.Agency.Name},
                    {"AgencyLogoUrl", agentImage},
                    {"PropertyImageUrl",homeImage},
                    {"PropertyAddressLine1", property.AddressStreet1},
                    {"PropertyAddressLine2", property.AddressSuburb + " " + property.AddressState + " " + property.AddressPostcode}
                };

            var msg = ConstructMessage(emailAddress, variables, strategy, subject, templateName, GetUserDisplayName(owner));

            if (appraisalReport == null && !string.IsNullOrEmpty(file))
            {
                using (var stream = new FileStream(file, FileMode.Open))
                {
                    var raw = new RawAttachment()
                    {
                        Stream = stream,
                        MediaType = MediaTypeNames.Application.Pdf,
                        Name = valuationRequest.WealthItem.Name + ".pdf"
                    };

                    msg.RawAttachments.Add(raw);
                }
            }

            if (appraisalReport != null)
            {
                var raw = new RawAttachment()
                {
                    Stream = appraisalReport,
                    //MediaType = MediaTypeNames.Application.Pdf,
                    Name = valuationRequest.WealthItem.Name + ".pdf"
                };
                msg.RawAttachments.Add(raw);
            }

            if (documents != null)
            {
                foreach (var raw in documents.Select(document => new RawAttachment
                {
                    Stream = document.InputStream,
                    //MediaType = document.ContentType,
                    Name = document.FileName
                }))
                {
                    msg.RawAttachments.Add(raw);
                }
            }

            strategy.Send(msg);
        }

        public static bool AccountantJoinComplete(string emailAddress, string file, string name)
        {
            var strategy = GetStrategy(MessageType.Email);

            var msg = ConstructMessage(emailAddress, null, strategy, "Welcome Aboard", "AccountantJoinChecklist", name);

            msg.To.Add("support@myprosperity.com.au");
            if (!string.IsNullOrEmpty(file))
            {
                Attachment data = new Attachment(file, MediaTypeNames.Application.Octet);
                msg.Attachments.Add(data);
            }

            return strategy.Send(msg);
        }

        public static bool AccountantJoinWizard(string emailAddress, string name, string getStartedUrl)
        {
            var strategy = GetStrategy(MessageType.Email);

            var variables = new Dictionary<string, string>
            {
                {"GetStartedUrl", getStartedUrl},
            };

            var msg = ConstructMessage(emailAddress, variables, strategy, "Welcome Aboard", "AccountantJoinWizard", name);

            msg.To.Add("support@myprosperity.com.au");

            return strategy.Send(msg);
        }

        public static bool ProPlanAddonPurchase(string emailAddress, string name, string addonName)
        {
            var variables = new Dictionary<string, string>
            {
                {"AddonName", addonName},
            };

            var strategy = GetStrategy(MessageType.Email);
            var msg = ConstructMessage(emailAddress, variables, strategy, "Pro Pack Purchase Confirmed", "PlanAddonPurchase", name);
            msg.To.Add("support@myprosperity.com.au");
            return strategy.Send(msg);
        }

        public static bool AccountantPlanUpgradeToDocSigning(string emailAddress, string name)
        {
            var strategy = GetStrategy(MessageType.Email);
            var msg = ConstructMessage(emailAddress, null, strategy, "Plan Upgrade Confirmed", "AccountantPlanUpgradeToDocSigning", name);
            msg.To.Add("support@myprosperity.com.au");
            return strategy.Send(msg);
        }

        public static bool DocSigningCompletedNotification(Account ownerAccount, Account senderAccount,
            DocumentGroup documentGroup, string signername, string signerEmail, string filePath = "", string fileName = "")
        {
            var result = true;
            Document doc = null;

            // Get Doc, filepath and filename from documentGroup
            if (documentGroup != null)
            {
                doc = documentGroup.GetPrimaryOrFirstDocument();

                if (doc != null && doc.File != null)
                {
                    if (filePath.IsNullOrEmpty())
                        filePath = doc.File.FileRef;
                    if (fileName.IsNullOrEmpty())
                        fileName = doc.File.FileName;
                }
            }

            // ensure file exists on local server
            if (!filePath.IsNullOrEmpty())
            {
                var localPath = Path.Combine(UploadEnvConfiguration.Instance.FileSaveDirPath, filePath);
                if (!System.IO.File.Exists(localPath))
                {
                    if (doc != null)
                    {
                        var docDownloadInfo = DocumentService.DownloadFileToLocalServer(doc);
                        if (docDownloadInfo == null)
                            throw new FileNotFoundException(string.Format("Can't send DocSigningCompletedNotification - Could not download file from documentGroupID{0} with filePath:{1} for recipient AccountId:{2} and senderAccountId:{3}",
                                documentGroup.ID, filePath, ownerAccount.ID, senderAccount.ID));
                        else
                        {
                            filePath = docDownloadInfo.PhysicalPath;
                        }
                    }
                    else
                    {
                        throw new FileNotFoundException(string.Format("Can't send DocSigningCompletedNotification - Could not find fileName:{0} with filePath:{1} for recipient AccountId:{2} and senderAccountId:{3}",
                                fileName, filePath, ownerAccount.ID, senderAccount.ID));
                    }
                }
            }

            var ok = DocSigningCompletePartnerAgent(senderAccount, ownerAccount, documentGroup, signername, filePath, fileName);

            if (!ok)
            {
                result = false;
                ExceptionHelper.Log(new Exception(string.Format("Unable to send email notification to partner accountId:{0} for fileName{1}", ownerAccount.ID, fileName)));
            }

            ok = DocSigningCompleteAccountOwner(ownerAccount, signername, signerEmail, filePath, fileName);
            if (!ok)
            {
                result = false;
                ExceptionHelper.Log(new Exception(string.Format("Unable to send email notification to client accountId:{0} for fileName{1}", ownerAccount.ID, fileName)));

            }
            return result;
        }

        private static bool DocSigningCompletePartnerAgent(Account senderAccount, Account ownerAccount,
            DocumentGroup documentGroup, string signername, string filePath, string fileName)
        {
            signername = signername.IsNullOrEmpty()
                ? ownerAccount.DisplayName
                : signername.ToTitleCase();

            var strategy = GetStrategy(MessageType.Email);
            var subject = string.Format("The document {0} has been signed for {1}", fileName, signername);
            var variables = new Dictionary<string, string>
                {
                    {"docname", fileName},
                    {"signername", signername},
                };

            //todo:remove depending on functionality requirement.
            var attachDocuments = ConfigHelper.TryGetOrDefault("SendDocSigningCompleteWithFileAttachments", false);
            var result = false;
            var attachmentFilePaths = new List<string>();

            if (!string.IsNullOrEmpty(filePath))
                attachmentFilePaths.Add(filePath);

            var sendViaNotificationQueue = ConfigHelper.TryGetOrDefault("SendDocSigningCompleteViaNotificationQueue", true);

            if (attachDocuments)
            {
                result = ConstructAndSendMessage(senderAccount.EmailAddress, variables, strategy, subject, "DocSignningCompletePartnerAgent",
                    senderAccount.DisplayName, sendViaNotificationQueue, documentGroup, attachmentFilePaths);

                if (result && ConfigHelper.TryGetOrDefault("SendCopyOfSignedDocToInfo", true))
                    ConstructAndSendMessage("info@myprosperity.com.au", variables, strategy, subject, "DocSignningCompletePartnerAgent",
                        senderAccount.DisplayName, sendViaNotificationQueue, documentGroup, attachmentFilePaths);
            }
            else
            {
                result = ConstructAndSendMessage(senderAccount.EmailAddress, variables, strategy, subject, "DocSignningCompletePartnerAgent",
                    senderAccount.DisplayName, sendViaNotificationQueue);

                if (result && ConfigHelper.TryGetOrDefault("SendCopyOfSignedDocToInfo", true))
                    ConstructAndSendMessage("info@myprosperity.com.au", variables, strategy, subject, "DocSignningCompletePartnerAgent",
                        senderAccount.DisplayName, sendViaNotificationQueue);
            }

            return result;
        }

        private static bool DocSigningCompleteAccountOwner(Account account, string signername, string signerEmail, string filePath, string fileName)
        {
            signername = signername.IsNullOrEmpty()
                ? account.DisplayName
                : signername.ToTitleCase();

            if (signerEmail.IsNullOrEmpty())
                signerEmail = account.EmailAddress;

            var strategy = GetStrategy(MessageType.Email);
            var subject = string.Format("The document {0} has been signed", fileName);
            var variables = new Dictionary<string, string>
                {
                    {"docname", fileName},
                    {"signername", signername},
                };
            var sendViaNotificationQueue = ConfigHelper.TryGetOrDefault("SendDocSigningCompleteViaNotificationQueue", true);

            return ConstructAndSendMessage(signerEmail, variables, strategy, subject, "DocSignningCompleteAccountOwner",
                signername, sendViaNotificationQueue);
        }

        public static bool WillDocGenerationCompleteNotification(WillOtherProtectionItem will, Document document, Account ownerAccount)
        {
            if (will == null)
            {
                throw new ArgumentNullException("will");
            }

            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            if (ownerAccount == null)
            {
                throw new ArgumentNullException("ownerAccount");
            }

            var filePath = document.File.FileRef;

            // ensure file exists on local server
            if (!filePath.IsNullOrEmpty())
            {
                var localPath = Path.Combine(UploadEnvConfiguration.Instance.FileSaveDirPath, filePath);
                if (!System.IO.File.Exists(localPath))
                {
                    var docDownloadInfo = DocumentService.DownloadFileToLocalServer(document);
                    if (docDownloadInfo == null)
                        throw new FileNotFoundException(string.Format(
                            "Can't send WillDocGenerationNotification - Could not download file from document Id:{0} with filePath:{1} for willId:{2}",
                            document.ID, filePath, will.ID));
                    else
                    {
                        filePath = docDownloadInfo.PhysicalPath;
                    }
                }
            }
            else
            {
                throw new InvalidOperationException(string.Format(
                    "Can't send WillDocGenerationNotification - Could not get file path for document Id:{0} for willId:{1}",
                    document.ID, will.ID));
            }

            var willOwner = will.Owners.FirstOrDefault();
            if (willOwner == null)
            {
                throw new InvalidOperationException(string.Format("Will must have an owner. ID: {0}", will.ID));
            }

            var strategy = GetStrategy(MessageType.Email);
            var subject = string.Format("The will document for {0} has been generated", willOwner.Entity.DisplayName);
            var variables = new Dictionary<string, string>
            {
                {"willOwner", willOwner.Entity.DisplayName},
            };

            var result = false;
            var attachmentFilePaths = new List<string>();

            if (!string.IsNullOrEmpty(filePath))
                attachmentFilePaths.Add(filePath);


            result = ConstructAndSendMessage(ownerAccount.EmailAddress, variables, strategy, subject, "WillDocGenerationComplete",
                ownerAccount.DisplayName, synchronousAttachmentFilePaths: attachmentFilePaths);

            //if (result && ConfigHelper.TryGetOrDefault("SendCopyOfGeneratedWillDocToInfo", true))
            //    ConstructAndSendMessage("info@myprosperity.com.au", variables, strategy, subject, "WillDocGenerationComplete",
            //        ownerAccount.DisplayName, synchronousAttachmentFilePaths: attachmentFilePaths);

            if (!result)
            {
                ExceptionHelper.Log(new Exception(string.Format(
                    "Unable to send will doc generation email notification to client accountId:{0} for Document_ID: {1}", ownerAccount.ID,
                    document.ID)));
            }

            return result;
        }

        public static bool SendWillRequest(WillOtherProtectionItem will, Account ownerAccount)
        {
            if (will == null)
            {
                throw new ArgumentNullException("will");
            }

            if (ownerAccount == null)
            {
                throw new ArgumentNullException("ownerAccount");
            }

            var cobrand = ownerAccount.CobrandToUse;
            if (cobrand == null)
            {
                throw new InvalidOperationException(string.Format("No Cobrand found for account ID: {0}", ownerAccount.ID));
            }

            var willOwner = will.Owners.FirstOrDefault();
            if (willOwner == null)
            {
                throw new InvalidOperationException(string.Format("Will must have an owner. ID: {0}", will.ID));
            }

            if (will.PrimaryExecutorEntity == null)
            {
                throw new InvalidOperationException(string.Format("Will must have a primary executor. ID: {0}", will.ID));
            }

            if (will.SecondaryExecutorEntity == null)
            {
                throw new InvalidOperationException(string.Format("Will must have a secondary executor. ID: {0}", will.ID));
            }

            if (!will.IsNetEstateForParentsBeneficiaries && will.Beneficiaries.IsNullOrEmpty())
            {
                throw new InvalidOperationException(string.Format("Will must have beneficiaries. ID: {0}", will.ID));
            }

            Entity spouse = null;
            var accountOwnerEntity = ownerAccount.GetEntity();
            var willForAccountOwner = willOwner.Entity.ID == accountOwnerEntity.ID;
            if (willForAccountOwner)
            {
                var spouseBeneficiary = will.ActualBeneficiaries
                    .FirstOrDefault(b => b.Entity != null && b.Entity.Relationship.HasValue && b.Entity.Relationship.Value.IsSpouse());
                spouse = spouseBeneficiary == null ? null : spouseBeneficiary.Entity;
            }
            else if (willOwner.Entity.Relationship.HasValue && willOwner.Entity.Relationship.Value.IsSpouse())
            {
                var spouseBeneficiary = will.ActualBeneficiaries.FirstOrDefault(b => b.Entity != null && b.Entity.ID == accountOwnerEntity.ID);
                spouse = spouseBeneficiary == null ? null : spouseBeneficiary.Entity;
            }

            var strategy = GetStrategy(MessageType.Email);
            var subject = string.Format("A client from {0} has requested assistance with estate planning", cobrand.DisplayName);
            var variables = new Dictionary<string, string>
            {
                {"LawyerName", cobrand.CobrandSettings.WillSettings.LegalPartnerName},
                {"willOwnerName", willOwner.Entity.DisplayName},
                {"willOwnerEmail", willOwner.Entity.PreferredEmailAddress},
                {"willOwnerAddress", willOwner.Entity.AddressString},
                {"InitialExecutor", will.PrimaryExecutorEntity.DisplayName},
                {"BackupExecutor", will.SecondaryExecutorEntity.DisplayName},
                {"spouseName", spouse == null ? string.Empty : spouse.DisplayName},
                {"PrimaryGuardian", will.PrimaryGuardianEntity == null ? null : will.PrimaryGuardianEntity.DisplayName},
                {"BackupGuardian", will.SecondaryGuardianEntity == null ? null : will.SecondaryGuardianEntity.DisplayName},
                {
                    "Beneficiaries",
                    will.IsNetEstateForParentsBeneficiaries
                        ? "Parents"
                        : will.ActualBeneficiaries.Aggregate(string.Empty, (s, b) => s + "," + b.Entity.DisplayName).Trim(',')
                },
                {
                    "ExtraQuestions", will.QuestionAnswers.IsNullOrEmpty()
                        ? string.Empty
                        : will.QuestionAnswers.Aggregate(string.Empty,
                            (s, b) => s + "<strong>" + b.QuestionText + "</strong><br/>" + b.AnswerText + "<br/>")
                },
            };

            var result = false;

            result = ConstructAndSendMessage(cobrand.CobrandSettings.WillSettings.LegalPartnerEmail, variables, strategy, subject,
                "WillRequestToLawyer",
                ownerAccount.DisplayName);

            //if (result && ConfigHelper.TryGetOrDefault("SendCopyOfGeneratedWillDocToInfo", true))
            //    ConstructAndSendMessage("info@myprosperity.com.au", variables, strategy, subject,
            //        "WillRequestToLawyer",
            //        ownerAccount.DisplayName);

            if (!result)
            {
                ExceptionHelper.Log(new Exception(string.Format(
                    "Unable to send will request email to laywer. accountId:{0}", ownerAccount.ID)));
            }

            return result;
        }

        public static bool SendWillRequestNotificationToAccountant(WillOtherProtectionItem will, Account ownerAccount)
        {
            if (will == null)
            {
                throw new ArgumentNullException("will");
            }

            if (ownerAccount == null)
            {
                throw new ArgumentNullException("ownerAccount");
            }

            var defaultAccountant = ownerAccount.Group.Members.FirstOrDefault(gm => gm.Entity != null && gm.Entity.IsPrimary);
            if (defaultAccountant == null)
            {
                // skip if user doesn't have a default accountant
                return true;
            }

            var defaultAccountantEntity = defaultAccountant.Entity;
            if (string.IsNullOrEmpty(defaultAccountantEntity.PreferredEmailAddress))
            {
                return true;
            }

            var willOwner = will.Owners.FirstOrDefault();
            if (willOwner == null)
            {
                throw new InvalidOperationException(string.Format("Will must have an owner. ID: {0}", will.ID));
            }

            if (will.PrimaryExecutorEntity == null)
            {
                throw new InvalidOperationException(string.Format("Will must have a primary executor. ID: {0}", will.ID));
            }

            if (will.SecondaryExecutorEntity == null)
            {
                throw new InvalidOperationException(string.Format("Will must have a secondary executor. ID: {0}", will.ID));
            }

            if (!will.IsNetEstateForParentsBeneficiaries && will.Beneficiaries.IsNullOrEmpty())
            {
                throw new InvalidOperationException(string.Format("Will must have beneficiaries. ID: {0}", will.ID));
            }

            Entity spouse = null;
            var accountOwnerEntity = ownerAccount.GetEntity();
            var willForAccountOwner = willOwner.Entity.ID == accountOwnerEntity.ID;
            if (willForAccountOwner)
            {
                var spouseBeneficiary = will.ActualBeneficiaries
                    .FirstOrDefault(b => b.Entity != null && b.Entity.Relationship.HasValue && b.Entity.Relationship.Value.IsSpouse());
                spouse = spouseBeneficiary == null ? null : spouseBeneficiary.Entity;
            }
            else if (willOwner.Entity.Relationship.HasValue && willOwner.Entity.Relationship.Value.IsSpouse())
            {
                var spouseBeneficiary = will.ActualBeneficiaries.FirstOrDefault(b => b.Entity != null && b.Entity.ID == accountOwnerEntity.ID);
                spouse = spouseBeneficiary == null ? null : spouseBeneficiary.Entity;
            }

            var strategy = GetStrategy(MessageType.Email);
            var subject = string.Format("Will Request for {0} has been submitted", willOwner.Entity.DisplayName);
            var variables = new Dictionary<string, string>
            {
                {"AccountantName", defaultAccountantEntity.DisplayName},
                {"willOwnerName", willOwner.Entity.DisplayName},
                {"willOwnerEmail", willOwner.Entity.PreferredEmailAddress},
                {"willOwnerAddress", willOwner.Entity.AddressString},
                {"InitialExecutor", will.PrimaryExecutorEntity.DisplayName},
                {"BackupExecutor", will.SecondaryExecutorEntity.DisplayName},
                {"spouseName", spouse == null ? string.Empty : spouse.DisplayName},
                {"PrimaryGuardian", will.PrimaryGuardianEntity == null ? null : will.PrimaryGuardianEntity.DisplayName},
                {"BackupGuardian", will.SecondaryGuardianEntity == null ? null : will.SecondaryGuardianEntity.DisplayName},
                {
                    "Beneficiaries",
                    will.IsNetEstateForParentsBeneficiaries
                        ? "Parents"
                        : will.ActualBeneficiaries.Aggregate(string.Empty, (s, b) => s + "," + b.Entity.DisplayName).Trim(',')
                },
                {
                    "ExtraQuestions", will.QuestionAnswers.IsNullOrEmpty()
                        ? string.Empty
                        : will.QuestionAnswers.Aggregate(string.Empty,
                            (s, b) => s + "<strong>" + b.QuestionText + "</strong><br/>" + b.AnswerText + "<br/>")
                },
            };

            var result = false;

            result = ConstructAndSendMessage(defaultAccountantEntity.PreferredEmailAddress, variables, strategy, subject,
                "WillRequestNotificationToAccountant",
                ownerAccount.DisplayName);

            //if (result && ConfigHelper.TryGetOrDefault("SendCopyOfGeneratedWillDocToInfo", true))
            //    ConstructAndSendMessage("info@myprosperity.com.au", variables, strategy, subject,
            //        "WillRequestNotificationToAccountant",
            //        ownerAccount.DisplayName);

            if (!result)
            {
                ExceptionHelper.Log(new Exception(string.Format(
                    "Unable to send will request  nbgm n.notification email to accountant. accountId:{0}", ownerAccount.ID)));
            }

            return result;
        }

        public static bool PropManNewManagedPropertyNotification(string recipientEmail, string recipientName,
                                                                 string propertyManagerName,
                                                                 string propertyManagerImageUrl, string agencyName,
                                                                 string agencyLogoUrl, string propertyUrl,
                                                                 string streetAddress, string suburbStateAndPostCode,
                                                                 string siteUrl)
        {
            const string TEMPLATE_NAME = "NewManagedPropertyNotification";
            const string SUBJECT = "New property added to your account";

            var variables = new Dictionary<string, string>
                {
                    {"recipientEmail", recipientEmail},
                    {"recipientName", recipientName},
                    {"propertyManagerName", propertyManagerName},
                    {"propertyManagerImageUrl", propertyManagerImageUrl},
                    {"agencyName", agencyName},
                    {"agencyLogoUrl", agencyLogoUrl},
                    {"propertyUrl", propertyUrl},
                    {"streetAddress", streetAddress},
                    {"suburbStateAndPostCode", suburbStateAndPostCode},
                    {"propertyViewUrl", siteUrl},
                    {"siteUrl", siteUrl}
                };

            return ConstructAndSendMessage(recipientEmail, variables, MessageType.Email, SUBJECT, TEMPLATE_NAME, recipientName);
        }

        public static bool SendFinancialServicePreferences(Account account, List<InterestCategory> interestCategories, TaxPartnerAgent accountant, string templateName, string subject, string cobranToken)
        {
            var listOfServicesMarkup = interestCategories.Select(interestCategory => interestCategory.AlternateName ?? interestCategory.Name)
                    .Aggregate(string.Empty, (current, service) => current + string.Format("{0}<br />", WebUtility.HtmlEncode(service)));

            var variables = new Dictionary<string, string>
                {
                    {"client", GetUserDisplayName(account)},
                    {"clientEmail", account.EmailAddress},
                    {"date", DateTime.Now.ToShortDateString()},
                    {"cobrand", cobranToken},
                    {"RawHTML_Services", listOfServicesMarkup}
                };

            return ConstructAndSendMessage(accountant.Email, variables, MessageType.Email, subject, templateName, "Team");
        }

        public static bool SendFinancialServiceRequest(Account sender, string serviceName)
        {
            if (sender == null)
                throw new ArgumentNullException("sender");

            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentNullException("serviceName");

            var subject = String.Format("{0} has requested a new financial service", sender.DisplayName);
            var body = String.Format("{0} [{1}] has requested the following financial service be added: {2}",
                sender.DisplayName, sender.EmailAddress, serviceName);

            return SendEnquiryToAdmin(subject, body);
        }

        public static bool RequestNewDataFeed(Account sender, string feedName)
        {
            if (sender == null)
                throw new ArgumentNullException("sender");

            if (string.IsNullOrEmpty(feedName))
                throw new ArgumentNullException("feedName");

            var subject = String.Format("{0} has requested a new data feed", sender.DisplayName);
            var body = String.Format("{0} [{1}] has requested the following data feed be added: {2}",
                sender.DisplayName, sender.EmailAddress, feedName);

            return SendEnquiryToAdmin(subject, body);
        }


        public static bool SendRegistrationNotificationToAccountant(MessageType messageType, Account account, TaxPartnerAgent taxPartnerAgent)
        {
            var accountantEmail = taxPartnerAgent.Email;
            var entity = account.GetEntity();
            var message = string.IsNullOrEmpty(entity.ContactPhone)
                              ? string.Empty
                              : string.Format("You can contact them on {0}", entity.ContactPhone);
            var variables = new Dictionary<string, string>
                                {
                                    {"accountantname", taxPartnerAgent.Name},
                                    {"emailaddress", account.EmailAddress},
                                    {"message",message},
                                };

            return ConstructAndSendMessage(accountantEmail, variables, messageType,
                string.Format("{0} has registered an account", account.DisplayName), "NotifyRegistrationToAccountant", GetUserDisplayName(account));
        }

        public static bool SendActivationNotificationToAccountant(MessageType messageType, Account account, TaxPartnerAgent taxPartnerAgent)
        {
            var accountantEmail = taxPartnerAgent.Email;
            var variables = new Dictionary<string, string>
                                {
                                    {"accountantname", taxPartnerAgent.Name},
                                    {"emailaddress", account.EmailAddress},
                                };

            return ConstructAndSendMessage(accountantEmail, variables, messageType,
                string.Format("{0} has activated their account", account.DisplayName), "NotifyActivationToAccountant", account.DisplayName);
        }

        public static bool SendExecutorKitReminderEmail(MessageType messageType, Account account)
        {
            var email = account.EmailAddress;
            var variables = new Dictionary<string, string>
            {
            };

            return ConstructAndSendMessage(email, variables, messageType,
                string.Format("Upgrade to finish executor kit", account.DisplayName), "UpgradeToFinishExecutorKit", account.DisplayName);
        }

        public static bool SendSortedReviewNotificationToBookkeeper(MessageType messageType, SortedReview sortedReview)
        {
            var variables = new Dictionary<string, string>
            {
                {"bookkeeperName", sortedReview.BookkeeperAccount.DisplayName},
                {"clientName", sortedReview.ClientAccount.DisplayName},
                {"sortedReviewDate", sortedReview.NextReviewDate.ToShortDateString()}
            };

            return ConstructAndSendMessage(sortedReview.BookkeeperAccount.EmailAddress, variables, messageType, "You’ve been assigned to complete a financial review", "SortedReviewNotification", GetUserDisplayName(sortedReview.BookkeeperAccount));
        }

        public static bool SendReassignSortedReviewNotificationToDefaultAgent(MessageType messageType, Account defaultAgentAccount, Account accountToBeDeleted,
            List<SortedReview> sortedReviews)
        {
            if (sortedReviews.Count <= 0 || defaultAgentAccount == null) return false;

            var sbSortedReviews = new StringBuilder();

            foreach (var sortedReview in sortedReviews)
            {
                var strDueDate = string.Format("{0}{1}", sortedReview.NextReviewDate.ToShortDateString(),
                    sortedReview.NextReviewDate < DateTime.Now.Date
                        ? "<span style='color: red;'> (overdue)</span>"
                        : string.Empty);

                sbSortedReviews.Append(string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td></tr>",
                    FormatHelper.Ellipsify(sortedReview.ClientAccount.DisplayName, 20),
                    FormatHelper.Ellipsify(sortedReview.ClientAccount.EmailAddress, 27), strDueDate));

            }

            var variables = new Dictionary<string, string>
            {
                {"defaultAgentName", defaultAgentAccount.DisplayName},
                {"accountToBeDeletedName", accountToBeDeleted.DisplayName},
                {RAW_HTML_EMAIL_MARKUP + "sortedreview", sbSortedReviews.ToString()}

            };

            return ConstructAndSendMessage(defaultAgentAccount.EmailAddress, variables, messageType, "Please reassign financial review(s)", "ReassignSortedReviewNotification", GetUserDisplayName(defaultAgentAccount));
        }

        public static bool SendNewAdviserAddedNotification(string relationship, Account inviterAccount, Account clientAccount, Account inviteeAccount, bool addedBefore, string templateName, string subject)
        {
            var plan = inviterAccount.CobrandToUse != null &&
                       inviterAccount.CobrandToUse.Subscription != null &&
                       inviterAccount.CobrandToUse.Subscription.Plan != null
                ? inviterAccount.CobrandToUse.Subscription.Plan.MpPlanType.GetDescription()
                : string.Empty;

            var variables = new Dictionary<string, string>
            {
                {"relationship", relationship},
                {"plan", plan},
                {"cobrandName", inviterAccount.CobrandToUse != null ? inviterAccount.CobrandToUse.DisplayName : string.Empty},
                {"cobrandToken", inviterAccount.CobrandToUse != null ? inviterAccount.CobrandToUse.Token : string.Empty},
                {"clientName", GetUserDisplayName(clientAccount)},
                {"clientCobrandPlan", clientAccount.CobrandToUse != null &&
                clientAccount.CobrandToUse.Subscription != null &&
                clientAccount.CobrandToUse.Subscription.Plan != null ? clientAccount.CobrandToUse.Subscription.Plan.MpPlanType.GetDescription() : string.Empty},
                {"inviteeName", GetUserDisplayName(inviteeAccount)},
                {"inviteeEmail", inviteeAccount.EmailAddress},
                {"addedBefore", addedBefore ? "Yes" : "No"}
            };

            return ConstructAndSendMessage("info@myprosperity.com.au", variables, MessageType.Email, subject, templateName, "Team");
        }

        public static bool SendBankAccountFailNotificationToClient(MessageType messageType, Account clientAccount)
        {

            var clientName = GetUserDisplayName(clientAccount);
            var clientEmail = clientAccount.EmailAddress;

            var variables = new Dictionary<string, string>
            {
                {"clientName", clientName},
                {"clientEmail", clientEmail },
            };

            var subject = ConfigHelper.TryGetOrDefault("SendBankAccountFailNotificationSubject", "Adding Bank Account Failed");

            return ConstructAndSendMessage(clientAccount.EmailAddress, variables, messageType, subject, "SendBankAccountFailNotificationToClient", clientName);
        }

        public static bool SendBankAccountFailNotificationToAdvisor(MessageType messageType, Account clientAccount, TaxPartnerAgent taxPartnerAgent)
        {

            var accountantName = taxPartnerAgent.Name;
            var accountantEmailAddress = taxPartnerAgent.Email;
            var clientName = GetUserDisplayName(clientAccount);
            var clientEmail = clientAccount.EmailAddress;

            var variables = new Dictionary<string, string>
            {
                {"accountant", accountantName},
                {"accountantEmail",accountantEmailAddress },
                {"clientName", clientName},
                {"clientEmail", clientEmail },
            };

            var subject = ConfigHelper.TryGetOrDefault("SendBankAccountFailNotificationSubject", "Adding Bank Account Failed");

            return ConstructAndSendMessage(accountantEmailAddress, variables, messageType, subject, "SendBankAccountFailNotificationToAdvisor", clientName);
        }
    }

    public class ApplicationNotificationParameters
    {
        public Dictionary<string, string> ParameterDictionary { get; set; }

        public ApplicationNotificationParameters(Dictionary<string, string> dictionary, string emailAddress,
            string subject, string username, string templateName, out string fromEmailAddress, out string fromName)
        {
            var cobrandService = ObjectFactory.GetInstance<ICobrandService>();
            var cobrandEmailVariables = cobrandService.GetCobrandEmailTemplateVariables(emailAddress, out fromEmailAddress, out fromName);

            PopulateParameterDictionary(dictionary, subject, username, templateName, cobrandEmailVariables, emailAddress);
        }

        private void PopulateParameterDictionary(Dictionary<string, string> dictionary, string subject, string username, string templateName,
            IDictionary<string, string> cobrandEmailVariables, string toAddress)
        {
            if (dictionary == null)
                dictionary = new Dictionary<string, string>();
            ParameterDictionary = dictionary;

            if (cobrandEmailVariables.ContainsKey("signature"))
                ParameterDictionary.AddOrUpdate("signature", cobrandEmailVariables["signature"]);

            if (cobrandEmailVariables.ContainsKey("supportemail"))
                ParameterDictionary.AddOrUpdate("supportemail", cobrandEmailVariables["supportemail"]);

            if (cobrandEmailVariables.ContainsKey("headerlogo"))
                ParameterDictionary.AddOrUpdate("headerlogo", cobrandEmailVariables["headerlogo"]);

            if (cobrandEmailVariables.ContainsKey("headerstyle"))
                ParameterDictionary.AddOrUpdate("headerstyle", cobrandEmailVariables["headerstyle"]);

            if (cobrandEmailVariables.ContainsKey("fromemail"))
                ParameterDictionary.AddOrUpdate("fromemail", cobrandEmailVariables["fromemail"]);

            if (cobrandEmailVariables.ContainsKey("BaseHref"))
            {
                // annoyingly some email templates use BaseHref, some use BaseUrl as the NotificationBaseUrl placeholder
                ParameterDictionary.AddOrUpdate("BaseHref", cobrandEmailVariables["BaseHref"]);
                ParameterDictionary.AddOrUpdate("BaseUrl", cobrandEmailVariables["BaseHref"]);
            }

            if (cobrandEmailVariables.ContainsKey("cobrandparam"))
                ParameterDictionary.AddOrUpdate("cobrandparam", cobrandEmailVariables["cobrandparam"]);

            if (cobrandEmailVariables.ContainsKey("cobrandtoken"))
                ParameterDictionary.AddOrUpdate("cobrandtoken", cobrandEmailVariables["cobrandtoken"]);

            if (cobrandEmailVariables.ContainsKey("cobranddisplayname"))
                ParameterDictionary.AddOrUpdate("cobranddisplayname", cobrandEmailVariables["cobranddisplayname"]);

            if (cobrandEmailVariables.ContainsKey("cobrandfootername"))
                ParameterDictionary.AddOrUpdate("cobrandfootername", cobrandEmailVariables["cobrandfootername"]);

            if (cobrandEmailVariables.ContainsKey("iOSAppDownloadMessageDisplay"))
                ParameterDictionary.AddOrUpdate("iOSAppDownloadMessageDisplay", cobrandEmailVariables["iOSAppDownloadMessageDisplay"]);

            if (cobrandEmailVariables.ContainsKey("iOSAppDownloadLink"))
                ParameterDictionary.AddOrUpdate("iOSAppDownloadLink", cobrandEmailVariables["iOSAppDownloadLink"]);

            if (cobrandEmailVariables.ContainsKey("androidAppDownloadMessageDisplay"))
                ParameterDictionary.AddOrUpdate("androidAppDownloadMessageDisplay", cobrandEmailVariables["androidAppDownloadMessageDisplay"]);

            if (cobrandEmailVariables.ContainsKey("androidAppDownloadLink"))
                ParameterDictionary.AddOrUpdate("androidAppDownloadLink", cobrandEmailVariables["androidAppDownloadLink"]);

            // annoyingly some email templates use uppercase and some use lowercase as the "Subject" placeholder
            ParameterDictionary.AddOrUpdate("subject", subject);
            ParameterDictionary.AddOrUpdate("Subject", subject);

            // annoyingly some email templates use uppercase and some use lowercase as the "username"/"userName" placeholder
            ParameterDictionary.AddOrUpdate("username", username);
            ParameterDictionary.AddOrUpdate("userName", username);

            var gifRequestUrl = ApplicationNotification.GetGifRequest(templateName);
            ParameterDictionary.AddOrUpdate("gifRequestUrl", gifRequestUrl);
            ParameterDictionary.AddOrUpdate("GIFRequestUrl", gifRequestUrl);
            ParameterDictionary.AddOrUpdate("email", toAddress);
        }
    }

}
