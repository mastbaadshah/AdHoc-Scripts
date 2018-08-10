using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MyProsperity.Framework.Model.Enums;

namespace Data.Model
{
    public class SNSEndpoint
    {
        [Key]
        public int ID { get; set; }

        public virtual Account Account { get; set; }

        public string EndpointARN { get; set; }

        public string SNSToken { get; set; }

        public bool IsLoggedIn { get; set; }

        public DateTime? LastLoginDate { get; set; }

        public int MobilePlatformTypeInternal { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreateDate { get; set; }

        [NotMapped]
        public string SnsEndpointAuthKey { get; set; }

        [NotMapped]
        public MobilePlatformType MobilePlatformType
        {
            get
            {
                return (MobilePlatformType)MobilePlatformTypeInternal;
            }
            set
            {
                MobilePlatformTypeInternal = (int)value;
            }
        }
    }
}
