using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Data;
using Data.Enumerations.Notifications;
using Data.Model;
using MyProsperity.Business.Interfaces;
using MyProsperity.Framework;
using MyProsperity.Framework.Caching;
using MyProsperity.Framework.Logging;
using MyProsperity.Framework.Model.Enums;
using MyProsperity.Notification;
using MyProsperity.Notification.Messages;
using MyProsperity.Notification.TemplateEngine;
using Newtonsoft.Json;
using StructureMap;

namespace Business
{
    public class PushNotificationService : DBService, IPushNotificationService
    {
        private readonly NotificationTemplateService _notificationTemplateService;
        private readonly ITemplateEngine _templateEngine = IocHelper.Get<ITemplateEngine>();

        public PushNotificationService(DBContext context) : base(context)
        {
            _notificationTemplateService = ObjectFactory.GetInstance<NotificationTemplateService>();
        }

        public bool AddOrUpdateSnsTokenForAccount(Account account, string snsToken, MobilePlatformType mobilePlatform, DateTime? lastLoginDate = null, bool isLoggedIn = true,
            bool isActive = true, string endpointArn = null)
        {
            var endpoint = DB.SNSEndpoints.FirstOrDefault(e => e.SNSToken == snsToken);
            if (endpoint == null)
            {
                return CreateSnsEndPointForAccount(account, snsToken, mobilePlatform, out endpoint, lastLoginDate, isLoggedIn, isActive);
            }
            else
            {
                endpoint.Account = account;
                endpoint.SNSToken = snsToken;
                endpoint.MobilePlatformType = mobilePlatform;
                if (lastLoginDate.HasValue)
                    endpoint.LastLoginDate = lastLoginDate.Value;
                endpoint.IsLoggedIn = isLoggedIn;
                endpoint.IsActive = isActive;

                try
                {
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

        public bool CreateSnsEndPointForAccount(Account account, string snsToken, MobilePlatformType mobilePlatform, out SNSEndpoint snsEndpoint,
            DateTime? lastLoginDate = null, bool isLoggedIn = true, bool isActive = true)
        {
            snsEndpoint = new SNSEndpoint
            {
                Account = account,
                CreateDate = DateTime.Now,
                IsActive = isActive,
                IsLoggedIn = isLoggedIn,
                LastLoginDate = lastLoginDate,
                MobilePlatformType = mobilePlatform,
                SNSToken = snsToken,
            };

            try
            {
                DB.SNSEndpoints.Add(snsEndpoint);
                DB.SaveChanges();
                return true;
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex);
                return false;
            }
        }

        /// <summary>
        /// Asynchronously sending of notification
        /// </summary>
        /// <param name="notificationTemplateName"></param>
        /// <param name="endpoints"></param>
        /// <param name="type"></param>
        /// <param name="url"></param>
        /// <param name="variables"></param>
        /// <returns></returns>
        public bool Send(string notificationTemplateName, IList<SNSEndpoint> endpoints, SNSType type, string url = null, Dictionary<string, string> variables = null)
        {
            NotificationTemplate template = _notificationTemplateService.GetTemplate(notificationTemplateName, MessageType.Notification,
                            FormatType.Text);
            var freshTemplate = new NotificationTemplate()
            {
                DateCreated = template.DateCreated,
                TemplateName = template.TemplateName,
                Description = template.Description,
                TemplateBody = template.TemplateBody,
                MessageType = template.MessageType,
                MessageTypeInternal = template.MessageTypeInternal,
                FormatType = template.FormatType,
                FormatTypeInternal = template.FormatTypeInternal,
                TemplatePlaceholderStrategy = template.TemplatePlaceholderStrategy,
                TemplatePlaceholderStrategyEnum = template.TemplatePlaceholderStrategyEnum,
                SubjectTitle = template.SubjectTitle
            };
            if (variables != null)
            {
                Hashtable vars = new Hashtable();
                foreach (string index in variables.Keys)
                    vars[(object)index] = (object)variables[index];
                freshTemplate.TemplateBody = _templateEngine.Render(template.TemplateBody, vars);
            }
            else
            {
                freshTemplate.TemplateBody = notificationTemplateName;
            }

            SendNotificationAsync(freshTemplate, endpoints.Where(_ => _.IsActive && _.IsLoggedIn).ToList(), type, url);
            return true;
        }

        private void SendNotificationAsync(NotificationTemplate template, IList<SNSEndpoint> endpoints, SNSType type, string url = null )
        {
            Parallel.ForEach(endpoints,
                endpoint =>
                {
                    new Thread(() =>
                    {
                        SNSMessage message = new SNSMessage(template, endpoint.SNSToken, type, url);
                        var jsonObj = message.GetNotificationJson();
                        SendNotification(jsonObj, endpoint, 3);
                    }).Start();
                });
        }

        private void SendNotification(string jsonObj, SNSEndpoint endpoint, int retries)
        {
            if (retries == 0)
                return;
          
            try
            {
                var tRequest = WebRequest.Create(ConfigHelper.TryGetOrDefault("FCMWebRequestUri", "https://fcm.googleapis.com/fcm/send"));
                tRequest.Method = "post";
                tRequest.ContentType = "application/json";

                var byteArray = Encoding.UTF8.GetBytes(jsonObj);
                tRequest.Headers.Add(string.Format("Authorization: key={0}", ConfigHelper.TryGetOrDefault("FCMWebRequestAuthorizationToken", "AIzaSyDPr3u0jzTYoNzmrauUprXbvd9su4D4lgk")));
                tRequest.ContentLength = byteArray.Length;
                using (var dataStream = tRequest.GetRequestStream())
                {
                    dataStream.Write(byteArray, 0, byteArray.Length);

                    try
                    {
                        using (var tResponse = tRequest.GetResponse())
                        {
                            using (var dataStreamResponse = tResponse.GetResponseStream())
                            {
                                using (var tReader = new StreamReader(dataStreamResponse))
                                {
                                    var responseFromServer = tReader.ReadToEnd();
                                    var response = JsonConvert.DeserializeObject<FCMResponse>(responseFromServer);
                                    if (response.success == 1)
                                        LogHelper.LogInfo(
                                            string.Format("SNS message Id: {0}. Success.",
                                                response.results[0].message_id),
                                            "SNS.SendNotification");
                                    else if (response.failure == 1)
                                        LogHelper.LogError(
                                            string.Format("SNS token: {0}. Failure.",
                                                endpoint),
                                            "SNS.SendNotification");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        retries--;

                        LogHelper.LogError(
                            string.Format("SNS token: {0}. Failure. {1} Attempt Left. Message: {2}",
                                endpoint, retries, ex.Message),
                            "SNS.SendNotificationAsync");

                        Thread.Sleep(1000);
                        SendNotification(jsonObj, endpoint, retries);
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionHelper.Log(ex, "SNS.SendNotificationAsync");
            }
        }

    }
}